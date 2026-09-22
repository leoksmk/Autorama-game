// Geometria 3D do traçado: converte o t da regra (Core.Pista) em posição e
// orientação no espaço, para o circuito que estiver em uso.
//
// O leito é uma B-SPLINE CÚBICA FECHADA sobre o polígono de controle do
// circuito (ver Circuitos.cs). B-spline e não Catmull-Rom porque a inclinação
// lateral vem da CURVATURA: Catmull-Rom é contínua só na primeira derivada,
// então a curvatura salta em cada ponto de controle e a pista ganharia dobras
// de inclinação visíveis. A cúbica uniforme é C² e a inclinação sai lisa.
//
// O t é REPARAMETRIZADO POR COMPRIMENTO DE ARCO: t = 0,25 é sempre um quarto
// da volta em METROS, não um quarto do parâmetro da spline. Sem isso a nave
// aceleraria e frearia sozinha onde os pontos de controle são mais densos, e
// pior: os checkpoints em t deixariam de corresponder a distâncias fixas na
// pista, que é exatamente o que os sensores físicos vão medir.
//
// Só o desenho usa isto. A regra continua raciocinando em t normalizado.

using System;
using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby.Mundo;

public static class Tracado
{
    public const float AlturaVoo = 0.62f;      // a nave flutua acima do leito

    private static Circuito _circuito = Circuitos.Icaro;

    /// <summary>
    /// Troca o circuito e joga fora as tabelas medidas. Quem chama precisa
    /// reconstruir tudo que foi gerado a partir da geometria — pista, pedras,
    /// estação —, por isso a troca mora em Main.AplicarCircuito e não aqui.
    /// </summary>
    public static void Usar(Circuito circuito)
    {
        _circuito = circuito;
        _parametro = null;
        Cfg.Checkpoints = circuito.Checkpoints;
        Cfg.RitmoDoCircuito = circuito.Ritmo;
        Cfg.VoltasParaVencer = circuito.Voltas;
    }

    public static Circuito Atual => _circuito;

    public static float Largura => _circuito.Largura;
    public static float OffsetFaixa => _circuito.OffsetFaixa;
    public static float InclinacaoMax => _circuito.InclinacaoMax;

    /// <summary>Pista 0 por dentro, pista 1 por fora — igual à versão 2D.</summary>
    public static float OffsetDaFaixa(int lane) => lane == 0 ? -OffsetFaixa : OffsetFaixa;

    // -- a curva ----------------------------------------------------------------

    /// <summary>
    /// B-spline cúbica uniforme fechada, no parâmetro BRUTO p ∈ [0, n). Não use
    /// isto direto: p não anda em metros. <see cref="Centro"/> é a entrada boa.
    /// </summary>
    private static Vector3 Forma(double p)
    {
        var (a, b, c, d, u) = Vizinhos(p);
        float u2 = u * u, u3 = u2 * u;
        return (a * (-u3 + 3f * u2 - 3f * u + 1f)
              + b * (3f * u3 - 6f * u2 + 4f)
              + c * (-3f * u3 + 3f * u2 + 3f * u + 1f)
              + d * u3) / 6f;
    }

    /// <summary>Primeira e segunda derivadas da spline no parâmetro bruto.</summary>
    private static (Vector3 d1, Vector3 d2) Derivadas(double p)
    {
        var (a, b, c, d, u) = Vizinhos(p);
        float u2 = u * u;
        var d1 = (a * (-3f * u2 + 6f * u - 3f)
                + b * (9f * u2 - 12f * u)
                + c * (-9f * u2 + 6f * u + 3f)
                + d * (3f * u2)) / 6f;
        var d2 = (a * (-6f * u + 6f)
                + b * (18f * u - 12f)
                + c * (-18f * u + 6f)
                + d * (6f * u)) / 6f;
        return (d1, d2);
    }

    private static (Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u) Vizinhos(double p)
    {
        var pts = _circuito.Controle;
        int n = pts.Length;
        double x = p - Math.Floor(p / n) * n;
        int i = (int)x;
        return (pts[(i - 1 + n) % n], pts[i], pts[(i + 1) % n], pts[(i + 2) % n], (float)(x - i));
    }

    /// <summary>Ponto da linha de centro a t da volta, medido em COMPRIMENTO.</summary>
    public static Vector3 Centro(double t) => Forma(ParametroEm(Pista.Mod1(t)));

    /// <summary>
    /// Tangente unitária em t. Analítica, e não por diferença finita: a tabela
    /// de reparametrização é linear por pedaço, então derivar numericamente por
    /// cima dela injeta ruído na curvatura — e é a curvatura que acende as
    /// zebras e decide a inclinação.
    /// </summary>
    private static Vector3 Tangente(double t) => Derivadas(ParametroEm(Pista.Mod1(t))).d1.Normalized();

    /// <summary>
    /// Curvatura horizontal com sinal, em 1/m. Positiva quando a pista vira à
    /// esquerda. Em metros, e não no parâmetro, para não depender do tamanho do
    /// circuito: a mesma curva fechada inclina igual em qualquer pista.
    /// </summary>
    private static float Curvatura(double t)
    {
        var (d1, d2) = Derivadas(ParametroEm(Pista.Mod1(t)));
        float den = MathF.Pow(d1.X * d1.X + d1.Z * d1.Z, 1.5f);
        return den > 1e-9f ? (d1.Z * d2.X - d1.X * d2.Z) / den : 0f;
    }

    /// <summary>Inclinação lateral do leito em t, em radianos. Zero na reta.</summary>
    public static float Inclinacao(double t) =>
        InclinacaoMax * MathF.Tanh(MathF.Abs(Curvatura(t)) * _circuito.RaioDeReferencia);

    /// <summary>
    /// Quadro local num ponto da volta: X à direita, Y para cima (já inclinado
    /// na curva), -Z para a frente. <paramref name="offset"/> positivo afasta do
    /// centro do circuito; <paramref name="altura"/> sobe pela normal do leito.
    /// </summary>
    public static Transform3D Quadro(double t, float offset = 0f, float altura = 0f)
    {
        t = Pista.Mod1(t);
        Vector3 p = Centro(t);
        Vector3 frente = Tangente(t);
        Vector3 cimaReto = (Vector3.Up - frente * frente.Dot(Vector3.Up)).Normalized();
        Vector3 esquerda = cimaReto.Cross(frente).Normalized();

        float curva = Curvatura(t);
        float banco = InclinacaoMax * MathF.Tanh(MathF.Abs(curva) * _circuito.RaioDeReferencia);
        // A normal do leito tomba para DENTRO da curva: é o que segura a nave.
        Vector3 cima = (cimaReto + esquerda * MathF.Sign(curva) * MathF.Tan(banco)).Normalized();
        Vector3 direita = frente.Cross(cima).Normalized();

        Vector3 pos = p + direita * (LadoDeFora * offset) + cima * altura;
        return new Transform3D(new Basis(direita, cima, -frente), pos);
    }

    /// <summary>
    /// Direção lateral que aponta para fora do circuito, no plano inclinado.
    ///
    /// É um LADO FIXO do sentido de percurso, decidido uma vez por circuito, e
    /// não "o lado oposto ao centro do mundo". A diferença aparece no
    /// INTERLAGOS ORBITAL, cujo miolo passa pelo meio do circuito: ali a regra
    /// radial inverteria no meio da volta, as duas faixas trocariam de lugar e
    /// o leito ganharia uma emenda de cor no ponto da inversão.
    /// </summary>
    public static Vector3 Fora(Transform3D quadro) => quadro.Basis.X * LadoDeFora;

    // -- comprimento de arco e reparametrização ---------------------------------

    private const int Amostras = 4096;
    private static float[]? _parametro;    // parâmetro bruto na fração de volta j/Amostras
    private static float _comprimento;
    private static float _ladoDeFora = 1f;
    private static Aabb _caixa;

    public static float Comprimento
    {
        get
        {
            Preparar();
            return _comprimento;
        }
    }

    /// <summary>
    /// Caixa que contém a volta inteira. É o que a câmera usa para enquadrar:
    /// os circuitos têm tamanhos diferentes, e um enquadramento fixo ou corta o
    /// INTERLAGOS ORBITAL ou deixa a ÓRBITA CLÁSSICA perdida no meio da tela.
    /// </summary>
    public static Aabb Caixa
    {
        get
        {
            Preparar();
            return _caixa;
        }
    }

    private static float LadoDeFora
    {
        get
        {
            Preparar();
            return _ladoDeFora;
        }
    }

    /// <summary>Metros percorridos desde a linha de largada até t.</summary>
    public static float Distancia(double t) => (float)Pista.Mod1(t) * Comprimento;

    /// <summary>Parâmetro bruto da spline no ponto a t da volta, em comprimento.</summary>
    private static float ParametroEm(double t)
    {
        Preparar();
        double x = t * Amostras;
        int j = Math.Min((int)x, Amostras - 1);
        return Mathf.Lerp(_parametro![j], _parametro[j + 1], (float)(x - j));
    }

    /// <summary>
    /// Mede a volta uma vez e monta as duas tabelas: bruto → metros e, invertida,
    /// metros → bruto. Depois decide de que lado fica o "fora" do circuito.
    ///
    /// Tudo aqui usa só <see cref="Forma"/> e <see cref="Derivadas"/>, que não
    /// dependem das tabelas — é o que impede a recursão, já que Curvatura e
    /// Quadro dependem delas.
    /// </summary>
    private static void Preparar()
    {
        if (_parametro is not null)
            return;

        int n = _circuito.Controle.Length;
        var acum = new float[Amostras + 1];
        Vector3 anterior = Forma(0.0);
        for (int i = 1; i <= Amostras; i++)
        {
            Vector3 atual = Forma((double)i * n / Amostras);
            acum[i] = acum[i - 1] + anterior.DistanceTo(atual);
            anterior = atual;
        }
        _comprimento = acum[Amostras];

        // Inversão: para cada fatia igual de METROS, qual parâmetro bruto.
        var par = new float[Amostras + 1];
        int k = 0;
        for (int j = 0; j <= Amostras; j++)
        {
            float alvo = _comprimento * j / Amostras;
            while (k < Amostras && acum[k + 1] < alvo)
                k++;
            float d0 = acum[k], d1 = acum[Math.Min(k + 1, Amostras)];
            float f = d1 > d0 ? (alvo - d0) / (d1 - d0) : 0f;
            par[j] = (float)((k + f) * n / (double)Amostras);
        }
        par[Amostras] = n;
        _parametro = par;

        _ladoDeFora = MedirLadoDeFora();

        Vector3 min = Forma(0.0), max = min;
        for (int i = 1; i < Amostras; i++)
        {
            Vector3 v = Forma((double)i * n / Amostras);
            min = min.Min(v);
            max = max.Max(v);
        }
        _caixa = new Aabb(min, max - min);
    }

    /// <summary>
    /// De que lado do sentido de percurso fica o lado de fora, +1 ou -1.
    ///
    /// Vota ponto a ponto — "a direita do carro aponta para longe do centroide
    /// do circuito?" — e o voto pesa pela distância ao centroide, para que as
    /// retas externas decidam e o miolo, que fica perto do centroide e onde o
    /// sinal é ambíguo, quase não conte.
    /// </summary>
    private static float MedirLadoDeFora()
    {
        const int amostras = 720;
        Vector3 centroide = Vector3.Zero;
        for (int i = 0; i < amostras; i++)
            centroide += Forma((double)i * _circuito.Controle.Length / amostras);
        centroide /= amostras;

        float voto = 0f;
        for (int i = 0; i < amostras; i++)
        {
            double p = (double)i * _circuito.Controle.Length / amostras;
            Vector3 pos = Forma(p);
            Vector3 frente = Derivadas(p).d1.Normalized();
            Vector3 cima = (Vector3.Up - frente * frente.Dot(Vector3.Up)).Normalized();
            Vector3 direita = frente.Cross(cima).Normalized();
            Vector3 radial = new(pos.X - centroide.X, 0f, pos.Z - centroide.Z);
            voto += direita.Dot(radial);   // o próprio produto já pesa pela distância
        }
        return voto >= 0f ? 1f : -1f;
    }
}
