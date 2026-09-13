// Ponto de entrada do Orbital Derby 3D: monta o mundo, liga as entradas e
// conduz as telas.
//
//     ATRAÇÃO -> CONTAGEM -> CORRIDA -> RESULTADO -> (R / AÇÃO) CONTAGEM
//
// Esta é a única camada que conhece teclado de sistema, janela e serial. A
// regra (Core.Corrida) recebe pulsos e escreve PWM; o mundo 3D só lê a regra.
//
// Argumentos (depois de "--" na linha de comando do Godot):
//     --p1=teclado|controle|cpu    fonte do ÍON
//     --p2=teclado|controle|cpu    fonte do ÍGNIS
//     --demo                       CPU contra CPU, começa e recomeça sozinho
//     --qualidade=alta|media|baixa preset gráfico inicial (Q troca durante o jogo)
//     --captura=<pasta>            roteiro de verificação: tira capturas e sai
//     --captura=rajada:<pasta>     60 fotos seguidas na câmera de perseguição
//     --sair-em=<segundos>         fecha sozinho (teste de fumaça)
//     --som-wav=<pasta>            grava o banco de sons em .wav e sai

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using OrbitalDerby.Core;
using OrbitalDerby.Entrada;
using OrbitalDerby.Interface;
using OrbitalDerby.Mundo;
using OrbitalDerby.Audio;

namespace OrbitalDerby;

public enum EstadoApp { Atracao, Contagem, Corrida, Resultado }

public partial class Main : Node3D
{
    private const double EsperaResultado = 2.5;

    private readonly SaidaNula _saida = new();
    private readonly GerenteSerial _serial = new();
    private readonly IFonteEntrada[] _fontes = new IFonteEntrada[2];
    private readonly bool[] _forcarAcao = new bool[2];
    private TipoFonte[] _tipos = { TipoFonte.Teclado, TipoFonte.Teclado };

    private Corrida _corrida = null!;
    private Efeitos3D _fx = null!;
    private PistaVisual _pista = null!;
    private CameraRig _camera = null!;
    private NaveVisual[] _naves = null!;
    private Hud _hud = null!;
    private Som _som = null!;
    private readonly AncoraNave[] _ancoras = new AncoraNave[2];
    private Ambiente _ambiente = null!;
    private Estacao _estacao = null!;
    private Asteroides _asteroides = null!;
    private Ambiente.Qualidade _qualidade = Ambiente.Qualidade.Alta;

    private EstadoApp _estado = EstadoApp.Atracao;
    private double _contagem, _espera, _relogioDemo, _sairEm = -1, _vivo;
    private bool _demo;
    private Captura? _captura;
    private string? _exportarSom;

    public override void _Ready()
    {
        _fx = new Efeitos3D();
        // O som sintetiza o banco inteiro no _Ready — que o AddChild dispara
        // agora —, então ele já está de pé quando a Corrida nasce e chama
        // Limpar() pela primeira vez.
        _som = new Som();
        AddChild(_som);
        // Os eventos da regra vão para o 3D e para o som pelo mesmo caminho:
        // a interface IEfeitos não mudou, ganhou um segundo ouvinte.
        _corrida = new Corrida(_saida, new EfeitosCompostos(_fx, _som));

        _ambiente = new Ambiente();
        AddChild(_ambiente);
        _estacao = new Estacao();
        AddChild(_estacao);
        _pista = new PistaVisual();
        AddChild(_pista);
        _asteroides = new Asteroides();
        AddChild(_asteroides);
        _naves = new[] { new NaveVisual(0), new NaveVisual(1) };
        foreach (var n in _naves) AddChild(n);
        _camera = new CameraRig();
        AddChild(_camera);
        AddChild(_fx);

        var camada = new CanvasLayer();
        AddChild(camada);
        _hud = new Hud();
        camada.AddChild(_hud);

        _tipos = ConfigControles.Carregar();
        LerArgumentos(OS.GetCmdlineUserArgs());

        _serial.Iniciar();
        for (int i = 0; i < 2; i++)
            _fontes[i] = CriarFonte(i, _tipos[i]);

        _fx.Configurar(_corrida, _naves, _camera);
        _som.Configurar(_corrida);
        _fx.Marcador = _hud.Marcar;
        _hud.Ancoras = _ancoras;
        _hud.Configurar(_corrida, _saida, _fontes, _serial);
        AplicarQualidade(_qualidade);
        EntrarAtracao();

        if (_exportarSom is not null)
        {
            ulong t0 = Time.GetTicksMsec();
            int n = Exportar.Tudo(_exportarSom);
            GD.Print($"som: {n} arquivos .wav em {_exportarSom} ({Time.GetTicksMsec() - t0} ms)");
            GetTree().Quit();
        }
    }

    private void LerArgumentos(string[] args)
    {
        foreach (var a in args)
        {
            string[] kv = a.TrimStart('-').Split('=', 2);
            string chave = kv[0].ToLowerInvariant();
            string valor = kv.Length > 1 ? kv[1] : "";
            switch (chave)
            {
                case "p1": _tipos[0] = TipoDe(valor, _tipos[0]); break;
                case "p2": _tipos[1] = TipoDe(valor, _tipos[1]); break;
                case "demo":
                    _demo = true;
                    _tipos = new[] { TipoFonte.Cpu, TipoFonte.Cpu };
                    break;
                case "captura":
                    _demo = true;
                    _tipos = new[] { TipoFonte.Cpu, TipoFonte.Cpu };
                    _captura = new Captura(this, valor);
                    break;
                case "qualidade":
                    _qualidade = valor.ToLowerInvariant() switch
                    {
                        "media" or "média" => Ambiente.Qualidade.Media,
                        "baixa" => Ambiente.Qualidade.Baixa,
                        _ => Ambiente.Qualidade.Alta,
                    };
                    break;
                case "sair-em":
                    double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out _sairEm);
                    break;
                case "som-wav":
                    _exportarSom = string.IsNullOrWhiteSpace(valor) ? OS.GetUserDataDir() : valor;
                    break;
            }
        }
    }

    private static TipoFonte TipoDe(string s, TipoFonte padrao) => s.ToLowerInvariant() switch
    {
        "teclado" => TipoFonte.Teclado,
        "controle" or "serial" or "esp" => TipoFonte.Controle,
        "cpu" => TipoFonte.Cpu,
        _ => padrao,
    };

    private IFonteEntrada CriarFonte(int lane, TipoFonte tipo) => tipo switch
    {
        TipoFonte.Controle => new FonteControle(lane, _serial),
        TipoFonte.Cpu => new FonteCpu(_corrida, lane, 100 + lane),
        _ => lane == 0
            ? new FonteTeclado(Key.A, Key.S, "A", "S")
            : new FonteTeclado(Key.L, Key.K, "L", "K"),
    };

    private void AplicarQualidade(Ambiente.Qualidade q)
    {
        _qualidade = q;
        _ambiente.Aplicar(q);
        _asteroides.Sombras(q == Ambiente.Qualidade.Alta);
        _estacao.LuzNucleo.ShadowEnabled = q != Ambiente.Qualidade.Baixa;
    }

    // -- telas ---------------------------------------------------------------------

    private void EntrarAtracao()
    {
        _corrida.Reiniciar();
        _estado = EstadoApp.Atracao;
        _relogioDemo = 0;
        _pista.LuzesLargada(0, false);
    }

    private void IniciarContagem()
    {
        _corrida.Reiniciar();
        _contagem = Cfg.ContagemDuracao;
        _espera = 0;
        _relogioDemo = 0;
        _estado = EstadoApp.Contagem;
    }

    private void TrocarFonte(int lane)
    {
        if (_estado is not (EstadoApp.Atracao or EstadoApp.Resultado)) return;
        _tipos[lane] = ConfigControles.Proximo(_tipos[lane]);
        _fontes[lane] = CriarFonte(lane, _tipos[lane]);
        ConfigControles.Salvar(_tipos);
        _som.Interface();
    }

    // -- ciclo -----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        // Uma janela arrastada ou um soluço do sistema não teletransporta as naves.
        double dt = Math.Min(delta, 0.05);
        _vivo += delta;
        if (_sairEm > 0 && _vivo >= _sairEm)
        {
            GetTree().Quit();
            return;
        }

        var p1 = _fontes[0].Ler(dt);
        var p2 = _fontes[1].Ler(dt);
        if (_forcarAcao[0]) { p1 = p1 with { Acao = true }; _forcarAcao[0] = false; }
        if (_forcarAcao[1]) { p2 = p2 with { Acao = true }; _forcarAcao[1] = false; }

        switch (_estado)
        {
            case EstadoApp.Atracao:
                _relogioDemo += dt;
                if (p1.Acao || p2.Acao || (_demo && _captura is null && _relogioDemo > 6))
                    IniciarContagem();
                break;

            case EstadoApp.Contagem:
                _contagem -= dt;
                double decorrido = Cfg.ContagemDuracao - _contagem;
                _pista.LuzesLargada(Math.Min(5, (int)(decorrido / 0.6) + 1), false);
                if (_contagem <= 0)
                {
                    _estado = EstadoApp.Corrida;
                    _pista.LuzesLargada(0, true);
                    _corrida.Anotar("Largada");
                    _fx.Largada();
                }
                break;

            case EstadoApp.Corrida:
                if (_corrida.Vencedor is not null)
                {
                    _espera += dt;
                    if (_espera >= EsperaResultado)
                        _estado = EstadoApp.Resultado;
                }
                break;

            case EstadoApp.Resultado:
                _relogioDemo += dt;
                if (p1.Acao || p2.Acao || (_demo && _captura is null && _relogioDemo > 8))
                    IniciarContagem();
                break;
        }

        bool correndo = _estado == EstadoApp.Corrida;
        _corrida.Atualizar(dt, correndo ? p1 : Pulso.Nenhum, correndo ? p2 : Pulso.Nenhum, correndo);

        for (int i = 0; i < 2; i++)
            _naves[i].Sincronizar(_corrida.Naves[i], dt);
        _fx.Atualizar(dt);
        // Depois da regra, de propósito: o som lê CheckpointCruzado, que vale
        // só no quadro em que o sensor dispara.
        _som.Atualizar(dt, _estado, _contagem, correndo);
        AcenderPortais();

        int lider = _corrida.Posicao(_corrida.Naves[0]) == 1 ? 0 : 1;
        _camera.Cinematica = _estado == EstadoApp.Atracao;
        _camera.Atualizar(dt, _naves, lider);
        AtualizarAncoras();
        _hud.Atualizar(dt, _estado, _contagem,
            $"{CameraRig.NomeDoModo(_camera.ModoAtual)} (C) · qualidade {Ambiente.Nome(_qualidade)} (Q)"
            + $" · som {(_som.EmSurdina ? "mudo" : "ligado")} (M)");

        _captura?.Passo(delta);
    }

    /// <summary>Onde cada nave aparece na tela: o HUD prende nela o nome e os marcadores.</summary>
    private void AtualizarAncoras()
    {
        var cam = _camera.Camera;
        var tela = GetViewport().GetVisibleRect().Size;
        for (int i = 0; i < 2; i++)
        {
            var q = _naves[i].GlobalTransform;
            var ponto = q.Origin + q.Basis.Y * 0.9f;
            if (cam.IsPositionBehind(ponto))
            {
                _ancoras[i] = default;
                continue;
            }
            var p = cam.UnprojectPosition(ponto);
            bool naTela = p.X > -60f && p.X < tela.X + 60f && p.Y > -60f && p.Y < tela.Y + 60f;
            // Com a câmera colada na nave (perseguição) o nome dela sai de cena.
            float alfa = Mathf.Clamp((cam.GlobalPosition.DistanceTo(ponto) - 12f) / 10f, 0f, 1f);
            _ancoras[i] = new AncoraNave(naTela, p, alfa);
        }
    }

    /// <summary>O portal do checkpoint acende na cor de quem está com a caixa aberta ali.</summary>
    private void AcenderPortais()
    {
        for (int cp = 0; cp < Cfg.Checkpoints.Length; cp++)
        {
            var dono = _corrida.Naves.FirstOrDefault(n =>
                n.Roleta.Estado == EstadoRoleta.Oportunidade && n.Roleta.Checkpoint == cp);
            if (dono is null)
                _pista.PortalEmRepouso(cp);
            else
                _pista.DefinirPortal(cp, Paleta.DoJogador(dono.Lane), 3.5f + 2f * MathF.Sin((float)_vivo * 14f));
        }
    }

    public override void _UnhandledInput(InputEvent evento)
    {
        if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.PhysicalKeycode)
        {
            case Key.Escape:
                GetTree().Quit();
                break;
            case Key.F11:
                DisplayServer.WindowSetMode(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen
                    ? DisplayServer.WindowMode.Windowed
                    : DisplayServer.WindowMode.Fullscreen);
                break;
            case Key.F3:
                _hud.MostrarFps = !_hud.MostrarFps;
                break;
            case Key.C:
                _camera.ProximoModo();
                _som.Interface();
                break;
            case Key.Q:
                AplicarQualidade((Ambiente.Qualidade)(((int)_qualidade + 1) % 3));
                _som.Interface();
                break;
            case Key.M:
                // Surdina geral. A corrida e os carrinhos seguem: numa feira
                // dá para calar o jogo sem parar a partida de ninguém.
                _som.AlternarSurdina();
                break;
            case Key.Space:
                if (_estado is EstadoApp.Atracao or EstadoApp.Resultado)
                    IniciarContagem();
                break;
            case Key.R:
                if (_estado is EstadoApp.Corrida or EstadoApp.Resultado)
                    IniciarContagem();
                break;
            case Key.Key1:
            case Key.Kp1:
                TrocarFonte(0);
                break;
            case Key.Key2:
            case Key.Kp2:
                TrocarFonte(1);
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
            _serial.Dispose();
    }

    public override void _ExitTree() => _serial.Dispose();

    // -- roteiro de verificação ----------------------------------------------------------

    /// <summary>
    /// Roda uma sequência fixa — atração, contagem, corrida, caixa, bomba,
    /// bloqueio, resultado — e salva uma captura de tela em cada ponto. Serve
    /// para conferir o visual sem ninguém jogando.
    /// </summary>
    private sealed class Captura
    {
        private readonly Main _m;
        private readonly string _pasta;
        private readonly (double t, string? nome, Action? acao)[] _roteiro;
        private double _t;
        private int _passo;

        public Captura(Main m, string pasta)
        {
            _m = m;
            // "rajada:<pasta>" troca o roteiro por fotos seguidas na câmera de
            // perseguição — serve para caçar artefato que só aparece às vezes.
            bool rajada = pasta.StartsWith("rajada:");
            if (rajada) pasta = pasta["rajada:".Length..];
            _pasta = string.IsNullOrWhiteSpace(pasta) ? OS.GetUserDataDir() : pasta;
            Directory.CreateDirectory(_pasta);
            var c = m;
            if (rajada)
            {
                var r = new System.Collections.Generic.List<(double, string?, Action?)>
                {
                    (1.0, null, () => c.IniciarContagem()),
                    (1.1, null, () => c._camera.ModoAtual = CameraRig.Modo.Perseguicao),
                };
                for (int i = 0; i < 60; i++)
                    r.Add((6.0 + i * 0.35, $"r{i:00}", null));
                r.Add((6.0 + 60 * 0.35 + 0.5, null, () => c.GetTree().Quit()));
                _roteiro = r.ToArray();
                return;
            }
            _roteiro = new (double, string?, Action?)[]
            {
                (3.0, "01_atracao", null),
                (3.2, null, () => c.IniciarContagem()),
                (4.8, "02_contagem", null),
                (11.0, "03_corrida", null),
                (11.1, null, () => c._camera.ModoAtual = CameraRig.Modo.Perseguicao),
                (13.0, "04_perseguicao", null),
                (13.1, null, () =>
                {
                    c._camera.ModoAtual = CameraRig.Modo.Transmissao;
                    c._corrida.Naves[0].Slot = null;
                    c._corrida.Naves[0].Roleta.Cancelar();
                    c._corrida.Naves[0].Roleta.Abrir(0);
                }),
                (13.5, "05_caixa_aberta", null),
                (13.6, null, () => c._corrida.Naves[0].Roleta.Girar(false, c._corrida.Rng, Item.Bomba)),
                (14.2, "06_caixa_girando", null),
                (15.2, "07_caixa_premio", null),
                (16.5, null, () =>
                {
                    var (a, b) = (c._corrida.Naves[0], c._corrida.Naves[1]);
                    b.T = Pista.Mod1(a.T + 0.05);
                    b.Escudo = false;
                    a.Slot = Item.Bomba;
                    c._forcarAcao[0] = true;
                }),
                (16.85, "08_bomba_no_ar", null),
                (17.12, "09_explosao", null),
                (18.5, null, () =>
                {
                    var (a, b) = (c._corrida.Naves[0], c._corrida.Naves[1]);
                    a.T = Pista.Mod1(b.T + 0.06);
                    a.Escudo = true;
                    b.Slot = Item.Tiro;
                    c._forcarAcao[1] = true;
                }),
                (18.95, "10_bloqueio", null),
                (19.5, null, () => c._camera.ModoAtual = CameraRig.Modo.VisaoGeral),
                (21.0, "11_visao_geral", null),
                (21.1, null, () =>
                {
                    c._camera.ModoAtual = CameraRig.Modo.Transmissao;
                    c._corrida.Naves[1].Voltas = Cfg.VoltasParaVencer;
                }),
                (24.5, "12_resultado", null),
                (25.0, null, () => c.GetTree().Quit()),
            };
        }

        public void Passo(double dt)
        {
            _t += dt;
            while (_passo < _roteiro.Length && _t >= _roteiro[_passo].t)
            {
                var (_, nome, acao) = _roteiro[_passo++];
                acao?.Invoke();
                if (nome is not null)
                    Salvar(nome);
            }
        }

        private void Salvar(string nome)
        {
            var imagem = _m.GetViewport().GetTexture().GetImage();
            string caminho = Path.Combine(_pasta, nome + ".png");
            imagem.SavePng(caminho);
            GD.Print($"captura: {caminho}  ({Engine.GetFramesPerSecond():0} fps)");
        }
    }
}
