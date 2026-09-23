// O catálogo de circuitos. Ordem da lista = ordem no menu.
//
// Os números de cada um (comprimento, raio mínimo, inclinação, rampa) foram
// medidos, não estimados, e dá para remedir a qualquer momento com
// `jogar.bat --medir-pistas`, que mede ESTE código e não uma cópia dele.

using System;
using System.Collections.Generic;
using Godot;

namespace OrbitalDerby.Mundo;

public static class Circuitos
{
    /// <summary>
    /// O traçado bruto do ANEL DE ÍCARO, como nasceu. É a base de duas pistas:
    /// esta e a suave, que é ela mesma filtrada.
    /// </summary>
    private static readonly Vector3[] IcaroBruto =
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

    /// <summary>
    /// ANEL DE ÍCARO — o padrão. É o traçado bruto passado por duas passagens
    /// de média móvel, alargado 7% para a volta continuar com 387 m, e com o
    /// desnível reduzido a 40%.
    ///
    /// O que mudou da bruta, medido: inclinação máxima de 42° para 22°, rampa
    /// de 19% para 7,6%, desnível de 13,7 m para 5,3 m, raio mínimo de 14,0 m
    /// para 16,5 m. A volta e a velocidade em tela não mudaram.
    /// </summary>
    public static readonly Circuito Icaro = new()
    {
        Nome = "ANEL DE ÍCARO",
        Resumo = "387 m · o S no leste, subida longa no oeste · 9,6 s por volta",
        Controle = Circuito.Suavizar(IcaroBruto, 2, escalaXZ: 1.07f, escalaY: 0.40f),
        Checkpoints = new[] { 0.14, 0.35, 0.60, 0.83 },
        Ritmo = 0.65,
        InclinacaoMax = 0.42f,
        RaioDeReferencia = 26f,
    };

    /// <summary>
    /// ANEL DE ÍCARO (BRUTO) — a primeira versão, guardada de propósito.
    /// Curvas mais fechadas, 42° de inclinação e 19% de rampa: é a pista de
    /// montanha-russa. Fica porque agrada quem quer exagero, e porque é a
    /// referência de onde a suave veio.
    /// </summary>
    public static readonly Circuito IcaroOriginal = new()
    {
        Nome = "ÍCARO BRUTO",
        Resumo = "387 m · o mesmo desenho sem filtro: 42° de banco, 19% de rampa",
        Controle = IcaroBruto,
        Checkpoints = new[] { 0.14, 0.35, 0.60, 0.83 },
        Ritmo = 0.65,
        InclinacaoMax = 0.85f,
        RaioDeReferencia = 18f,
    };

    /// <summary>
    /// INTERLAGOS ORBITAL — homenagem ao traçado de Interlagos, com os mesmos
    /// marcos na mesma ordem: reta dos boxes, o S, Curva do Sol, Reta Oposta,
    /// Descida do Lago, Ferradura, Pinheirinho, Bico de Pato, Mergulho, Junção
    /// e a Subida dos Boxes.
    ///
    /// É o circuito longo (709 m) e o único com miolo: a pista entra no meio de
    /// si mesma. Por isso a ÍRIS-9 sai do centro e vai para cima — no centro ela
    /// ficaria em cima do Bico de Pato.
    ///
    /// Ritmo 0,367 porque 709 m no teto cru dariam 408 km/h. Com o fator, a
    /// volta leva 17 s a 150 km/h, e por isso são 3 voltas e não 5.
    /// </summary>
    public static readonly Circuito Interlagos = new()
    {
        Nome = "INTERLAGOS ORBITAL",
        Resumo = "709 m · anel externo e miolo lento · 17 s por volta, 3 voltas",
        Controle = new Vector3[]
        {
            // -- reta dos boxes, rumo leste, subindo
            new(-58f, 1.6f, 70f), new(-30f, 1.9f, 71f), new(-2f, 2.0f, 71f),
            // -- o S: esquerda, direita, em descida
            new(26f, 1.8f, 69f),
            new(52f, 1.3f, 63f),
            new(70f, 0.7f, 48f),
            new(71f, 0.2f, 33f),
            new(61f, -0.2f, 21f),
            new(58f, -0.5f, 6f),
            // -- Curva do Sol: esquerda longa que entrega a reta oposta
            new(70f, -0.9f, -10f), new(70f, -1.3f, -31f),
            // -- Reta Oposta, em descida, rumo oeste
            new(57f, -1.7f, -48f), new(33f, -2.1f, -60f), new(4f, -2.4f, -65f),
            // -- Descida do Lago: cai para o oeste, no ponto mais baixo
            new(-26f, -2.8f, -64f), new(-52f, -3.0f, -56f),
            new(-72f, -3.1f, -41f), new(-79f, -3.0f, -21f),
            // -- Ferradura: esquerda longa que entra no miolo
            new(-71f, -2.8f, -3f), new(-53f, -2.5f, 6f), new(-33f, -2.3f, 5f),
            // -- Pinheirinho: fecha e aponta de volta para o leste
            new(-17f, -2.1f, -5f), new(-8f, -2.0f, -21f),
            // -- Bico de Pato: o ponto mais lento, direita e esquerda coladas
            new(5f, -2.0f, -33f), new(23f, -2.2f, -31f), new(31f, -2.4f, -14f),
            // -- Mergulho: esquerda longa voltando para oeste
            new(27f, -2.6f, 5f), new(11f, -2.8f, 17f), new(-12f, -2.9f, 23f),
            // -- Junção: a curva mais lenta, no fundo do circuito
            new(-38f, -2.6f, 27f), new(-58f, -2.0f, 34f),
            // -- Subida dos Boxes: espalhada em quatro pontos para o polígono
            //    não dobrar de uma vez e criar um raio de 8 m na entrada da reta
            new(-75f, -1.0f, 44f), new(-83f, 0.2f, 55f), new(-81f, 1.1f, 65f), new(-71f, 1.6f, 71f),
        },
        // Seis, e não quatro: a volta leva 11,3 s, então com quatro cada trecho
        // durava 2,9 s — a caixa de item abria raro e o Escudo chegava a valer
        // 7 s. Com seis são ~1,9 s por trecho, o mesmo ritmo do ANEL DE ÍCARO.
        Checkpoints = new[] { 0.06, 0.23, 0.40, 0.56, 0.72, 0.88 },
        Ritmo = 0.367,
        Voltas = 3,
        Largura = 9f,            // circuito mais estreito: o miolo tem 10 m de raio
        OffsetFaixa = 2.2f,
        InclinacaoMax = 0.45f,
        RaioDeReferencia = 24f,
        // Fora do circuito, e não acima dele: pairando sobre o miolo, a estação
        // tapava justamente o Bico de Pato na câmera de visão geral.
        EstacaoPos = new Vector3(4f, 34f, 210f),
        EstacaoRaio = 34f,
    };

    /// <summary>
    /// ÓRBITA CLÁSSICA — a elipse modulada da versão 2D (r = 1 + 0,075·cos 3a),
    /// ampliada 35% para caber a ÍRIS-9 no miolo. Curta, quase plana e sem
    /// inversão: é a pista de aprender o acelerador.
    /// </summary>
    public static readonly Circuito Classica = new()
    {
        Nome = "ÓRBITA CLÁSSICA",
        Resumo = "317 m · a elipse da versão 2D · a mais curta, para aprender",
        Controle = Elipse(28, 59.4f, 39.2f, 0.075f, 3, 1.8f),
        Checkpoints = new[] { 0.12, 0.45, 0.78 },
        Ritmo = 0.69,
        InclinacaoMax = 0.30f,
        RaioDeReferencia = 30f,
        EstacaoRaio = 13f,
    };

    /// <summary>
    /// PLANTA BAIXA — o traçado do desenho técnico da pista física: 1,20 m ×
    /// 0,80 m de tampo, 3,3 m de pista. Aqui está ampliado 110×, para a nave
    /// ficar do tamanho certo em relação ao leito.
    ///
    /// É descrito por CANTOS e não por pontos soltos de spline, e essa é a
    /// diferença que importa: uma pista de autorama é montada com peças retas e
    /// peças de curva de raio fixo, então metade desta volta é reta de verdade
    /// e o resto são arcos de raio constante. Descrita como spline livre, ela
    /// saía curvando o tempo todo e com curvas abertas demais — parecida de
    /// longe, errada de perto.
    ///
    /// É também a única PLANA: altura zero em todo canto e InclinacaoMax = 0.
    /// Uma placa de MDF não tem sobrelevação nem rampa, e a graça desta pista é
    /// ser a que vai existir de fato — os checkpoints aqui são onde os sensores
    /// vão ser parafusados.
    ///
    /// Para remedir contra a placa: cada 110 m de volta no jogo é 1 m de pista
    /// no tampo.
    /// </summary>
    public static readonly Circuito PlantaBaixa = new()
    {
        Nome = "PLANTA BAIXA",
        Resumo = "365 m · o desenho da pista física, 1,20 × 0,80 m · plana, reta e travada",
        // (x, z, raio) em METROS DO TAMPO, x para a direita e z para cima na
        // planta. Sentido de percurso: para leste na reta de cima.
        Controle = Tampo(new (float, float, float)[]
        {
            (0.10f, 0.70f, 0.17f),    // canto superior esquerdo
            (1.10f, 0.70f, 0.17f),    // canto superior direito
            (1.10f, 0.13f, 0.15f),    // canto inferior direito
            (0.70f, 0.13f, 0.075f),   // entrada do degrau: sobe
            (0.70f, 0.42f, 0.075f),   // topo do degrau: vira a oeste
            (0.34f, 0.42f, 0.075f),   // fim do degrau: desce
            (0.34f, 0.13f, 0.075f),   // volta para a borda de baixo
            (0.10f, 0.13f, 0.14f),    // canto inferior esquerdo
        }),
        Checkpoints = new[] { 0.06, 0.28, 0.50, 0.72 },
        Ritmo = 0.64,
        Largura = 8f,            // mais estreita: os cantos têm 8 m de raio
        OffsetFaixa = 2.0f,
        InclinacaoMax = 0f,      // placa de MDF não tem sobrelevação
        RaioDeReferencia = 26f,
        // Fora do circuito: o meio do tampo é ocupado pelo degrau.
        EstacaoPos = new Vector3(0f, 34f, -190f),
        EstacaoRaio = 32f,
    };

    public static readonly Circuito[] Todos = { Icaro, PlantaBaixa, Interlagos, IcaroOriginal, Classica };

    public static Circuito PorNome(string nome)
    {
        foreach (var c in Todos)
            if (c.Nome == nome)
                return c;
        return Icaro;
    }

    public static int IndiceDe(Circuito c)
    {
        for (int i = 0; i < Todos.Length; i++)
            if (ReferenceEquals(Todos[i], c))
                return i;
        return 0;
    }

    /// <summary>
    /// Constrói o polígono de controle de uma pista descrita por CANTOS: uma
    /// polilinha fechada em que cada vértice vira um arco de raio fixo, ligado
    /// por retas. É como uma pista de autorama é de verdade, e é o que dá
    /// retas que são retas em vez de curvas muito abertas.
    ///
    /// Também converte metros do tampo em metros do jogo: centra a placa na
    /// origem, amplia 110× e espelha a profundidade, para a reta de cima do
    /// desenho ficar em cima na câmera de visão geral.
    /// </summary>
    private static Vector3[] Tampo((float x, float z, float raio)[] cantos)
    {
        const float larguraDoTampo = 1.20f, alturaDoTampo = 0.80f;
        const float escala = 132f / larguraDoTampo;
        const float passo = 0.012f;      // metros de tampo entre pontos de controle

        int n = cantos.Length;
        var vertice = new Vector2[n];
        var raio = new float[n];
        for (int i = 0; i < n; i++)
        {
            vertice[i] = new Vector2(cantos[i].x, cantos[i].z);
            raio[i] = cantos[i].raio;
        }

        EncolherRaios(vertice, raio);

        // Onde cada arco começa, termina e em volta de quê.
        var ini = new Vector2[n];
        var fim = new Vector2[n];
        var centro = new Vector2[n];
        var anguloInicial = new float[n];
        var giro = new float[n];
        for (int i = 0; i < n; i++)
        {
            var (entra, sai, curva) = Esquina(vertice, i);
            giro[i] = curva;
            if (MathF.Abs(curva) < 1e-5f)
            {
                ini[i] = fim[i] = vertice[i];
                continue;
            }
            float t = raio[i] * MathF.Abs(MathF.Tan(curva * 0.5f));
            ini[i] = vertice[i] - entra * t;
            fim[i] = vertice[i] + sai * t;
            float lado = MathF.Sign(curva);
            centro[i] = ini[i] + new Vector2(-entra.Y, entra.X) * (raio[i] * lado);
            anguloInicial[i] = (ini[i] - centro[i]).Angle();
        }

        var pts = new List<Vector3>();
        void Por(Vector2 v) =>
            pts.Add(new Vector3((v.X - larguraDoTampo * 0.5f) * escala,
                                0f,
                                (alturaDoTampo * 0.5f - v.Y) * escala));

        for (int i = 0; i < n; i++)
        {
            // Reta: do fim do arco anterior até o começo deste.
            Vector2 de = fim[(i - 1 + n) % n], ate = ini[i];
            int k = Math.Max(1, (int)(de.DistanceTo(ate) / passo));
            for (int j = 0; j < k; j++)
                Por(de.Lerp(ate, (float)j / k));

            if (MathF.Abs(giro[i]) < 1e-5f)
                continue;

            // Arco de raio fixo em volta do canto.
            float r = centro[i].DistanceTo(ini[i]);
            int a = Math.Max(2, (int)(MathF.Abs(giro[i]) * r / passo));
            for (int j = 0; j < a; j++)
            {
                float ang = anguloInicial[i] + giro[i] * ((float)j / a);
                Por(centro[i] + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r);
            }
        }
        return pts.ToArray();
    }

    /// <summary>Direção de entrada, de saída e ângulo de virada num canto.</summary>
    private static (Vector2 entra, Vector2 sai, float giro) Esquina(Vector2[] v, int i)
    {
        int n = v.Length;
        Vector2 entra = (v[i] - v[(i - 1 + n) % n]).Normalized();
        Vector2 sai = (v[(i + 1) % n] - v[i]).Normalized();
        return (entra, sai, MathF.Atan2(entra.Cross(sai), entra.Dot(sai)));
    }

    /// <summary>
    /// Reduz os raios que não cabem no trecho reto entre dois cantos.
    ///
    /// Dois arcos tangentes só cabem se a soma das tangentes for menor que a
    /// distância entre os vértices. Sem esta trava, um canto come o outro e o
    /// traçado sai com raio zero e borda interna NEGATIVA — pista atravessando
    /// a si mesma, e sem nenhum aviso: foi o que aconteceu na primeira tentativa
    /// de encaixar o degrau perto do canto inferior esquerdo.
    /// </summary>
    private static void EncolherRaios(Vector2[] vertice, float[] raio)
    {
        int n = vertice.Length;
        for (int passada = 0; passada < 4; passada++)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float comprimento = vertice[i].DistanceTo(vertice[j]);
                float ti = Tangente(vertice, raio, i);
                float tj = Tangente(vertice, raio, j);
                if (ti + tj <= comprimento * 0.98f || ti + tj <= 1e-6f)
                    continue;
                float k = comprimento * 0.98f / (ti + tj);
                raio[i] *= k;
                raio[j] *= k;
            }
    }

    private static float Tangente(Vector2[] v, float[] raio, int i) =>
        raio[i] * MathF.Abs(MathF.Tan(Esquina(v, i).giro * 0.5f));

    /// <summary>
    /// Pontos de controle que reproduzem a elipse modulada da 2D.
    ///
    /// A B-spline não passa pelos pontos de controle: com n pontos sobre um
    /// círculo de raio R a curva sai em R·(4 + 2cos(2π/n))/6. A correção
    /// desfaz exatamente isso, e a curva sai em cima da elipse original.
    /// </summary>
    private static Vector3[] Elipse(int n, float rx, float rz, float modulacao, int harmonica, float ondulacao)
    {
        float correcao = 6f / (4f + 2f * Mathf.Cos(Mathf.Tau / n));
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            float r = (1f + modulacao * Mathf.Cos(harmonica * a)) * correcao;
            pts[i] = new Vector3(rx * r * Mathf.Cos(a),
                                 ondulacao * Mathf.Sin(2f * a + 0.6f),
                                 rz * r * Mathf.Sin(a));
        }
        return pts;
    }
}
