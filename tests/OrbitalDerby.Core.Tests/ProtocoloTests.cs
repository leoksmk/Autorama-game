using Xunit;

namespace OrbitalDerby.Core.Tests;

/// <summary>Leitura das linhas que o controle ESP32 manda pela serial.</summary>
public class ProtocoloTests
{
    [Theory]
    [InlineData("HELLO ORBITAL 1 1.0", 1, "1.0")]
    [InlineData("HELLO ORBITAL 2 1.3\r", 2, "1.3")]
    [InlineData("ets Jun  8 2016 00:22:57 rst:0x1 HELLO ORBITAL 2 1.0", 2, "1.0")]
    [InlineData("HELLO ORBITAL 1", 1, "?")]
    public void Reconhece_a_apresentacao_mesmo_com_lixo_de_boot(string linha, int id, string versao)
    {
        var m = Assert.IsType<Ola>(ProtocoloControle.Interpretar(linha));
        Assert.Equal(id, m.Id);
        Assert.Equal(versao, m.Versao);
    }

    [Theory]
    [InlineData("HELLO ORBITAL 3 1.0")]
    [InlineData("HELLO ORBITAL x")]
    [InlineData("HELLO OUTRACOISA 1 1.0")]
    public void Recusa_apresentacao_invalida(string linha)
    {
        Assert.IsType<Desconhecida>(ProtocoloControle.Interpretar(linha));
    }

    [Theory]
    [InlineData("T1", BotaoControle.Acelerador, true)]
    [InlineData("T0", BotaoControle.Acelerador, false)]
    [InlineData("A1", BotaoControle.Acao, true)]
    [InlineData(" A0\r", BotaoControle.Acao, false)]
    public void Le_os_botoes(string linha, BotaoControle botao, bool pressionado)
    {
        var m = Assert.IsType<Tecla>(ProtocoloControle.Interpretar(linha));
        Assert.Equal(botao, m.Botao);
        Assert.Equal(pressionado, m.Pressionado);
    }

    [Theory]
    [InlineData("K 10", true, false)]
    [InlineData("K 01", false, true)]
    [InlineData("K 00", false, false)]
    public void Le_a_pulsacao(string linha, bool acel, bool acao)
    {
        var m = Assert.IsType<Pulsacao>(ProtocoloControle.Interpretar(linha));
        Assert.Equal(acel, m.AceleradorPressionado);
        Assert.Equal(acao, m.AcaoPressionada);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ÿþ garbage")]
    [InlineData("T2")]
    [InlineData("K 1")]
    [InlineData("K 12")]
    public void Ignora_o_resto(string? linha)
    {
        Assert.IsType<Desconhecida>(ProtocoloControle.Interpretar(linha));
    }

    [Fact]
    public void Comando_de_id()
    {
        Assert.Equal("ID 2", ProtocoloControle.DefinirId(2));
    }
}
