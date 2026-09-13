using Xunit;

namespace OrbitalDerby.Core.Tests;

/// <summary>Invariantes da corrida e contrato com o barramento de saída.</summary>
public class CorridaTests
{
    [Fact]
    public void Velocidade_nunca_sobe_sem_pwm_e_o_barramento_diz_a_verdade()
    {
        var r = Apoio.Nova();
        var saida = (SaidaNula)r.Saida;
        var rng = new Random(9);
        var anterior = new double[2];
        for (int i = 0; i < 6000 && r.Vencedor is null; i++)
        {
            var a = new Pulso(rng.NextDouble() < 0.12, rng.NextDouble() < 0.01);
            var b = new Pulso(rng.NextDouble() < 0.09, rng.NextDouble() < 0.01);
            r.Atualizar(Apoio.DT, a, b, true);
            foreach (var n in r.Naves)
            {
                Assert.False(n.Speed > anterior[n.Lane] + 1e-9 && n.Pwm <= 0.0,
                    $"frame {i}: pista {n.Lane} acelerou sem PWM");
                anterior[n.Lane] = n.Speed;
                Assert.InRange(saida.Pwm(n.Lane), 0.0, 1.0);
                Assert.Equal(n.Pwm, saida.Pwm(n.Lane), 12);
            }
        }
    }

    [Fact]
    public void Sincronizar_posicao_conta_a_volta_so_na_virada()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        n.T = 0.95;
        n.Voltas = 2;
        r.SincronizarPosicao(0, 0.02);
        Assert.Equal(3, n.Voltas);
        Assert.Equal(0.02, n.T, 12);
        r.SincronizarPosicao(0, 0.45);
        Assert.Equal(3, n.Voltas);
    }

    [Fact]
    public void Classificacao_usa_o_tempo_de_chegada_e_o_segundo_tambem_recebe_tempo()
    {
        var r = Apoio.Nova();
        var (a, b) = (r.Naves[0], r.Naves[1]);
        a.Voltas = Cfg.VoltasParaVencer - 1; a.T = 0.99;
        b.Voltas = Cfg.VoltasParaVencer - 1; b.T = 0.90;
        a.Speed = b.Speed = 0.16;

        for (int i = 0; i < 400 && !(a.Terminou && b.Terminou); i++)
            Apoio.Passos(r, 1, p1t: true, p2t: true, acaoEm: -1);

        Assert.True(a.Terminou && b.Terminou);
        Assert.Same(a, r.Vencedor);
        Assert.True(b.TempoFinal > a.TempoFinal, "o cronômetro parou para o segundo");
        Assert.Same(a, r.Classificacao()[0]);
    }

    [Fact]
    public void Reiniciar_zera_o_barramento()
    {
        var r = Apoio.Nova();
        Apoio.Martelar(r, 2.0, 8);
        r.Reiniciar();
        var saida = (SaidaNula)r.Saida;
        Assert.Equal(0.0, saida.Pwm(0));
        Assert.Equal("parado", saida.Tag(1));
    }
}
