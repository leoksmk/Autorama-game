using Xunit;

namespace OrbitalDerby.Core.Tests;

/// <summary>Caixa de item: janela de tempo, giro que não para a nave, prêmio certo.</summary>
public class RoletaTests
{
    private static int AteAbrir(Corrida r, Nave n, int limite = 600)
    {
        for (int i = 0; i < limite; i++)
        {
            var p = new Pulso(i % 8 == 0, false);
            r.Atualizar(Apoio.DT, n.Lane == 0 ? p : Pulso.Nenhum, n.Lane == 1 ? p : Pulso.Nenhum, true);
            if (n.Roleta.Estado == EstadoRoleta.Oportunidade)
                return i;
        }
        return -1;
    }

    [Fact]
    public void A_janela_abre_ao_cruzar_o_checkpoint()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        n.T = Cfg.Checkpoints[0] - 0.02;
        n.Speed = 0.16;
        Assert.True(AteAbrir(r, n) >= 0, "não abriu");
        Assert.Equal(0, n.Roleta.Checkpoint);
        Assert.True(n.T > Cfg.Checkpoints[0], "abriu antes de passar pela linha");
    }

    [Fact]
    public void Sem_apertar_a_janela_fecha_sozinha()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        n.Roleta.Abrir(0);
        int frames = 0;
        while (n.Roleta.Estado == EstadoRoleta.Oportunidade && frames < 600)
        {
            r.Atualizar(Apoio.DT, Pulso.Nenhum, Pulso.Nenhum, true);
            frames++;
        }
        Assert.InRange(frames * Apoio.DT, Cfg.RoletaOportunidade - 0.05, Cfg.RoletaOportunidade + 0.05);
        Assert.Null(n.Slot);
    }

    [Fact]
    public void Com_item_no_slot_cruzar_o_checkpoint_nao_abre()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        n.Slot = Item.Escudo;
        n.T = Cfg.Checkpoints[1] - 0.02;
        n.Speed = 0.16;
        Assert.Equal(-1, AteAbrir(r, n, 200));
    }

    [Fact]
    public void Sem_janela_aberta_a_acao_nao_gira()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        n.T = 0.30;
        Apoio.Passos(r, 1, p1a: true);
        Assert.Equal(EstadoRoleta.Parada, n.Roleta.Estado);
    }

    [Theory]
    [InlineData(Item.Tiro)]
    [InlineData(Item.Bomba)]
    [InlineData(Item.Escudo)]
    [InlineData(Item.Nada)]
    public void A_caixa_trava_na_face_sorteada_e_entrega_o_premio(Item premio)
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        n.Roleta.Abrir(0);
        n.Roleta.Girar(false, r.Rng, premio);

        Apoio.Passos(r, (int)(Cfg.RoletaGiro * 60) + 1, acaoEm: -1);
        Assert.Equal(EstadoRoleta.Revelando, n.Roleta.Estado);
        Assert.Equal(premio, n.Roleta.Face);

        Apoio.Passos(r, (int)(Cfg.RoletaRevelacao * 60) + 3, acaoEm: -1);
        Assert.Equal(EstadoRoleta.Parada, n.Roleta.Estado);
        Assert.Equal(premio == Item.Nada ? null : premio, n.Slot);
    }

    [Fact]
    public void Quarenta_giros_sorteados_sao_coerentes_e_o_nada_aparece()
    {
        var vistas = new HashSet<Item>();
        for (int semente = 0; semente < 40; semente++)
        {
            var r = Apoio.Nova(semente);
            var n = r.Naves[0];
            n.Roleta.Abrir(0);
            Apoio.Passos(r, 1, p1a: true);
            Assert.Equal(EstadoRoleta.Girando, n.Roleta.Estado);
            Item sorteado = n.Roleta.Resultado!.Value;
            Apoio.Passos(r, (int)((Cfg.RoletaGiro + Cfg.RoletaRevelacao) * 60) + 3, acaoEm: -1);
            Assert.Equal(sorteado == Item.Nada ? null : sorteado, n.Slot);
            vistas.Add(sorteado);
        }
        Assert.Contains(Item.Nada, vistas);
    }

    [Fact]
    public void Girar_nao_para_a_nave()
    {
        var r = Apoio.Nova(1);
        var n = r.Naves[0];
        n.Roleta.Abrir(0);
        Apoio.Passos(r, 1, p1t: true, p1a: true);
        Apoio.Passos(r, 40, p1t: true, acaoEm: -1);
        Assert.True(n.Roleta.Ativa);
        Assert.True(n.Pwm > 0 && n.Speed > 0, $"pwm {n.Pwm:0.00}, speed {n.Speed:0.000}");
        Assert.NotEqual("roleta", ((SaidaNula)r.Saida).Tag(0));
    }
}
