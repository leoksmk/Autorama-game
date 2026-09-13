// Fontes de entrada, uma por jogador: teclado, controle ESP ou CPU.
//
// Cada jogador escolhe a sua na tela de atração (teclas 1 e 2) e a escolha
// fica salva. Dá para misturar: um no controle ESP e o outro no teclado
// enquanto o segundo controle não fica pronto, ou alguém contra a CPU.
//
// Toda fonte entrega a mesma coisa à regra: um Pulso com as BORDAS de subida
// do frame. A regra nunca sabe de onde o aperto veio.

using System;
using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby.Entrada;

public enum TipoFonte { Teclado, Controle, Cpu }

public interface IFonteEntrada
{
    TipoFonte Tipo { get; }
    Pulso Ler(double dt);
    /// <summary>Pronta para jogar: teclado e CPU sempre; controle só conectado.</summary>
    bool Pronta { get; }
    string Descricao { get; }
    /// <summary>O que mostrar no miolo da caixa de item: qual botão resolve.</summary>
    string RotuloAcao { get; }
    bool AceleradorSegurado { get; }
}

public sealed class FonteTeclado : IFonteEntrada
{
    private readonly Key _acel, _acao;
    private readonly string _rotuloAcel, _rotuloAcao;
    private readonly BordaBotao _bordaAcel = new(), _bordaAcao = new();

    public FonteTeclado(Key acel, Key acao, string rotuloAcel, string rotuloAcao)
    {
        _acel = acel;
        _acao = acao;
        _rotuloAcel = rotuloAcel;
        _rotuloAcao = rotuloAcao;
    }

    public TipoFonte Tipo => TipoFonte.Teclado;
    public bool Pronta => true;
    public string Descricao => $"teclado · {_rotuloAcel} acelera, {_rotuloAcao} ação";
    public string RotuloAcao => _rotuloAcao;
    public bool AceleradorSegurado { get; private set; }

    public Pulso Ler(double dt)
    {
        bool acel = Input.IsPhysicalKeyPressed(_acel);
        AceleradorSegurado = acel;
        return new Pulso(_bordaAcel.Atualizar(acel), _bordaAcao.Atualizar(Input.IsPhysicalKeyPressed(_acao)));
    }
}

public sealed class FonteControle : IFonteEntrada
{
    private readonly int _lane;
    private readonly GerenteSerial _gerente;
    private ControleSerial? _atual;

    public FonteControle(int lane, GerenteSerial gerente)
    {
        _lane = lane;
        _gerente = gerente;
    }

    public TipoFonte Tipo => TipoFonte.Controle;
    public bool Pronta => _atual is { Vivo: true };
    public string RotuloAcao => "AÇÃO";
    public bool AceleradorSegurado => _atual?.AceleradorSegurado ?? false;

    public string Descricao => _atual is { Vivo: true }
        ? $"controle ESP · {_atual.Porta} · id {_atual.Id}"
        : $"controle ESP (id {_lane + 1}) · aguardando conexão";

    public Pulso Ler(double dt)
    {
        var c = _gerente.ParaJogador(_lane);
        if (!ReferenceEquals(c, _atual))
        {
            // Controle trocado ou recém-conectado: descarta o que acumulou antes.
            _atual = c;
            _atual?.TirarPulso();
            return Pulso.Nenhum;
        }
        return _atual?.TirarPulso() ?? Pulso.Nenhum;
    }
}

/// <summary>
/// Adversário da CPU: martela forte enquanto o calor deixa e alivia antes de
/// cortar, gira sempre que a caixa abre, ataca quando o alvo está no alcance e
/// levanta o escudo quando vê um ataque vindo.
/// </summary>
public sealed class FonteCpu : IFonteEntrada
{
    private readonly Corrida _corrida;
    private readonly int _lane;
    private readonly Random _rng;
    private double _fase, _espera, _paciencia;

    public FonteCpu(Corrida corrida, int lane, int semente)
    {
        _corrida = corrida;
        _lane = lane;
        _rng = new Random(semente);
    }

    public TipoFonte Tipo => TipoFonte.Cpu;
    public bool Pronta => true;
    public string Descricao => "CPU";
    public string RotuloAcao => "CPU";
    public bool AceleradorSegurado => false;

    public Pulso Ler(double dt)
    {
        var n = _corrida.Naves[_lane];
        var adv = _corrida.Adversario(n);

        double limite = 0.68 + _lane * 0.08;
        double hz = n.Superaquecimento > 0 || n.Atordoado > 0 ? 0 : n.Calor > limite ? 3.8 : 9.0;
        bool clique = false;
        if (hz > 0)
        {
            _fase += dt * hz;
            if (_fase >= 1.0)
            {
                _fase -= 1.0;
                clique = true;
            }
        }
        else
        {
            _fase = 0.99;
        }

        bool acao = false;
        if (n.Slot is null && n.Roleta.PodeGirar)
        {
            _espera += dt;
            if (_espera > 0.15 + _rng.NextDouble() * 0.25)
            {
                acao = true;
                _espera = 0;
            }
        }
        else if (n.Slot is Item.Tiro or Item.Bomba)
        {
            double alcance = n.Slot == Item.Tiro ? Cfg.TiroAlcance : Cfg.BombaAlcance;
            acao = Pista.DistanciaCurta(n.T, adv.T) < alcance * 0.8;
        }
        else if (n.Slot == Item.Escudo)
        {
            _paciencia += dt;
            bool ameaca = adv.Slot is Item.Tiro or Item.Bomba && Pista.DistanciaCurta(n.T, adv.T) < 0.16;
            if (ameaca || _paciencia > 6 + _rng.NextDouble() * 6)
            {
                acao = true;
                _paciencia = 0;
            }
        }
        return new Pulso(clique, acao);
    }
}

/// <summary>Escolha teclado / controle / CPU de cada jogador, salva entre sessões.</summary>
public static class ConfigControles
{
    private const string Arquivo = "user://controles.cfg";

    public static TipoFonte[] Carregar()
    {
        var tipos = new[] { TipoFonte.Teclado, TipoFonte.Teclado };
        var cf = new ConfigFile();
        if (cf.Load(Arquivo) != Error.Ok)
            return tipos;
        for (int i = 0; i < 2; i++)
            if (Enum.TryParse(cf.GetValue("controles", $"p{i + 1}", "Teclado").AsString(), out TipoFonte t))
                tipos[i] = t;
        return tipos;
    }

    public static void Salvar(TipoFonte[] tipos)
    {
        var cf = new ConfigFile();
        for (int i = 0; i < tipos.Length; i++)
            cf.SetValue("controles", $"p{i + 1}", tipos[i].ToString());
        cf.Save(Arquivo);
    }

    public static string Nome(TipoFonte t) => t switch
    {
        TipoFonte.Controle => "controle ESP",
        TipoFonte.Cpu => "CPU",
        _ => "teclado",
    };

    public static TipoFonte Proximo(TipoFonte t) => (TipoFonte)(((int)t + 1) % 3);
}
