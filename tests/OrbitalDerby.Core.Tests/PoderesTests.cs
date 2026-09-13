using Xunit;

namespace OrbitalDerby.Core.Tests;

/// <summary>Tiro, Bomba e Escudo: alcance, tempo de voo e bloqueio.</summary>
public class PoderesTests
{
    private static (Corrida r, Nave a, Nave b) Duelo(Item item, double tA = 0.30, double tB = 0.36)
    {
        var r = Apoio.Nova();
        var (a, b) = (r.Naves[0], r.Naves[1]);
        a.Slot = item;
        a.T = tA;
        b.T = tB;
        b.Voltas = a.Voltas;
        return (r, a, b);
    }

    [Fact]
    public void A_caixa_tem_tres_poderes_e_a_face_vazia()
    {
        Assert.Equal(new[] { Item.Tiro, Item.Bomba, Item.Escudo, Item.Nada }, Roleta.Ordem);
        var rng = new Random(2);
        var contagem = new Dictionary<Item, int>();
        for (int i = 0; i < 6000; i++)
        {
            var it = Roleta.Sortear(false, rng);
            contagem[it] = contagem.GetValueOrDefault(it) + 1;
        }
        Assert.Equal(4, contagem.Count);
        double nada = contagem[Item.Nada] / 6000.0;
        Assert.InRange(nada, 0.10, 0.26);
    }

    [Fact]
    public void Catchup_da_mais_ataque_para_quem_esta_atras_e_pode_ser_desligado()
    {
        int Conta(bool atrasado, bool catchup, Item alvo)
        {
            var rng = new Random(1);
            int c = 0;
            for (int i = 0; i < 20000; i++)
                if (Roleta.Sortear(atrasado, rng, catchup) == alvo) c++;
            return c;
        }
        Assert.True(Conta(true, true, Item.Nada) < Conta(false, true, Item.Nada));
        Assert.True(Conta(true, true, Item.Tiro) > Conta(false, true, Item.Tiro));
        Assert.Equal(Conta(false, true, Item.Nada), Conta(true, false, Item.Nada));
    }

    [Fact]
    public void Bomba_so_vale_na_chegada_e_para_por_dois_segundos()
    {
        var (r, _, b) = Duelo(Item.Bomba);
        Apoio.Passos(r, 1, p1a: true);
        Assert.Equal(0.0, b.Atordoado);                       // ainda no ar
        Apoio.Passos(r, (int)(Cfg.BombaVoo * 60) + 1, acaoEm: -1);
        Assert.InRange(b.Atordoado, Cfg.BombaDuracao - 0.05, Cfg.BombaDuracao);
        Apoio.Passos(r, 60, p2t: true, acaoEm: -1);
        Assert.Equal(0.0, b.Pwm);
        Apoio.Passos(r, (int)(Cfg.BombaDuracao * 60) + 5, p2t: true, acaoEm: -1);
        Assert.Equal(0.0, b.Atordoado);
        Assert.True(b.Pwm > 0, "não voltou depois da bomba");
    }

    [Fact]
    public void Tiro_derruba_o_teto_mas_nao_para()
    {
        var (r, _, b) = Duelo(Item.Tiro);
        Apoio.Passos(r, 1, p1a: true);
        Apoio.Passos(r, (int)(Cfg.TiroVoo * 60) + 1, acaoEm: -1);
        Assert.True(b.Lento > 0);
        Assert.Equal(Cfg.PwmBase * Cfg.TiroMult, b.TetoPwm(), 9);
        Assert.Equal(0.0, b.Atordoado);
    }

    [Theory]
    [InlineData(Item.Tiro)]
    [InlineData(Item.Bomba)]
    public void Ataques_so_pegam_de_perto_a_frente_ou_atras(Item item)
    {
        double alcance = item == Item.Tiro ? Cfg.TiroAlcance : Cfg.BombaAlcance;

        bool Pegou(double tAlvo)
        {
            var (r, a, b) = Duelo(item, 0.30, tAlvo);
            Apoio.Passos(r, 1, p1a: true);
            Apoio.Passos(r, 60, acaoEm: -1);
            Assert.Null(a.Slot);                              // queima mesmo errando
            return b.Lento > 0 || b.Atordoado > 0;
        }

        Assert.True(Pegou(0.30 + alcance * 0.5), "perto à frente");
        Assert.True(Pegou(0.30 - alcance * 0.5), "perto atrás");
        Assert.False(Pegou(0.30 + alcance * 3), "longe");
    }

    [Theory]
    [InlineData(Item.Tiro)]
    [InlineData(Item.Bomba)]
    public void Escudo_bloqueia_o_proximo_ataque(Item ataque)
    {
        var (r, _, b) = Duelo(ataque);
        b.Escudo = true;
        Apoio.Passos(r, 1, p1a: true);
        Apoio.Passos(r, 45, acaoEm: -1);
        Assert.Equal(0.0, b.Lento);
        Assert.Equal(0.0, b.Atordoado);
        Assert.False(b.Escudo);
    }

    [Fact]
    public void Escudo_protege_uma_vez_so()
    {
        var (r, a, b) = Duelo(Item.Bomba);
        b.Escudo = true;
        Apoio.Passos(r, 1, p1a: true);
        Apoio.Passos(r, 45, acaoEm: -1);
        a.Slot = Item.Bomba;
        Apoio.Passos(r, 1, p1a: true);
        Apoio.Passos(r, 45, acaoEm: -1);
        Assert.True(b.Atordoado > 0, "o segundo ataque deveria passar");
    }

    [Fact]
    public void Da_para_levantar_o_escudo_com_a_bomba_no_ar()
    {
        var (r, _, b) = Duelo(Item.Bomba);
        b.Slot = Item.Escudo;
        Apoio.Passos(r, 1, p1a: true);                        // lança
        Assert.Single(r.EmVoo);
        Apoio.Passos(r, 6, p2a: true);                        // alvo reage depois
        Assert.True(b.Escudo);
        Apoio.Passos(r, 45, acaoEm: -1);
        Assert.Equal(0.0, b.Atordoado);
        Assert.False(b.Escudo);
    }

    [Fact]
    public void Item_pode_ser_usado_fora_do_checkpoint()
    {
        var (r, a, _) = Duelo(Item.Escudo, 0.33, 0.9);
        Apoio.Passos(r, 1, p1a: true);
        Assert.True(a.Escudo);
        Assert.Null(a.Slot);
    }
}
