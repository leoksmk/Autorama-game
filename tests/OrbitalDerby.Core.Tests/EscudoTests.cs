using Xunit;

namespace OrbitalDerby.Core.Tests;

/// <summary>
/// O Escudo não tem relógio: ele vence numa PASSAGEM POR SENSOR.
///
/// Esta é a única regra do jogo cuja duração é contada em eventos de pista, e
/// os testes aqui existem para travar as duas metades disso: que a passagem
/// realmente gasta o escudo, e que gastar é a ÚNICA coisa que a passagem faz
/// com ele — nada de decaimento por tempo entrando de volta sem ninguém ver.
/// </summary>
public class EscudoTests
{
    // 7 cliques por segundo: rápido o bastante para a nave andar de verdade,
    // devagar o bastante para ela não superaquecer no meio do teste e virar o
    // assunto. Martelar é obrigatório — sem clique o PWM é zero, a nave para
    // em meio segundo e nunca alcança sensor nenhum.
    private const double HzDeCruzeiro = 7.0;

    /// <summary>Martela o acelerador até cruzar o próximo checkpoint.</summary>
    private static int AndarAteOProximoSensor(Corrida r, Nave n, int limiteFrames = 3600)
    {
        double passo = 60.0 / HzDeCruzeiro;
        double prox = 0.0;
        for (int i = 0; i < limiteFrames; i++)
        {
            bool clique = i >= prox;
            if (clique) prox += passo;
            r.Atualizar(Apoio.DT, new Pulso(clique, false), Pulso.Nenhum, true);
            if (n.CheckpointCruzado.HasValue)
                return i;
        }
        Assert.Fail("a nave não chegou a checkpoint nenhum");
        return -1;
    }

    private static Corrida NaveAndando(out Nave n, double t = 0.0)
    {
        var r = Apoio.Nova();
        n = r.Naves[0];
        n.T = t;
        return r;
    }

    [Fact]
    public void Levantar_o_escudo_da_o_prazo_cheio()
    {
        var r = Apoio.Nova();
        var n = r.Naves[0];
        n.Slot = Item.Escudo;
        Apoio.Passos(r, 1, p1a: true);

        Assert.True(n.Escudo);
        Assert.Equal(Cfg.EscudoTrechos, n.EscudoTrechos);
        Assert.False(n.EscudoNoUltimoTrecho);
    }

    [Fact]
    public void Cada_passagem_por_sensor_gasta_um_trecho()
    {
        var r = NaveAndando(out var n);
        n.Escudo = true;

        AndarAteOProximoSensor(r, n);
        Assert.Equal(Cfg.EscudoTrechos - 1, n.EscudoTrechos);
        Assert.True(n.Escudo, "o escudo não pode cair no primeiro sensor");
        Assert.True(n.EscudoNoUltimoTrecho);
        Assert.False(n.EscudoVenceu);
    }

    [Fact]
    public void O_escudo_cai_no_segundo_sensor_e_avisa_uma_vez_so()
    {
        var r = NaveAndando(out var n);
        n.Escudo = true;

        for (int k = 0; k < Cfg.EscudoTrechos; k++)
            AndarAteOProximoSensor(r, n);

        Assert.False(n.Escudo);
        Assert.Equal(0, n.EscudoTrechos);
        Assert.True(n.EscudoVenceu, "o frame em que cai tem de anunciar");

        // O aviso vale um frame só, como qualquer evento de sensor.
        r.Atualizar(Apoio.DT, Pulso.Nenhum, Pulso.Nenhum, true);
        Assert.False(n.EscudoVenceu);
    }

    [Fact]
    public void Quem_nao_tem_escudo_nao_anuncia_queda_ao_passar_pelo_sensor()
    {
        var r = NaveAndando(out var n);
        AndarAteOProximoSensor(r, n);
        Assert.False(n.EscudoVenceu);
        Assert.Equal(0, n.EscudoTrechos);
    }

    [Fact]
    public void Levantar_de_novo_rearma_o_prazo_cheio()
    {
        var r = NaveAndando(out var n);
        n.Escudo = true;
        AndarAteOProximoSensor(r, n);
        Assert.Equal(Cfg.EscudoTrechos - 1, n.EscudoTrechos);

        n.Slot = Item.Escudo;
        Apoio.Passos(r, 1, p1a: true);
        Assert.Equal(Cfg.EscudoTrechos, n.EscudoTrechos);
    }

    [Fact]
    public void Aparar_um_ataque_gasta_o_escudo_inteiro_e_nao_um_trecho()
    {
        var r = Apoio.Nova();
        var (a, b) = (r.Naves[0], r.Naves[1]);
        a.Slot = Item.Bomba;
        a.T = 0.30;
        b.T = 0.36;
        b.Escudo = true;

        Apoio.Passos(r, 1, p1a: true);
        Apoio.Passos(r, 45, acaoEm: -1);

        Assert.Equal(0.0, b.Atordoado);
        Assert.Equal(0, b.EscudoTrechos);
        Assert.False(b.EscudoVenceu, "aparar não é vencer: o marcador de queda é outro evento");
    }

    [Fact]
    public void O_sensor_fisico_gasta_o_escudo_pelo_mesmo_caminho_que_a_integracao()
    {
        // SincronizarPosicao é a ponte para os sensores do autorama. Se ela não
        // passasse pelo mesmo ponto, o escudo teria duas regras: uma na
        // simulação e outra na pista de verdade.
        var r = Apoio.Nova();
        var n = r.Naves[0];
        n.Escudo = true;
        n.T = Cfg.Checkpoints[0] - 0.01;

        r.SincronizarPosicao(0, Cfg.Checkpoints[0] + 0.01);

        Assert.Equal(0, n.CheckpointCruzado);
        Assert.Equal(Cfg.EscudoTrechos - 1, n.EscudoTrechos);
    }

    [Fact]
    public void Cada_trecho_entre_sensores_da_tempo_de_reagir_a_um_ataque()
    {
        // O menor trecho tem de ser mais longo que o voo de uma bomba, senão o
        // escudo cairia antes de o ataque chegar e o item não existiria.
        double menor = 1.0;
        for (int i = 0; i < Cfg.Checkpoints.Length; i++)
        {
            double prox = Cfg.Checkpoints[(i + 1) % Cfg.Checkpoints.Length];
            menor = System.Math.Min(menor, Pista.Mod1(prox - Cfg.Checkpoints[i]));
        }
        double segundos = menor / Cfg.Cap;

        Assert.True(segundos > Cfg.BombaVoo * 2,
            $"o menor trecho dura {segundos:0.00} s, curto demais para o escudo valer");
    }
}
