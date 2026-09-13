// Protocolo entre o jogo e o controle ESP32, por serial USB a 115200 baud.
//
// Linhas de texto ASCII terminadas em '\n'. Texto, e não binário, para dar
// para depurar abrindo a porta num monitor serial qualquer.
//
// ESP -> PC
//     HELLO ORBITAL <id> <versao>   ao ligar e em resposta a "?"; id 1 = ÍON, 2 = ÍGNIS
//     T1 / T0                       acelerador apertado / solto (já sem repique)
//     A1 / A0                       ação apertada / solta
//     K <t><a>                      pulsação a cada 500 ms com o estado atual, ex. "K 10"
//
// PC -> ESP
//     ?                             pede o HELLO
//     ID <n>                        grava o id na memória do ESP (1 ou 2) e responde HELLO
//
// Reservado para o autorama físico (o firmware já ignora com segurança):
//     M <duty>                      PWM do motor da pista, 0..1000
//     C <n>                         ESP -> PC: passou pelo sensor de checkpoint n
//
// O debounce acontece no FIRMWARE. Com o acelerador de martelar, um botão que
// repica vira clique fantasma; a lógica do PC só tem o teto de 25 Hz como
// segunda linha de defesa.

using System;

namespace OrbitalDerby.Core;

public enum BotaoControle { Acelerador, Acao }

public abstract record MensagemControle;

/// <summary>Apresentação do controle: quem ele é.</summary>
public sealed record Ola(int Id, string Versao) : MensagemControle;

/// <summary>Um botão mudou de estado.</summary>
public sealed record Tecla(BotaoControle Botao, bool Pressionado) : MensagemControle;

/// <summary>Sinal de vida com o estado atual dos dois botões.</summary>
public sealed record Pulsacao(bool AceleradorPressionado, bool AcaoPressionada) : MensagemControle;

/// <summary>Qualquer outra coisa: ruído de boot do ESP, lixo de linha. Ignorada.</summary>
public sealed record Desconhecida(string Linha) : MensagemControle;

public static class ProtocoloControle
{
    public const int Baud = 115200;
    public const string Pergunta = "?";
    public const string Assinatura = "HELLO ORBITAL";

    /// <summary>Sem pulsação por este tempo, o controle é dado como desconectado.</summary>
    public const double TimeoutPulsacao = 1.6;

    public static string DefinirId(int id) => $"ID {id}";

    public static MensagemControle Interpretar(string? linha)
    {
        if (string.IsNullOrWhiteSpace(linha))
            return new Desconhecida(linha ?? "");

        string s = linha.Trim();

        // O ESP32 cospe o log da ROM ao reiniciar (e o Windows reinicia o ESP
        // ao abrir a porta em várias placas). Procurar a assinatura em qualquer
        // ponto da linha tolera lixo grudado antes dela.
        int i = s.IndexOf(Assinatura, StringComparison.Ordinal);
        if (i >= 0)
        {
            var partes = s[(i + Assinatura.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length >= 1 && int.TryParse(partes[0], out int id) && id is 1 or 2)
                return new Ola(id, partes.Length >= 2 ? partes[1] : "?");
            return new Desconhecida(s);
        }

        switch (s)
        {
            case "T1": return new Tecla(BotaoControle.Acelerador, true);
            case "T0": return new Tecla(BotaoControle.Acelerador, false);
            case "A1": return new Tecla(BotaoControle.Acao, true);
            case "A0": return new Tecla(BotaoControle.Acao, false);
        }

        if (s.Length == 4 && s[0] == 'K' && s[1] == ' '
            && s[2] is '0' or '1' && s[3] is '0' or '1')
            return new Pulsacao(s[2] == '1', s[3] == '1');

        return new Desconhecida(s);
    }
}

/// <summary>
/// Converte estado contínuo em borda de subida. O botão só vale no instante em
/// que é apertado — segurar não repete o comando.
/// </summary>
public sealed class BordaBotao
{
    private bool _anterior;

    public bool Atualizar(bool estado)
    {
        bool borda = estado && !_anterior;
        _anterior = estado;
        return borda;
    }

    public void Reiniciar() => _anterior = false;
}
