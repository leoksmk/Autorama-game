// Geometria 3D do traçado: converte o t da regra (Core.Pista) em posição e
// orientação no espaço.
//
// A forma não é mais uma elipse modulada. O leito é uma B-SPLINE CÚBICA
// FECHADA passando pelo polígono de controle abaixo, que desenha um circuito
// de verdade: reta principal, o S (esquerda rápida, direita fechada, em
// descida), o curvão do leste, a reta oposta no fundo do vale e a subida do
// zênite voltando para a largada.
//
// Por que B-spline e não Catmull-Rom: a inclinação lateral das curvas é
// calculada a partir da CURVATURA do traçado. Catmull-Rom é contínua só na
// primeira derivada, então a curvatura salta em cada ponto de controle e a
// pista ficaria com dobras de inclinação visíveis. A B-spline cúbica uniforme
// é C², a curvatura varia sem degrau, e a inclinação sai lisa de graça. O
// preço é que a curva não passa pelos pontos de controle — ela é puxada para
// dentro do polígono —, o que não importa: a forma é autoral, não interpolada.
//
// O t é REPARAMETRIZADO POR COMPRIMENTO DE ARCO: t = 0,25 é sempre um quarto
// da volta em METROS, não um quarto do parâmetro da spline. Sem isso a nave
// acelararia e frearia sozinha onde os pontos de controle são mais densos, e
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
    public const float OffsetFaixa = 2.6f;     // do centro até cada faixa, m
    public const float Largura = 11f;          // largura total do leito, m
    public const float AlturaVoo = 0.62f;      // a nave flutua acima do leito

    // Inclinação lateral máxima nas curvas. Alta de propósito: a 220 km/h numa
    // curva de 16 m de raio, leito plano não se sustenta nem como ficção.
    public const float InclinacaoMax = 0.85f;  // rad (~49°)
    // Raio (m) em que a curva já vale meia inclinação. Em metros, e não no
    // parâmetro da curva, para a inclinação não mudar quando o traçado crescer.
    private const float RaioDeReferencia = 18f;

    /// <summary>
    /// Polígono de controle da volta, em ordem de percurso: (x, altura, z).
    ///
    /// t = 0 — a linha de largada — cai no meio da reta principal, de propósito:
    /// a bandeira quadriculada numa curva fica ilegível de qualquer câmera.
    /// </summary>
    private static readonly Vector3[] Controle =
    {
        // -- reta principal (borda sul), rumo leste, subindo até a entrada do S
        new(-18f, 5.6f, 45f), new(4f, 6.3f, 45f), new(26f, 6.4f, 45f),

        // -- O S: esquerda longa e rápida, direita curta e mais fechada, saída
        //    abrindo de novo à esquerda. Os três em descida, um atrás do outro.
        new(47f, 5.4f, 44f),
        new(62f, 3.4f, 39f),     // entra virando à esquerda
        new(69f, 1.0f, 27f),     // ápice da esquerda
        new(67f, -1.2f, 15f),
        new(58f, -3.0f, 7f),     // inverte: agora é direita, e o leito cai
        new(57f, -4.6f, -5f),    // ápice da direita, mais fechado que o da esquerda
        new(64f, -5.9f, -16f),   // saída, abrindo à esquerda de novo

        // -- curvão do leste, já no ponto mais baixo da volta
        new(66f, -6.8f, -28f), new(58f, -7.2f, -39f), new(43f, -7.4f, -47f),

        // -- reta oposta (borda norte), rumo oeste, no fundo do vale
        new(23f, -7.4f, -49f), new(0f, -7.4f, -49f), new(-22f, -7.0f, -48f),

        // -- subida do zênite: uma esquerda só, que fecha no ápice e abre na saída
        new(-42f, -6.0f, -42f), new(-58f, -4.4f, -27f), new(-62f, -2.4f, -10f),
        new(-61f, -0.2f, 5f), new(-58f, 2.0f, 20f), new(-49f, 3.6f, 34f),
        new(-40f, 4.6f, 45f),
    };

    /// <summary>Pista 0 por dentro, pista 1 por fora — igual à versão 2D.</summary>
    public static float OffsetDaFaixa(int lane) => lane == 0 ? -OffsetFaixa : OffsetFaixa;

    // -- a curva ----------------------------------------------------------------

    /// <summary>
    /// B-spline cúbica uniforme fechada, no parâmetro BRUTO p ∈ [0, n). Não use
    /// isto direto: p não anda em metros. <see cref="Centro"/> é a entrada boa.
    /// </summary>
    private static Vector3 Forma(double p)
    {
        int n = Controle.Length;
        double x = p - Math.Floor(p / n) * n;
        int i = (int)x;
        float u = (float)(x - i);

        Vector3 a = Controle[(i - 1 + n) % n];
        Vector3 b = Controle[i];
        Vector3 c = Controle[(i + 1) % n];
        Vector3 d = Controle[(i + 2) % n];

        float u2 = u * u, u3 = u2 * u;
        return (a * (-u3 + 3f * u2 - 3f * u + 1f)
              + b * (3f * u3 - 6f * u2 + 4f)
              + c * (-3f * u3 + 3f * u2 + 3f * u + 1f)
              + d * u3) / 6f;
    }

    /// <summary>Ponto da linha de centro a t da volta, medido em COMPRIMENTO.</summary>
    public static Vector3 Centro(double t) => Forma(ParametroEm(Pista.Mod1(t)));

    private static Vector3 Tangente(double t)
    {
        const double h = 2e-4;   // ~0,08 m: curto para acompanhar a curva, longo para não virar ruído
        return (Centro(t + h) - Centro(t - h)).Normalized();
    }

    /// <summary>
    /// Curvatura horizontal com sinal, em 1/m. Positiva quando a pista vira à
    /// esquerda. Em metros, e não no parâmetro, para não depender do tamanho
    /// do circuito.
    /// </summary>
    private static float Curvatura(double t)
    {
        const double h = 1.5e-3;
        return Tangente(t - h).Cross(Tangente(t + h)).Y / (float)(2 * h * Comprimento);
    }

    /// <summary>Inclinação lateral do leito em t, em radianos. Zero na reta.</summary>
    public static float Inclinacao(double t) =>
        InclinacaoMax * MathF.Tanh(MathF.Abs(Curvatura(t)) * RaioDeReferencia);

    /// <summary>
    /// Quadro local num ponto da volta: X à direita, Y para cima (já inclinado
    /// na curva), -Z para a frente. <paramref name="offset"/> positivo afasta do
    /// centro da órbita; <paramref name="altura"/> sobe pela normal do leito.
    /// </summary>
    public static Transform3D Quadro(double t, float offset = 0f, float altura = 0f)
    {
        t = Pista.Mod1(t);
        Vector3 p = Centro(t);
        Vector3 frente = Tangente(t);
        Vector3 cimaReto = (Vector3.Up - frente * frente.Dot(Vector3.Up)).Normalized();
        Vector3 esquerda = cimaReto.Cross(frente).Normalized();

        float curva = Curvatura(t);
        float banco = InclinacaoMax * MathF.Tanh(MathF.Abs(curva) * RaioDeReferencia);
        // A normal do leito tomba para DENTRO da curva: é o que segura a nave.
        Vector3 cima = (cimaReto + esquerda * MathF.Sign(curva) * MathF.Tan(banco)).Normalized();
        Vector3 direita = frente.Cross(cima).Normalized();

        Vector3 pos = p + Fora(direita, p) * offset + cima * altura;
        return new Transform3D(new Basis(direita, cima, -frente), pos);
    }

    /// <summary>Direção lateral que aponta para fora do circuito, no plano inclinado.</summary>
    public static Vector3 Fora(Transform3D quadro) => Fora(quadro.Basis.X, quadro.Origin);

    private static Vector3 Fora(Vector3 direita, Vector3 p)
    {
        Vector3 radial = new(p.X, 0f, p.Z);
        return direita.Dot(radial) >= 0f ? direita : -direita;
    }

    // -- comprimento de arco e reparametrização ---------------------------------

    private const int Amostras = 4096;
    private static float[]? _parametro;    // parâmetro bruto na fração de volta j/Amostras
    private static float _comprimento;

    public static float Comprimento
    {
        get
        {
            Preparar();
            return _comprimento;
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
    /// metros → bruto. Preguiçosa porque <see cref="Curvatura"/> precisa do
    /// comprimento e o comprimento precisa da forma — e a forma não depende de
    /// nenhum dos dois, então a recursão para aqui.
    /// </summary>
    private static void Preparar()
    {
        if (_parametro is not null)
            return;

        int n = Controle.Length;
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
    }
}
