// Controles ESP32 pela serial USB.
//
// Uma thread de varredura olha as portas COM a cada 1,5 s. Porta nova é
// sondada numa tarefa própria: abre, manda "?", espera o "HELLO ORBITAL".
// Quem responde vira um ControleSerial, com uma thread só para ler linhas.
// Nada disso roda na thread do jogo: uma porta Bluetooth que trava ao abrir,
// ou um cabo puxado no meio da corrida, não congela a tela.
//
// Quem é ÍON e quem é ÍGNIS vem do id que o próprio ESP informa — nunca do
// número da porta, que o Windows troca quando o cabo muda de entrada USB.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrbitalDerby.Core;

namespace OrbitalDerby.Entrada;

public sealed class ControleSerial : IDisposable
{
    private readonly SerialPort _porta;
    private readonly ConcurrentQueue<Tecla> _fila = new();
    private volatile bool _aberto = true;
    private long _ultimaLinha;

    public string Porta { get; }
    public int Id { get; private set; }
    public string Versao { get; private set; }
    public volatile bool AceleradorSegurado;
    public volatile bool AcaoSegurada;

    public ControleSerial(SerialPort porta, Ola ola)
    {
        _porta = porta;
        Porta = porta.PortName;
        Id = ola.Id;
        Versao = ola.Versao;
        Tocar();
        new Thread(Ler) { IsBackground = true, Name = $"serial {Porta}" }.Start();
    }

    /// <summary>Porta aberta e com sinal de vida recente (o ESP pulsa a cada 500 ms).</summary>
    public bool Vivo =>
        _aberto && DateTime.UtcNow.Ticks - Interlocked.Read(ref _ultimaLinha)
                   < TimeSpan.FromSeconds(ProtocoloControle.TimeoutPulsacao).Ticks;

    private void Tocar() => Interlocked.Exchange(ref _ultimaLinha, DateTime.UtcNow.Ticks);

    private void Ler()
    {
        while (_aberto)
        {
            try
            {
                string linha = _porta.ReadLine();
                Tocar();
                switch (ProtocoloControle.Interpretar(linha))
                {
                    case Tecla t:
                        if (t.Botao == BotaoControle.Acelerador)
                            AceleradorSegurado = t.Pressionado;
                        else
                            AcaoSegurada = t.Pressionado;
                        _fila.Enqueue(t);
                        break;
                    case Pulsacao p:
                        AceleradorSegurado = p.AceleradorPressionado;
                        AcaoSegurada = p.AcaoPressionada;
                        break;
                    case Ola o:
                        Id = o.Id;
                        Versao = o.Versao;
                        break;
                }
            }
            catch (TimeoutException)
            {
                if (!Vivo)
                    Fechar();
            }
            catch (Exception)
            {
                Fechar();                    // cabo puxado, porta sumiu
            }
        }
    }

    /// <summary>
    /// Bordas de subida acumuladas desde a última chamada. Vem direto dos
    /// eventos do ESP, não de uma leitura de estado por frame: um aperto curto
    /// que caiba inteiro entre dois frames não se perde.
    /// </summary>
    public Pulso TirarPulso()
    {
        bool acel = false, acao = false;
        while (_fila.TryDequeue(out var t))
        {
            if (!t.Pressionado) continue;
            if (t.Botao == BotaoControle.Acelerador) acel = true; else acao = true;
        }
        return new Pulso(acel, acao);
    }

    public void Enviar(string comando)
    {
        try { _porta.Write(comando + "\n"); }
        catch (Exception) { Fechar(); }
    }

    private void Fechar()
    {
        _aberto = false;
        try { _porta.Close(); } catch (Exception) { }
    }

    public void Dispose() => Fechar();
}

public sealed class GerenteSerial : IDisposable
{
    private readonly object _trava = new();
    private readonly List<ControleSerial> _controles = new();
    private readonly Dictionary<string, DateTime> _recusadas = new();
    private readonly HashSet<string> _sondando = new();
    private volatile bool _rodando;

    public void Iniciar()
    {
        if (_rodando) return;
        _rodando = true;
        new Thread(Varrer) { IsBackground = true, Name = "varredura serial" }.Start();
    }

    private void Varrer()
    {
        while (_rodando)
        {
            string[] portas;
            try { portas = SerialPort.GetPortNames(); }
            catch (Exception) { portas = Array.Empty<string>(); }

            lock (_trava)
            {
                _controles.RemoveAll(c =>
                {
                    if (c.Vivo) return false;
                    c.Dispose();
                    return true;
                });
                foreach (var p in portas.Distinct())
                {
                    if (_controles.Any(c => c.Porta == p) || _sondando.Contains(p)) continue;
                    // Porta que não é controle só é tentada de novo depois de um tempo.
                    if (_recusadas.TryGetValue(p, out var quando) && (DateTime.UtcNow - quando).TotalSeconds < 8) continue;
                    _sondando.Add(p);
                    string porta = p;
                    Task.Run(() => Sondar(porta));
                }
            }
            Thread.Sleep(1500);
        }
    }

    private void Sondar(string nome)
    {
        SerialPort? sp = null;
        try
        {
            // DTR e RTS desligados: em muitas placas eles resetam o ESP.
            var porta = new SerialPort(nome, ProtocoloControle.Baud)
            {
                NewLine = "\n",
                ReadTimeout = 250,
                WriteTimeout = 250,
                DtrEnable = false,
                RtsEnable = false,
                Encoding = Encoding.ASCII,
            };
            sp = porta;                                  // enquanto não for entregue, é nossa para fechar
            porta.Open();
            porta.DiscardInBuffer();
            porta.Write(ProtocoloControle.Pergunta + "\n");
            var fim = DateTime.UtcNow.AddSeconds(2.5);
            var repetir = DateTime.UtcNow.AddSeconds(0.5);
            while (DateTime.UtcNow < fim && _rodando)
            {
                MensagemControle msg;
                try { msg = ProtocoloControle.Interpretar(porta.ReadLine()); }
                catch (TimeoutException) { msg = new Desconhecida(""); }

                if (msg is Ola ola)
                {
                    var controle = new ControleSerial(porta, ola);
                    sp = null;                           // a porta agora é do controle
                    lock (_trava) _controles.Add(controle);
                    return;
                }
                // O ESP pode ter acabado de reiniciar ao abrir a porta: insiste.
                if (DateTime.UtcNow > repetir)
                {
                    porta.Write(ProtocoloControle.Pergunta + "\n");
                    repetir = DateTime.UtcNow.AddSeconds(0.5);
                }
            }
        }
        catch (Exception) { }
        finally
        {
            if (sp is not null)
            {
                try { sp.Close(); } catch (Exception) { }
                lock (_trava) _recusadas[nome] = DateTime.UtcNow;
            }
            lock (_trava) _sondando.Remove(nome);
        }
    }

    /// <summary>
    /// Quem controla cada nave: primeiro o ESP que se apresentou com o id da
    /// nave (1 = ÍON, 2 = ÍGNIS); na falta dele, um controle sobrando — assim
    /// dois ESP com o mesmo id ainda funcionam, e o HUD avisa para acertar.
    /// </summary>
    public (ControleSerial? ion, ControleSerial? ignis) Atribuicao()
    {
        lock (_trava)
        {
            var vivos = _controles.Where(c => c.Vivo).OrderBy(c => c.Porta).ToList();
            var ion = vivos.FirstOrDefault(c => c.Id == 1);
            var ignis = vivos.FirstOrDefault(c => c.Id == 2);
            foreach (var c in vivos)
            {
                if (c == ion || c == ignis) continue;
                if (ion is null) ion = c;
                else if (ignis is null) ignis = c;
            }
            return (ion, ignis);
        }
    }

    public ControleSerial? ParaJogador(int lane)
    {
        var (ion, ignis) = Atribuicao();
        return lane == 0 ? ion : ignis;
    }

    public int Conectados
    {
        get { lock (_trava) return _controles.Count(c => c.Vivo); }
    }

    public bool IdsRepetidos
    {
        get
        {
            lock (_trava)
            {
                var ids = _controles.Where(c => c.Vivo).Select(c => c.Id).ToList();
                return ids.Count != ids.Distinct().Count();
            }
        }
    }

    public void Dispose()
    {
        _rodando = false;
        lock (_trava)
        {
            foreach (var c in _controles) c.Dispose();
            _controles.Clear();
        }
    }
}
