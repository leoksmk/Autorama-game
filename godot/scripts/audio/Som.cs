// A camada de áudio inteira, vista de fora.
//
// Ela entra no jogo por dois caminhos, e nenhum dos dois toca em regra:
//
//   1. COMO IEfeitos. A Corrida já anuncia prêmio, ataque, impacto, escudo e
//      superaquecimento para quem quiser desenhar. O som se inscreve no mesmo
//      lugar que o Efeitos3D, através do EfeitosCompostos aqui embaixo. Zero
//      linhas novas no núcleo, e a paridade com a versão em Python segue
//      intacta.
//   2. OBSERVANDO. Volta completada, última volta, ultrapassagem, roleta
//      girando, calor no vermelho e o próprio motor não são eventos da regra —
//      são estado. O Som lê esse estado a cada quadro e decide sozinho. Ler é
//      de graça; inventar evento novo no núcleo custaria paridade com a
//      versão 2D e os testes.
//
// Tudo é sintetizado uma vez, no _Ready. Depois disso não há alocação de áudio
// nem leitura de arquivo durante a corrida.

using System;
using System.Collections.Generic;
using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby.Audio;

/// <summary>Reparte os eventos da regra entre vários interessados (o 3D e o som).</summary>
public sealed class EfeitosCompostos : IEfeitos
{
    private readonly IEfeitos[] _partes;

    public EfeitosCompostos(params IEfeitos[] partes) => _partes = partes;

    public void Limpar() { foreach (var p in _partes) p.Limpar(); }
    public void Largada() { foreach (var p in _partes) p.Largada(); }
    public void Premio(Nave n, Item i) { foreach (var p in _partes) p.Premio(n, i); }
    public void Ataque(Nave o, Nave a, Item t, bool e) { foreach (var p in _partes) p.Ataque(o, a, t, e); }
    public void Impacto(Nave a, Item t, ResultadoAtaque r) { foreach (var p in _partes) p.Impacto(a, t, r); }
    public void EscudoLevantado(Nave n) { foreach (var p in _partes) p.EscudoLevantado(n); }
    public void Superaquecimento(Nave n) { foreach (var p in _partes) p.Superaquecimento(n); }
}

public partial class Som : Node, IEfeitos
{
    /// <summary>Acima disto o alarme de calor entra; abaixo, sai sozinho.</summary>
    private const double CalorAlarme = 0.62;

    /// <summary>Carência entre dois avisos de ultrapassagem. Sem ela, empate vira metralhadora.</summary>
    private const double EsperaUltrapassagem = 3.0;

    private const int Vozes = 24;

    private Corrida _corrida = null!;
    private Trilha _trilha = null!;
    private readonly MotorSom[] _motores = new MotorSom[2];
    private readonly AudioStreamPlayer[] _alarme = new AudioStreamPlayer[2];

    // Rodízio de vozes para os sons de disparo único.
    private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[Vozes];
    private int _proxima;

    // Amostras prontas. O sufixo [2] é por pista: timbre e lado do estéreo.
    private readonly AudioStreamWav[] _disparo = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _bombaLancada = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _explosao = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _tiroImpacto = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _escudoLigado = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _escudoBloqueou = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _errou = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _caixaAbriu = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _tique = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _checkpoint = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _volta = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _superaqueceu = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _vitoria = new AudioStreamWav[2];
    private readonly AudioStreamWav[] _derrota = new AudioStreamWav[2];
    private readonly Dictionary<(int Lane, Item Item), AudioStreamWav> _revelacao = new();

    private AudioStreamWav _contagemBipe = null!;
    private AudioStreamWav _contagemVerde = null!;
    private AudioStreamWav _largada = null!;
    private AudioStreamWav _ultimaVolta = null!;
    private AudioStreamWav _ultrapassagem = null!;
    private AudioStreamWav _clique = null!;

    // Memória do quadro anterior: é dela que saem os eventos que a regra não anuncia.
    private readonly int[] _voltasAntes = new int[2];
    private readonly double[] _desdeCliqueAntes = new double[2];
    private readonly Item[] _faceAntes = new Item[2];
    private readonly EstadoRoleta[] _roletaAntes = new EstadoRoleta[2];
    private EstadoApp _estadoAntes = EstadoApp.Atracao;
    private int _liderAntes = -1;
    private double _dt = 1.0 / 60.0;
    private double _esperaUltrapassagem;
    private int _bipesDados;
    private bool _avisouUltimaVolta;
    private bool _pontuouResultado;

    public override void _Ready()
    {
        Mixagem.Montar();
        ulong t0 = Time.GetTicksMsec();
        Sintetizar();

        for (int i = 0; i < Vozes; i++)
        {
            var p = new AudioStreamPlayer { Bus = Mixagem.Sfx };
            AddChild(p);
            _pool[i] = p;
        }

        for (int lane = 0; lane < 2; lane++)
        {
            var m = new MotorSom(lane);
            AddChild(m);
            _motores[lane] = m;

            var a = new AudioStreamPlayer
            {
                Stream = Banco.AlarmeCalor(Banco.Tom(lane)).ParaWav(Banco.Pan(lane), loop: true),
                Bus = Mixagem.Sfx,
                VolumeDb = -60f,
            };
            AddChild(a);
            a.Play();
            _alarme[lane] = a;
        }

        _trilha = new Trilha();
        AddChild(_trilha);

        GD.Print($"som: banco sintetizado em {Time.GetTicksMsec() - t0} ms");
    }

    private void Sintetizar()
    {
        for (int lane = 0; lane < 2; lane++)
        {
            double tom = Banco.Tom(lane);
            double pan = Banco.Pan(lane);
            _disparo[lane] = Banco.Disparo(tom).ParaWav(pan);
            _bombaLancada[lane] = Banco.BombaLancada(tom).ParaWav(pan);
            _explosao[lane] = Banco.Explosao().ParaWav(pan * 0.7);
            _tiroImpacto[lane] = Banco.TiroImpacto(tom).ParaWav(pan);
            _escudoLigado[lane] = Banco.EscudoLigado(tom).ParaWav(pan);
            _escudoBloqueou[lane] = Banco.EscudoBloqueou(tom).ParaWav(pan);
            _errou[lane] = Banco.AtaqueErrou(tom).ParaWav(pan);
            _caixaAbriu[lane] = Banco.CaixaAbriu(tom).ParaWav(pan);
            _tique[lane] = Banco.RoletaTique(tom).ParaWav(pan);
            _checkpoint[lane] = Banco.Checkpoint(tom).ParaWav(pan);
            _volta[lane] = Banco.Volta(tom).ParaWav(pan);
            _superaqueceu[lane] = Banco.Superaquecimento(tom).ParaWav(pan);
            _vitoria[lane] = Banco.Vitoria().ParaWav(pan * 0.8);
            _derrota[lane] = Banco.Derrota().ParaWav(pan * 0.8);
            foreach (Item item in Roleta.Ordem)
                _revelacao[(lane, item)] = Banco.Revelacao(item, tom).ParaWav(pan);
        }

        _contagemBipe = Banco.ContagemBipe().ParaWav();
        _contagemVerde = Banco.ContagemVerde().ParaWav();
        _largada = Banco.Largada().ParaWav();
        _ultimaVolta = Banco.UltimaVolta().ParaWav();
        _ultrapassagem = Banco.Ultrapassagem().ParaWav();
        _clique = Banco.Clique().ParaWav();
    }

    public void Configurar(Corrida corrida)
    {
        _corrida = corrida;
        for (int i = 0; i < 2; i++)
        {
            _faceAntes[i] = Roleta.Ordem[0];
            _roletaAntes[i] = EstadoRoleta.Parada;
            _desdeCliqueAntes[i] = 99.0;
        }
    }

    // -- disparo de vozes ----------------------------------------------------

    private void Tocar(AudioStream som, float db = 0f)
    {
        // Prefere uma voz livre; se todas estiverem ocupadas, corta a mais
        // antiga. Perder o começo de um tique é melhor do que engolir a bomba.
        for (int k = 0; k < Vozes; k++)
        {
            int i = (_proxima + k) % Vozes;
            if (_pool[i].Playing)
                continue;
            _proxima = (i + 1) % Vozes;
            Disparar(_pool[i], som, db);
            return;
        }
        var voz = _pool[_proxima];
        _proxima = (_proxima + 1) % Vozes;
        Disparar(voz, som, db);
    }

    private static void Disparar(AudioStreamPlayer voz, AudioStream som, float db)
    {
        voz.Stream = som;
        voz.VolumeDb = db;
        voz.Play();
    }

    // -- IEfeitos: o que a regra já anuncia ----------------------------------

    public void Limpar()
    {
        foreach (var p in _pool)
            if (p.Playing) p.Stop();
        for (int i = 0; i < 2; i++)
        {
            if (_alarme[i] is not null) _alarme[i].VolumeDb = -60f;
            _motores[i]?.Definir(0, 0, travado: false, lento: false, ligado: false, clique: false);
            _voltasAntes[i] = 0;
            _roletaAntes[i] = EstadoRoleta.Parada;
            _desdeCliqueAntes[i] = 99.0;
        }
        _liderAntes = -1;
        _avisouUltimaVolta = false;
        _pontuouResultado = false;
        _esperaUltrapassagem = 0.0;
    }

    public void Largada() => Tocar(_largada, 1f);

    public void Premio(Nave nave, Item item) =>
        Tocar(_revelacao[(nave.Lane, item)], item == Item.Nada ? -3f : 0f);

    public void Ataque(Nave origem, Nave alvo, Item tipo, bool errou)
    {
        if (errou)
        {
            Tocar(_errou[origem.Lane], -2f);
            return;
        }
        Tocar(tipo == Item.Bomba ? _bombaLancada[origem.Lane] : _disparo[origem.Lane]);
    }

    public void Impacto(Nave alvo, Item tipo, ResultadoAtaque resultado)
    {
        if (resultado == ResultadoAtaque.Bloqueado)
        {
            Tocar(_escudoBloqueou[alvo.Lane], 1f);
            return;
        }
        Tocar(tipo == Item.Bomba ? _explosao[alvo.Lane] : _tiroImpacto[alvo.Lane], 2f);
    }

    public void EscudoLevantado(Nave nave) => Tocar(_escudoLigado[nave.Lane], -1f);

    public void Superaquecimento(Nave nave) => Tocar(_superaqueceu[nave.Lane], 1f);

    // -- observação: o que a regra não anuncia -------------------------------

    /// <summary>
    /// Uma vez por quadro, DEPOIS de Corrida.Atualizar — é o que garante que
    /// CheckpointCruzado ainda esteja de pé quando este código o lê.
    /// </summary>
    public void Atualizar(double dt, EstadoApp estado, double contagem, bool correndo)
    {
        if (_corrida is null)
            return;

        _dt = Math.Min(dt, 0.05);
        _esperaUltrapassagem = Math.Max(0.0, _esperaUltrapassagem - _dt);

        if (estado != _estadoAntes)
        {
            if (estado == EstadoApp.Contagem)
                _bipesDados = 0;
            if (estado is EstadoApp.Contagem or EstadoApp.Atracao)
                _pontuouResultado = false;
            _estadoAntes = estado;
        }

        Contagem(estado, contagem);
        foreach (var nave in _corrida.Naves)
            PorNave(nave, correndo);
        Disputa(correndo);
        Resultado(estado);

        // A trilha abaixa durante a corrida: o palco é dos motores.
        _trilha.Ambiente = estado == EstadoApp.Corrida ? 0.5 : 1.0;
        _trilha.Pulso = correndo ? 1.0 : 0.0;
        _trilha.Tensao = correndo && _avisouUltimaVolta ? 1.0 : 0.0;
    }

    /// <summary>Três bipes graves e o verde, nos mesmos limiares das luzes da pista.</summary>
    private void Contagem(EstadoApp estado, double contagem)
    {
        if (estado != EstadoApp.Contagem)
            return;
        int devidos = (int)Math.Floor(Cfg.ContagemDuracao - contagem);
        while (_bipesDados < Math.Min(3, devidos))
        {
            _bipesDados++;
            Tocar(_contagemBipe, -1f);
        }
        if (contagem <= 0.0 && _bipesDados < 4)
        {
            _bipesDados = 4;
            Tocar(_contagemVerde, 1f);
        }
    }

    private void PorNave(Nave nave, bool correndo)
    {
        int lane = nave.Lane;

        // Clique do jogador: DesdeClique zera no aperto. Comparar com o quadro
        // anterior dá a borda sem que o núcleo precise anunciá-la.
        bool clique = nave.DesdeClique < _desdeCliqueAntes[lane] - 1e-9;
        _desdeCliqueAntes[lane] = nave.DesdeClique;

        _motores[lane].Definir(
            nave.Esforco,
            nave.Calor,
            travado: nave.Travado,
            lento: nave.Lento > 0.0,
            ligado: correndo && !nave.Terminou,
            clique: clique && correndo);

        // Alarme de calor: sobe no vermelho e sai sozinho quando o jogador
        // alivia. Cala junto com o motor quando a nave trava.
        bool alarmando = correndo && !nave.Terminou && !nave.Travado && nave.Calor > CalorAlarme;
        float alvo = alarmando
            ? (float)(-16.0 + 11.0 * (nave.Calor - CalorAlarme) / (1.0 - CalorAlarme))
            : -60f;
        _alarme[lane].VolumeDb = Mathf.MoveToward(_alarme[lane].VolumeDb, alvo, (float)(90.0 * _dt));

        _faceAntes[lane] = nave.Roleta.Face;

        if (!correndo)
        {
            _voltasAntes[lane] = nave.Voltas;
            _roletaAntes[lane] = nave.Roleta.Estado;
            return;
        }

        if (nave.CheckpointCruzado.HasValue)
            Tocar(_checkpoint[lane], -6f);

        if (nave.Voltas > _voltasAntes[lane])
        {
            _voltasAntes[lane] = nave.Voltas;
            if (!nave.Terminou)
                Tocar(_volta[lane], -2f);
            if (!_avisouUltimaVolta && nave.Voltas == Cfg.VoltasParaVencer - 1)
            {
                _avisouUltimaVolta = true;
                Tocar(_ultimaVolta, 0f);
            }
        }

        // Roleta: a janela abrindo convida, e cada troca de face vira um
        // estalo. Como a face desacelera sozinha (o EaseOut do sorteio), o
        // ritmo dos estalos já conta a caixa travando — sem código de ritmo.
        var roleta = nave.Roleta;
        if (roleta.Estado != _roletaAntes[lane])
        {
            if (roleta.Estado == EstadoRoleta.Oportunidade)
                Tocar(_caixaAbriu[lane], -4f);
            _roletaAntes[lane] = roleta.Estado;
        }
    }

    /// <summary>Troca de liderança, com carência para não repicar em empate.</summary>
    private void Disputa(bool correndo)
    {
        if (!correndo)
        {
            _liderAntes = -1;
            return;
        }
        int lider = _corrida.Posicao(_corrida.Naves[0]) == 1 ? 0 : 1;
        if (_liderAntes >= 0 && lider != _liderAntes && _esperaUltrapassagem <= 0.0)
        {
            _esperaUltrapassagem = EsperaUltrapassagem;
            Tocar(_ultrapassagem, -4f);
        }
        _liderAntes = lider;
    }

    /// <summary>
    /// Placar. A fanfarra sai do lado do vencedor e a queda do lado de quem
    /// perdeu — as duas são a mesma tríade em movimento contrário, então elas
    /// se somam em vez de brigar, e cada jogador ouve o próprio resultado no
    /// seu lado da cabine.
    /// </summary>
    private void Resultado(EstadoApp estado)
    {
        if (estado != EstadoApp.Resultado || _pontuouResultado)
            return;
        var vencedor = _corrida.Vencedor;
        if (vencedor is null)
            return;

        _pontuouResultado = true;
        Tocar(_vitoria[vencedor.Lane], 0f);
        Tocar(_derrota[1 - vencedor.Lane], -8f);
    }

    // -- interface -----------------------------------------------------------

    public void Interface() => Tocar(_clique, -6f);

    /// <summary>Silêncio geral. A corrida continua — só a mesa emudece.</summary>
    public void AlternarSurdina()
    {
        bool nova = !Mixagem.EstaEmSurdina();
        Mixagem.Surdina(nova);
        if (!nova)
            Interface();
    }

    public bool EmSurdina => Mixagem.EstaEmSurdina();
}
