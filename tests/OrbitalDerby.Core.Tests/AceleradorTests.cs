using Xunit;

namespace OrbitalDerby.Core.Tests;

/// <summary>Acelerador de martelar: a frequência dos apertos é o que vira PWM.</summary>
public class AceleradorTests
{
    [Fact]
    public void Segurar_o_botao_gera_um_clique_so()
    {
        var borda = new BordaBotao();
        int cliques = 0;
        for (int i = 0; i < 300; i++)            // 5 s com o botão cravado
            if (borda.Atualizar(true))
                cliques++;
        Assert.Equal(1, cliques);
    }

    [Fact]
    public void Um_clique_isolado_nao_sustenta_a_nave()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        r.Atualizar(Apoio.DT, new Pulso(true, false), Pulso.Nenhum, true);
        Assert.True(n.Pwm > 0, "o primeiro aperto precisa responder (empurrão inicial)");
        Apoio.Martelar(r, 2.0, 0);
        Assert.Equal(0.0, n.Pwm);
        Assert.Equal(0.0, n.Speed);
    }

    [Theory]
    [InlineData(2.0, 0.25)]
    [InlineData(4.0, 0.50)]
    [InlineData(6.0, 0.75)]
    [InlineData(8.0, 1.00)]
    public void Cadencia_vira_o_esforco_prometido_no_config(double hz, double esperado)
    {
        var r = Apoio.Nova();
        Apoio.Martelar(r, 3.0, hz);   // 3 s: estabiliza e ainda não superaqueceu
        Assert.InRange(r.Naves[0].Esforco, esperado - 0.06, esperado + 0.06);
    }

    [Fact]
    public void Esforco_cresce_com_a_cadencia_e_satura()
    {
        double anterior = -1;
        foreach (double hz in new[] { 2.0, 4.0, 6.0, 8.0, 12.0 })
        {
            var r = Apoio.Nova();
            Apoio.Martelar(r, 3.0, hz);
            Assert.True(r.Naves[0].Esforco >= anterior - 1e-9, $"{hz} Hz regrediu");
            anterior = r.Naves[0].Esforco;
        }
        Assert.True(anterior >= 0.99, $"12 Hz deveria saturar, deu {anterior:0.00}");
    }

    [Fact]
    public void Parar_de_clicar_corta_o_motor()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        Apoio.Martelar(r, 4.0, 10);
        double pico = n.Speed;
        Apoio.Martelar(r, 1.5, 0);
        Assert.Equal(0.0, n.Esforco);
        Assert.True(n.Speed < pico * 0.25, $"caiu de {pico:0.000} só para {n.Speed:0.000}");
    }

    [Fact]
    public void Martelar_no_maximo_superaquece_em_ciclos()
    {
        var (cortes, _) = Apoio.ContarCortes(14, 14.0);
        Assert.True(cortes >= 2, $"14 Hz por 14 s cortou só {cortes}x");
    }

    [Fact]
    public void Ritmo_sustentavel_nunca_corta()
    {
        var (cortes, n) = Apoio.ContarCortes(4, 40.0);
        Assert.Equal(0, cortes);
        Assert.True(n.Speed > 0.06, $"andando a {n.Speed:0.000} voltas/s");
    }

    [Fact]
    public void Ritmo_forte_tem_preco()
    {
        var (cortes, _) = Apoio.ContarCortes(8, 40.0);
        Assert.True(cortes >= 2, $"40 s a 8 Hz cortou só {cortes}x");
    }

    [Fact]
    public void Ritmo_baixo_nao_esquenta()
    {
        var (cortes, n) = Apoio.ContarCortes(3, 25.0);
        Assert.Equal(0, cortes);
        Assert.True(n.Calor < 0.02, $"calor {n.Calor:0.000}");
    }

    [Fact]
    public void Durante_o_superaquecimento_o_pwm_e_zero()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        double prox = 0;
        for (int i = 0; i < 900; i++)
        {
            bool c = false;
            if (i >= prox) { c = true; prox += 60.0 / 14; }
            r.Atualizar(Apoio.DT, new Pulso(c, false), Pulso.Nenhum, true);
            if (n.Superaquecimento > 0)
            {
                Assert.Equal(0.0, n.Pwm);
                return;
            }
        }
        Assert.Fail("nunca superaqueceu");
    }
}
