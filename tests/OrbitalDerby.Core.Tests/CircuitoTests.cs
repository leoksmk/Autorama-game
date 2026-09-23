using Xunit;

namespace OrbitalDerby.Core.Tests;

/// <summary>
/// O que o CIRCUITO escolhe na regra: checkpoints, voltas e ritmo.
///
/// Os três deixaram de ser constantes quando o jogo ganhou mais de uma pista, e
/// isso abriu um risco novo: um valor de circuito vazando para os testes de
/// física, ou o ritmo deixando de ser neutro. Estes testes fecham esse buraco —
/// o resto da paridade com a versão Python depende de o ritmo valer exatamente
/// 1,0 quando ninguém escolheu pista.
/// </summary>
public class CircuitoTests
{
    [Fact]
    public void Sem_circuito_escolhido_o_ritmo_e_neutro()
    {
        // Se isto falhar, ReferenciaPythonTests falha junto: a versão Python não
        // tem fator de ritmo nenhum, então só 1,0 reproduz a conta dela.
        Assert.Equal(1.0, Cfg.RitmoDoCircuito);
    }

    [Fact]
    public void O_ritmo_escala_a_velocidade_e_nada_mais()
    {
        double Topo(double ritmo)
        {
            double antes = Cfg.RitmoDoCircuito;
            try
            {
                Cfg.RitmoDoCircuito = ritmo;
                var r = Apoio.Nova();
                // 7 Hz e 3 s: chega ao teto em meio segundo e ainda está longe
                // do superaquecimento, que a 8 Hz corta o motor aos 4,5 s e
                // deixaria a medição pegar uma nave parada.
                Apoio.Martelar(r, 3.0, 7.0);
                return r.Naves[0].Speed;
            }
            finally
            {
                Cfg.RitmoDoCircuito = antes;
            }
        }

        double cheio = Topo(1.0);
        double metade = Topo(0.5);

        Assert.True(cheio > 0.1, $"a nave deveria ter chegado perto do teto, e ficou em {cheio:0.000}");
        Assert.Equal(cheio * 0.5, metade, 6);
    }

    [Fact]
    public void O_ritmo_nao_mexe_no_PWM()
    {
        // O PWM é o contrato com o hardware. O ritmo é uma propriedade da PISTA,
        // não do motor: se ele mudasse o duty, um circuito mais longo mandaria
        // mais corrente para o mesmo carrinho.
        double PwmNoTopo(double ritmo)
        {
            double antes = Cfg.RitmoDoCircuito;
            try
            {
                Cfg.RitmoDoCircuito = ritmo;
                var r = Apoio.Nova();
                Apoio.Martelar(r, 2.0, 8.0);
                return r.Saida is SaidaNula s ? s.Pwm(0) : -1;
            }
            finally
            {
                Cfg.RitmoDoCircuito = antes;
            }
        }

        Assert.Equal(PwmNoTopo(1.0), PwmNoTopo(0.4), 9);
    }

    [Fact]
    public void Com_o_aquecimento_desligado_o_motor_nunca_corta()
    {
        // Desligado, martelar no talo a prova inteira tem de ser possível: é
        // exatamente para isso que a opção existe. Se o calor ainda subisse, o
        // jogador veria o motor cortar sem nenhuma barra explicando por quê.
        bool antes = Cfg.AquecimentoAtivo;
        try
        {
            Cfg.AquecimentoAtivo = false;
            var r = Apoio.Nova();
            var n = r.Naves[0];
            Apoio.Martelar(r, 20.0, 9.0);

            Assert.Equal(0.0, n.Calor);
            Assert.Equal(0.0, n.Superaquecimento);
            Assert.True(n.Pwm > 0.5, $"o motor deveria estar no talo, e o PWM ficou em {n.Pwm:0.00}");
        }
        finally
        {
            Cfg.AquecimentoAtivo = antes;
        }
    }

    [Fact]
    public void Com_o_aquecimento_ligado_o_motor_corta()
    {
        // O contraponto do teste acima: sem ele, "desligado funciona" não prova
        // nada, porque poderia estar desligado nos dois casos.
        Assert.True(Cfg.AquecimentoAtivo, "o padrão tem de ser ligado");
        var (cortes, _) = Apoio.ContarCortes(9.0, 20.0);
        Assert.True(cortes > 0, "martelando a 9 Hz por 20 s o motor tinha de cortar");
    }

    [Fact]
    public void Os_checkpoints_estao_em_ordem_e_dentro_da_volta()
    {
        // CruzouCheckpoint anda para frente a partir de t anterior e devolve o
        // PRIMEIRO da lista que couber no avanço. Fora de ordem, o índice
        // devolvido deixa de ser o do sensor que realmente disparou.
        for (int i = 0; i < Cfg.Checkpoints.Length; i++)
        {
            Assert.InRange(Cfg.Checkpoints[i], 0.0, 1.0);
            if (i > 0)
                Assert.True(Cfg.Checkpoints[i] > Cfg.Checkpoints[i - 1],
                    $"checkpoint {i} vem antes do {i - 1}");
        }
    }

    [Fact]
    public void Trocar_os_checkpoints_troca_o_que_a_regra_ve()
    {
        var originais = Cfg.Checkpoints;
        try
        {
            Cfg.Checkpoints = new[] { 0.5 };
            Assert.Null(Pista.CruzouCheckpoint(0.10, 0.20));
            Assert.Equal(0, Pista.CruzouCheckpoint(0.45, 0.55));
        }
        finally
        {
            Cfg.Checkpoints = originais;
        }
    }
}
