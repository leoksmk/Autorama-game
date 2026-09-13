// Oficina de síntese — o som deste jogo não vem de arquivo nenhum.
//
// O resto do projeto gera as malhas e os materiais em código; o áudio segue a
// mesma regra. Aqui há só aritmética de amostras: osciladores, envelopes,
// filtros e sujeira. Quem monta os sons de verdade é o Banco.
//
// A escola é a do som analógico de nave: nada de tom puro de sintetizador
// limpo. Toda voz passa por filtro ressonante e saturação, porque o que faz um
// motor soar MECÂNICO é a distorção e o ruído, não a nota.
//
// Nada aqui depende do estado do jogo, e a única parte que toca o Godot é a
// conversão final para AudioStreamWav.

using System;
using Godot;

namespace OrbitalDerby.Audio;

/// <summary>
/// Seno tabelado com interpolação linear. O motor soma várias parciais por
/// amostra e chamar Math.Sin em todas elas custaria caro sem nenhum ganho
/// audível: com 4096 pontos o erro fica abaixo de -80 dB.
/// </summary>
public static class Tabela
{
    private const int N = 4096;
    private static readonly double[] S = Construir();

    private static double[] Construir()
    {
        // Um ponto a mais na ponta evita um "e se" dentro do laço quente.
        var s = new double[N + 1];
        for (int i = 0; i <= N; i++)
            s[i] = Math.Sin(i * Math.Tau / N);
        return s;
    }

    /// <summary>Seno de uma fase normalizada: 1.0 é uma volta inteira.</summary>
    public static double Sen(double fase)
    {
        fase -= Math.Floor(fase);
        double x = fase * N;
        int i = (int)x;
        return S[i] + (S[i + 1] - S[i]) * (x - i);
    }
}

/// <summary>
/// Um trecho de áudio mono em ponto flutuante. Os métodos alteram o próprio
/// buffer e devolvem <c>this</c>, para encadear: Serra(...).Lp(...).Saturar(2).
/// </summary>
public sealed class Onda
{
    /// <summary>
    /// 32 kHz. Metade do custo de 44,1 e o corte em 16 kHz não tira nada que
    /// importe aqui — ainda ajuda a dar o brilho contido de equipamento antigo.
    /// </summary>
    public const int Taxa = 32000;

    public readonly float[] X;
    public int N => X.Length;

    public Onda(int amostras) => X = new float[Math.Max(1, amostras)];
    public Onda(double segundos) : this(Amostras(segundos)) { }
    public Onda(float[] dados) => X = dados;

    public static int Amostras(double segundos) => Math.Max(1, (int)(segundos * Taxa));

    /// <summary>Tempo, em segundos, da amostra i.</summary>
    public static double T(int i) => i / (double)Taxa;

    // -- geradores ---------------------------------------------------------
    //
    // Todos aceitam a frequência como FUNÇÃO do tempo. É isso que dá os
    // sweeps: o disparo descendente e a tosse do motor morrendo são a mesma
    // ferramenta com curvas diferentes.

    private static Onda Gerar(double dur, Func<double, double> freq, Func<double, double> forma)
    {
        var o = new Onda(dur);
        double fase = 0.0;
        for (int i = 0; i < o.N; i++)
        {
            double f = freq(T(i));
            fase += f / Taxa;
            if (fase >= 1.0) fase -= Math.Floor(fase);
            o.X[i] = (float)forma(fase);
        }
        return o;
    }

    public static Onda Seno(double dur, Func<double, double> freq) =>
        Gerar(dur, freq, p => Math.Sin(p * Math.Tau));

    public static Onda Seno(double dur, double freq) => Seno(dur, _ => freq);

    /// <summary>Dente de serra. Rica em harmônicos: é o corpo de todo motor daqui.</summary>
    public static Onda Serra(double dur, Func<double, double> freq) =>
        Gerar(dur, freq, p => 2.0 * p - 1.0);

    public static Onda Serra(double dur, double freq) => Serra(dur, _ => freq);

    public static Onda Quadrada(double dur, Func<double, double> freq, double ciclo = 0.5) =>
        Gerar(dur, freq, p => p < ciclo ? 1.0 : -1.0);

    public static Onda Quadrada(double dur, double freq, double ciclo = 0.5) =>
        Quadrada(dur, _ => freq, ciclo);

    public static Onda Triangulo(double dur, Func<double, double> freq) =>
        Gerar(dur, freq, p => 4.0 * Math.Abs(p - 0.5) - 1.0);

    public static Onda Triangulo(double dur, double freq) => Triangulo(dur, _ => freq);

    public static Onda Ruido(double dur, int semente = 1)
    {
        var o = new Onda(dur);
        var r = new Random(semente);
        for (int i = 0; i < o.N; i++)
            o.X[i] = (float)(r.NextDouble() * 2.0 - 1.0);
        return o;
    }

    public static Onda Silencio(double dur) => new(dur);

    /// <summary>
    /// Seno cuja frequência é ajustada para caber um número INTEIRO de ciclos
    /// na duração. Sem isso o loop do ambiente estala a cada volta do buffer.
    /// </summary>
    public static Onda SenoEmLoop(double dur, double freqAlvo, double faseInicial = 0.0)
    {
        var o = new Onda(dur);
        int ciclos = Math.Max(1, (int)Math.Round(freqAlvo * o.N / (double)Taxa));
        double f = ciclos * Taxa / (double)o.N;
        for (int i = 0; i < o.N; i++)
            o.X[i] = (float)Math.Sin((i * f / Taxa + faseInicial) * Math.Tau);
        return o;
    }

    // -- envelopes ---------------------------------------------------------

    /// <summary>Ataque linear e queda exponencial: a forma de quase toda batida.</summary>
    public Onda Ad(double ataque, double queda, double curva = 3.0)
    {
        int ia = Amostras(Math.Max(1e-4, ataque));
        for (int i = 0; i < N; i++)
        {
            double t = T(i);
            double e = i < ia
                ? i / (double)ia
                : Math.Pow(Math.Max(0.0, 1.0 - (t - ataque) / Math.Max(1e-4, queda)), curva);
            X[i] *= (float)e;
        }
        return this;
    }

    /// <summary>Envelope livre: recebe 0 a 1 ao longo do trecho.</summary>
    public Onda Env(Func<double, double> f)
    {
        double dur = N / (double)Taxa;
        for (int i = 0; i < N; i++)
            X[i] *= (float)f(T(i) / dur);
        return this;
    }

    /// <summary>Abre e fecha nas pontas: mata o estalo de quem começa no meio da onda.</summary>
    public Onda Bordas(double tempo = 0.005)
    {
        int k = Math.Min(N / 2, Amostras(tempo));
        for (int i = 0; i < k; i++)
        {
            float g = i / (float)k;
            X[i] *= g;
            X[N - 1 - i] *= g;
        }
        return this;
    }

    public Onda Ganho(double g)
    {
        for (int i = 0; i < N; i++) X[i] *= (float)g;
        return this;
    }

    // -- filtros -----------------------------------------------------------
    //
    // Biquad com coeficientes recalculados a cada 32 amostras: barato o
    // bastante para varrer o corte ao longo do som sem custar nada.

    private const int PassoCoef = 32;

    private Onda Biquad(Func<double, double> corte, double q, int tipo)
    {
        double z1 = 0, z2 = 0, a0 = 1, a1 = 0, a2 = 0, b1 = 0, b2 = 0;
        double dur = N / (double)Taxa;
        for (int i = 0; i < N; i++)
        {
            if (i % PassoCoef == 0)
            {
                double fc = Math.Clamp(corte(T(i) / dur), 20.0, Taxa * 0.45);
                double w = Math.Tau * fc / Taxa;
                double alfa = Math.Sin(w) / (2.0 * Math.Max(0.3, q));
                double cs = Math.Cos(w);
                double norma = 1.0 + alfa;
                switch (tipo)
                {
                    case 0: // passa-baixa
                        a0 = (1.0 - cs) / 2.0 / norma;
                        a1 = (1.0 - cs) / norma;
                        a2 = a0;
                        break;
                    case 1: // passa-alta
                        a0 = (1.0 + cs) / 2.0 / norma;
                        a1 = -(1.0 + cs) / norma;
                        a2 = a0;
                        break;
                    default: // passa-banda
                        a0 = alfa / norma;
                        a1 = 0.0;
                        a2 = -a0;
                        break;
                }
                b1 = -2.0 * cs / norma;
                b2 = (1.0 - alfa) / norma;
            }
            double e = X[i];
            double s = a0 * e + z1;
            z1 = a1 * e - b1 * s + z2;
            z2 = a2 * e - b2 * s;
            X[i] = (float)s;
        }
        return this;
    }

    public Onda Lp(Func<double, double> corte, double q = 0.707) => Biquad(corte, q, 0);
    public Onda Lp(double corte, double q = 0.707) => Biquad(_ => corte, q, 0);
    public Onda Hp(Func<double, double> corte, double q = 0.707) => Biquad(corte, q, 1);
    public Onda Hp(double corte, double q = 0.707) => Biquad(_ => corte, q, 1);
    public Onda Bp(Func<double, double> corte, double q = 2.0) => Biquad(corte, q, 2);
    public Onda Bp(double corte, double q = 2.0) => Biquad(_ => corte, q, 2);

    // -- sujeira -----------------------------------------------------------

    /// <summary>
    /// Saturação por tanh. É o que separa "sintetizador" de "máquina": os
    /// harmônicos que ela cria são o rosnado do motor.
    /// </summary>
    public Onda Saturar(double drive = 2.0)
    {
        double n = Math.Tanh(drive);
        for (int i = 0; i < N; i++)
            X[i] = (float)(Math.Tanh(X[i] * drive) / n);
        return this;
    }

    /// <summary>
    /// Eco curto realimentado. Com atraso de poucos milissegundos deixa de
    /// soar como eco e vira RESSONÂNCIA metálica — é o truque que faz um
    /// disparo soar como pancada em cabo de aço esticado.
    /// </summary>
    public Onda Eco(double atraso, double realimenta, double mistura = 0.6)
    {
        int d = Math.Max(1, Amostras(atraso));
        var seco = (float[])X.Clone();
        var buf = new float[N];
        for (int i = 0; i < N; i++)
        {
            float atras = i >= d ? buf[i - d] : 0f;
            buf[i] = seco[i] + atras * (float)realimenta;
            X[i] = seco[i] + atras * (float)(realimenta * mistura);
        }
        return this;
    }

    /// <summary>
    /// Cauda curta de reverberação (quatro pentes e um passa-tudo). O jogo se
    /// passa no vácuo, mas o ouvinte está DENTRO de alguma coisa: sem um resto
    /// de espaço, cada som fica colado na cara.
    /// </summary>
    public Onda Espaco(double tamanho = 0.6, double mistura = 0.25)
    {
        int[] pentes = { 1231, 1483, 1741, 1999 };
        var saida = new float[N];
        foreach (int d in pentes)
        {
            var buf = new float[N];
            double g = 0.72 * tamanho;
            for (int i = 0; i < N; i++)
            {
                float atras = i >= d ? buf[i - d] : 0f;
                buf[i] = X[i] + atras * (float)g;
                saida[i] += atras * 0.25f;
            }
        }
        const int dp = 331;
        for (int i = dp; i < N; i++)
            saida[i] += saida[i - dp] * -0.5f;
        for (int i = 0; i < N; i++)
            X[i] = (float)(X[i] * (1.0 - mistura) + saida[i] * mistura);
        return this;
    }

    // -- combinação --------------------------------------------------------

    /// <summary>Soma outra onda a partir de um deslocamento em segundos.</summary>
    public Onda Somar(Onda outra, double ganho = 1.0, double em = 0.0)
    {
        int off = em <= 0.0 ? 0 : Amostras(em);
        for (int i = 0; i < outra.N && i + off < N; i++)
            X[i + off] += outra.X[i] * (float)ganho;
        return this;
    }

    public static Onda Mixar(double dur, params (Onda o, double g)[] partes)
    {
        var m = new Onda(dur);
        foreach (var (o, g) in partes) m.Somar(o, g);
        return m;
    }

    /// <summary>Leva o pico ao valor pedido. Deixa o mix previsível.</summary>
    public Onda Normalizar(double pico = 0.9)
    {
        float max = 0f;
        for (int i = 0; i < N; i++) max = Math.Max(max, Math.Abs(X[i]));
        return max < 1e-6f ? this : Ganho(pico / max);
    }

    public Onda Inverter()
    {
        Array.Reverse(X);
        return this;
    }

    // -- saída -------------------------------------------------------------

    /// <summary>
    /// Vira um AudioStreamWav de 16 bits. <paramref name="pan"/> vai de -1
    /// (esquerda) a 1 (direita): com as duas pistas separadas no estéreo dá
    /// para ouvir QUAL nave está sofrendo sem tirar o olho da pista.
    ///
    /// A lei é de potência constante (a raiz), sem nenhum fator de compensação
    /// por cima: com um, o canal dominante de um som lateralizado passaria de
    /// 1.0 e ceifaria o pico. Assim o centro fica 3 dB abaixo das pontas, que
    /// é o preço certo a pagar.
    /// </summary>
    public AudioStreamWav ParaWav(double pan = 0.0, bool loop = false)
    {
        bool estereo = Math.Abs(pan) > 1e-3;
        var bytes = new byte[N * 2 * (estereo ? 2 : 1)];
        double ge = estereo ? Math.Sqrt((1.0 - pan) * 0.5) : 1.0;
        double gd = estereo ? Math.Sqrt((1.0 + pan) * 0.5) : 1.0;
        int k = 0;
        for (int i = 0; i < N; i++)
        {
            if (estereo)
            {
                Escrever(bytes, ref k, X[i] * (float)ge);
                Escrever(bytes, ref k, X[i] * (float)gd);
            }
            else
            {
                Escrever(bytes, ref k, X[i]);
            }
        }

        var w = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Taxa,
            Stereo = estereo,
            Data = bytes,
        };
        if (loop)
        {
            w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            w.LoopBegin = 0;
            w.LoopEnd = N;
        }
        return w;
    }

    private static void Escrever(byte[] destino, ref int k, float v)
    {
        short s = (short)Math.Clamp(v * 32000f, -32767f, 32767f);
        destino[k++] = (byte)(s & 0xFF);
        destino[k++] = (byte)((s >> 8) & 0xFF);
    }
}
