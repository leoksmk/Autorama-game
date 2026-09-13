namespace OrbitalDerby.Core.Tests;

/// <summary>Roteiros de entrada reutilizados pelos testes.</summary>
internal static class Apoio
{
    public const double DT = 1.0 / 60.0;

    public static Corrida Nova(int semente = 11) => new(new SaidaNula(), null, semente);

    /// <summary>Martela o acelerador de uma pista a <paramref name="hz"/> cliques por segundo.</summary>
    public static void Martelar(Corrida r, double segundos, double hz, int lane = 0)
    {
        int frames = (int)(segundos * 60);
        double passo = hz > 0 ? 60.0 / hz : double.MaxValue;
        double prox = 0.0;
        for (int i = 0; i < frames; i++)
        {
            bool clique = false;
            if (hz > 0 && i >= prox)
            {
                clique = true;
                prox += passo;
            }
            var p = new Pulso(clique, false);
            r.Atualizar(DT, lane == 0 ? p : Pulso.Nenhum, lane == 1 ? p : Pulso.Nenhum, true);
        }
    }

    /// <summary>
    /// Roda <paramref name="n"/> frames. A ação conta só no frame
    /// <paramref name="acaoEm"/> (é borda); o acelerador, se pedido, é
    /// martelado a 7,5 cliques por segundo.
    /// </summary>
    public static void Passos(Corrida r, int n, bool p1t = false, bool p1a = false,
                              bool p2t = false, bool p2a = false, int acaoEm = 0)
    {
        for (int i = 0; i < n; i++)
        {
            bool borda = i == acaoEm;
            var a = new Pulso(p1t && i % 8 == 0, p1a && borda);
            var b = new Pulso(p2t && i % 8 == 0, p2a && borda);
            r.Atualizar(DT, a, b, true);
        }
    }

    /// <summary>Quantas vezes o motor da pista 0 cortou martelando a <paramref name="hz"/>.</summary>
    public static (int cortes, Nave nave) ContarCortes(double hz, double segundos)
    {
        var r = Nova();
        var n = r.Naves[0];
        int cortes = 0;
        double prox = 0.0;
        for (int i = 0; i < (int)(segundos * 60); i++)
        {
            bool clique = false;
            if (i >= prox)
            {
                clique = true;
                prox += 60.0 / hz;
            }
            double antes = n.Superaquecimento;
            r.Atualizar(DT, new Pulso(clique, false), Pulso.Nenhum, true);
            if (n.Superaquecimento > 0 && antes <= 0)
                cortes++;
        }
        return (cortes, n);
    }
}
