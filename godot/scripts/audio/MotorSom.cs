// O motor de uma nave, sintetizado amostra a amostra, ao vivo.
//
// Por que não um loop gravado com o pitch esticado, que é o caminho fácil:
// neste jogo o acelerador é de MARTELAR. O que o jogador precisa ouvir não é
// "rápido ou devagar", é o próprio ritmo dele batendo. Esticar o pitch de um
// loop resolve a altura e destrói o timbre; aqui a frequência, o corte do
// filtro e a sujeira andam separados, e cada aperto entra como um golpe no
// pistão. É isso que fecha a alça de feedback do martelar.
//
// As camadas, de baixo para cima:
//
//   CORPO      parciais somadas à mão sobre a sub-oitava. As PARES são a série
//              harmônica do motor; as ÍMPARES caem no meio dela e são o
//              rosnado, que cresce com o esforço — o motor abre a garganta.
//   FORMANTES  três ressonâncias em frequências FIXAS, que NÃO seguem a
//              afinação. É o que dá caráter: conforme o motor sobe, seus
//              harmônicos atravessam os formantes e o timbre se transforma
//              sozinho. É o equivalente sintético de processar um bicho de
//              verdade, que é como se fazia som de nave na era analógica.
//   CASCO      um atraso de 3 ms realimentado. Ressonâncias fixas a cada
//              ~320 Hz: a impressão de metal em volta do propulsor.
//   AR         ruído cujo corte passeia sozinho. Ruído parado soa como chiado
//              de rádio; ruído que se mexe soa como algo queimando lá dentro.
//   GOLPE      um empurrão de afinação e volume por clique do jogador.
//   BURN       acima de 62% de esforço o motor muda de estado, não só de
//              volume. É a recompensa por martelar rápido.
//
// Mais a crepitação de quando o motor morre.
//
// O timbre é ADITIVO, e isso corrige um erro real de uma versão anterior. Ela
// usava dentes de serra saturados com filtro ressonante e soava estridente por
// quatro motivos somados, todos evitados aqui:
//
//   1. ALIASING. Serra por acumulador de fase tem harmônicos acima de Nyquist,
//      que dobram de volta como parciais DESAFINADAS — 18% da energia era
//      inarmônica. Aqui cada parcial é somada à mão e simplesmente não entra
//      se passar de 15 kHz, então aliasing não existe.
//   2. RESSONÂNCIA ESTREITA. Um filtro com Q alto põe um pico agudo bem em
//      3 kHz, onde o ouvido é mais sensível. O passa-baixa aqui não ressoa
//      (Q 0,7) e os formantes são largos (Q ~2).
//   3. ORDEM ERRADA. Saturar DEPOIS de filtrar cria harmônicos altos que já
//      não têm como ser removidos. Aqui a saturação vem antes do filtro.
//   4. ASPEREZA. Desafinar vozes em 0,75% vira batimento de dezenas de hertz
//      nos harmônicos altos, que o ouvido lê como aspereza. Todo o movimento
//      aqui é lento: LFO de 0,27 Hz e gêmea 0,18% acima.
//
// O brilho cresce mudando o EXPOENTE do decaimento das parciais, não o volume
// delas: o motor abre sem nunca gritar. Medido no talo, 10% da energia fica
// acima de 2 kHz na voz PROPULSOR e 17% na CAÇA, contra 44% de uma versão
// antiga que soava estridente.
//
// Cada pista tem sua voz (ÍON mais agudo à esquerda, ÍGNIS mais grave à
// direita) e escreve direto nos dois canais — sem posicionamento 3D: numa
// cabine de autorama o que importa é cada jogador achar o próprio motor.
//
// Os NÚMEROS de tudo isso moram em ReceitaMotor, não aqui: esta é a máquina, e
// cada receita é uma voz que ela sabe tocar (PROPULSOR, grave e fluido; CAÇA,
// agudo e uivante). Trocar de voz em corrida não passa por aqui nem custa
// recompilação — é a tecla N.

using System;
using Godot;

namespace OrbitalDerby.Audio;

/// <summary>
/// A síntese do motor, sem o Godot por perto: recebe o estado da nave e
/// devolve uma amostra por chamada. Fica separada do nó para poder rodar fora
/// da árvore de cena — é assim que a exportação em .wav desenha o motor.
/// </summary>
public sealed class VozMotor
{
    /// <summary>Biquad com estado entre blocos. O mesmo corpo do filtro da Onda, ao vivo.</summary>
    private struct Filtro
    {
        private double _z1, _z2, _a0, _a1, _a2, _b1, _b2;

        public void Ajustar(double corte, double q, bool banda = false)
        {
            corte = Math.Clamp(corte, 40.0, Onda.Taxa * 0.45);
            double w = Math.Tau * corte / Onda.Taxa;
            double alfa = Math.Sin(w) / (2.0 * Math.Max(0.3, q));
            double cs = Math.Cos(w);
            double norma = 1.0 + alfa;
            if (banda)
            {
                _a0 = alfa / norma;
                _a1 = 0.0;
                _a2 = -_a0;
            }
            else
            {
                _a0 = (1.0 - cs) / 2.0 / norma;
                _a1 = (1.0 - cs) / norma;
                _a2 = _a0;
            }
            _b1 = -2.0 * cs / norma;
            _b2 = (1.0 - alfa) / norma;
        }

        public double Passar(double e)
        {
            double s = _a0 * e + _z1;
            _z1 = _a1 * e - _b1 * s + _z2;
            _z2 = _a2 * e - _b2 * s;
            return s;
        }
    }

    private const int PassoCoef = 32;          // recalcular o filtro a cada 32 amostras basta
    private const double Dt = 1.0 / Onda.Taxa;

    /// <summary>Teto das parciais. Acima disto nada entra, e sem isso volta o aliasing.</summary>
    private const double Teto = 15000.0;

    /// <summary>
    /// Quantas parciais o corpo tem. Elas correm sobre a SUB-OITAVA, então
    /// cobrem tanto os harmônicos do motor (as pares) quanto o rosnado que cai
    /// no meio deles (as ímpares) — por isso são o dobro do que a fundamental
    /// sozinha precisaria. Precisam ser muitas: com só oito, sobre 150 Hz, o
    /// som acabaria em 1,2 kHz e o motor ficaria abafado, sem definição. O que
    /// evita a aspereza não é ter poucos harmônicos — é não ter aliasing, nem
    /// ressonância estreita, nem batimento rápido. O laço para sozinho onde os
    /// pesos deixam de contar.
    /// </summary>
    private const int NParciais = 48;

    /// <summary>
    /// Deslocamento de fase de cada parcial. Com todas alinhadas, elas somam
    /// num pico estreito a cada volta e o resultado tem cara de pulso — o
    /// oposto de fluido. Espalhadas, a mesma energia vira uma onda lisa.
    /// </summary>
    private static readonly double[] Fases = SortearFases();

    private static double[] SortearFases()
    {
        // Semente fixa: o timbre tem de ser o mesmo em toda execução.
        var r = new Random(4242);
        var f = new double[NParciais + 1];
        for (int i = 0; i <= NParciais; i++)
            f[i] = r.NextDouble();
        return f;
    }

    /// <summary>Quanto do golpe sobra a cada amostra (constante de tempo de ~60 ms).</summary>
    private const double GolpeDecai = 0.99948;

    /// <summary>Os números que definem esta voz. Ver ReceitaMotor.</summary>
    private readonly ReceitaMotor _r;

    private readonly double _tom;

    /// <summary>
    /// O mesmo desvio de pista, mas pela metade, para os filtros. Aplicar o
    /// tom cheio aqui empilha timbre em cima de afinação e afasta demais as
    /// duas naves: o ÍGNIS ficava sem presença nenhuma.
    /// </summary>
    private readonly double _cor;

    private readonly Random _rng;

    // Fases: precisam sobreviver de um bloco ao outro, senão cada preenchimento
    // começaria um estalo novo. As parciais saem todas de _fase (multiplicar
    // por um inteiro não quebra na virada da volta), mas a sub-oitava e a voz
    // gêmea precisam das suas.
    private double _faseSub, _faseGemea, _faseFm, _fLfo, _fUivo, _fBalanco;
    private Filtro _lp, _lpAr, _fmt1, _fmt2, _fmt3;
    private int _conta;

    // Linha de atraso do casco: o comb que dá o corpo metálico em volta.
    private const int Casco = 256;
    private readonly double[] _casco = new double[Casco];
    private readonly int _cascoAtraso;
    private int _cascoPos;

    // Pesos das parciais, recalculados a cada bloco junto com o filtro.
    private readonly double[] _pesos = new double[NParciais + 1];
    private double _somaPesos = 1.0;
    private int _nAtivas = 1;

    // Turbulência do ar e estado da pós-combustão.
    private double _turb, _turbAlvo, _burn;

    // Alvos vindos do jogo e os valores suavizados que realmente soam. Sem a
    // suavização, o esforço pulando entre quadros viraria um serrote audível.
    private double _esforcoAlvo, _calorAlvo, _abafarAlvo, _vivoAlvo;
    private double _esforco, _calor, _abafar, _vivo;
    private double _golpe;        // envelope do transiente de clique
    private double _crepita;      // ruído de motor morrendo

    public VozMotor(int lane, ReceitaMotor receita)
    {
        _r = receita;
        _tom = Banco.Tom(lane);
        _cor = 0.5 + 0.5 * _tom;
        // Ressonâncias de casco a cada ~1/atraso Hz. Dividir pela cor da pista
        // dá a cada nave um corpo de tamanho diferente.
        _cascoAtraso = Math.Clamp((int)(Onda.Taxa * _r.CascoMs * 0.001 / _cor), 1, Casco - 1);
        _rng = new Random(9001 + lane);
    }

    /// <summary>
    /// Estado da nave neste quadro. <paramref name="clique"/> é a borda de
    /// aperto — o mesmo evento que move o PWM — e vira um golpe no pistão.
    /// </summary>
    public void Definir(double esforco, double calor, bool travado, bool lento, bool ligado, bool clique)
    {
        _esforcoAlvo = ligado && !travado ? Math.Clamp(esforco, 0.0, 1.0) : 0.0;
        _calorAlvo = Math.Clamp(calor, 0.0, 1.0);
        // Tiro no teto de PWM: o motor não fica mais lento, fica ABAFADO.
        // Quem ouve entende que a nave está sendo segurada, não que desistiu.
        _abafarAlvo = lento ? 1.0 : 0.0;
        _vivoAlvo = ligado && !travado ? 1.0 : 0.0;
        if (clique)
            _golpe = 1.0;
        if (travado && _crepita <= 0.0 && _vivo > 0.25)
            _crepita = 1.0;
    }

    /// <summary>Uma amostra mono, de -1 a 1, já com o volume do motor aplicado.</summary>
    public double Proxima()
    {
        // O esforço sobe rápido (o aperto tem que soar imediato) e desce mais
        // devagar, que é como um volante de inércia se comporta.
        _esforco += (_esforcoAlvo - _esforco) * (_esforcoAlvo > _esforco ? 0.0016 : 0.0006);
        _calor += (_calorAlvo - _calor) * 0.0004;
        _abafar += (_abafarAlvo - _abafar) * 0.0008;
        _vivo += (_vivoAlvo - _vivo) * (_vivoAlvo > _vivo ? 0.0012 : 0.0025);

        if (_golpe > 0.0)
            _golpe *= GolpeDecai;

        double e = _esforco;

        // Pós-combustão: só acima de 62% de esforço. É a recompensa por
        // martelar rápido — o motor muda de estado em vez de só ficar mais
        // alto, e é daí que vem a emoção de estar no talo.
        _burn += (Math.Clamp((e - 0.62) / 0.38, 0.0, 1.0) - _burn) * 0.0009;

        if (_conta++ % PassoCoef == 0)
        {
            // Turbulência: um passeio aleatório lento no corte do ar.
            if (_conta % 512 == 0)
                _turbAlvo = _rng.NextDouble();
            _turb += (_turbAlvo - _turb) * 0.10;

            // O corte acompanha o esforço: é o que faz o motor "abrir". Q de
            // 0,7 não tem pico nenhum — ele só tira o topo, sem apitar. O tiro
            // fecha o corte: a nave segue andando, mas soa presa.
            double corte = (_r.CorteBase + _r.CorteGanho * e + _r.CorteBurn * _burn)
                           * (1.0 - 0.45 * _abafar) * _cor;
            _lp.Ajustar(corte, 0.7);
            _lpAr.Ajustar((_r.ArCorteBase + _r.ArCorteGanho * e) * (0.75 + 0.5 * _turb) * _cor, 0.7);

            // Formantes. Abrem um pouco com o esforço, como uma garganta, mas
            // continuam presos a frequências próprias — é essa independência
            // da afinação que cria o caráter. Q moderado de propósito: acima
            // de ~3 eles voltariam a apitar.
            // Balanço: as três frequências passeiam juntas, devagar. É o que
            // faz o timbre parecer uma garganta em movimento em vez de um
            // filtro parado. No propulsor a profundidade é zero.
            double bal = 1.0;
            if (_r.FmtBalancoProf > 0.0)
            {
                _fBalanco += _r.FmtBalancoHz / Onda.Taxa * PassoCoef;
                if (_fBalanco >= 1.0) _fBalanco -= Math.Floor(_fBalanco);
                bal += _r.FmtBalancoProf * (0.3 + 0.7 * e) * Tabela.Sen(_fBalanco);
            }
            _fmt1.Ajustar(_r.FmtHz[0] * _cor * bal * (1.0 + _r.FmtAbre[0] * e), _r.FmtQ[0], banda: true);
            _fmt2.Ajustar(_r.FmtHz[1] * _cor * bal * (1.0 + _r.FmtAbre[1] * e), _r.FmtQ[1], banda: true);
            _fmt3.Ajustar(_r.FmtHz[2] * _cor * bal * (1.0 + _r.FmtAbre[2] * e + 0.30 * _burn),
                          _r.FmtQ[2], banda: true);

            // Pesos das parciais: 1/h^p sobre a série da sub-oitava, com as
            // ímpares (o rosnado) pesando `growl`. Um Math.Pow por parcial por
            // amostra seria o item mais caro do jogo inteiro, e tanto o
            // expoente quanto o rosnado mudam devagar — recalcular junto com o
            // filtro é exato o bastante.
            double p = _r.PBase - _r.PGanho * e - 0.15 * _calor;
            double growl = _r.GrowlBase + _r.GrowlGanho * e;
            _somaPesos = 0.0;
            _nAtivas = 1;
            for (int n = 1; n <= NParciais; n++)
            {
                int h = (n + 1) / 2;                       // harmônico de f a que ela pertence
                double w = Math.Pow(h, -p) * (n % 2 == 1 ? growl : 1.0);
                _pesos[n] = w;
                _somaPesos += w;
                // Parar onde as parciais deixam de contar poupa metade do laço
                // quente quando o motor está em marcha lenta.
                if (w > 0.006)
                    _nAtivas = n;
            }
        }

        // Respiração: um LFO bem lento na afinação. É o que tira o som de
        // "parado" sem gerar o batimento rápido que o ouvido acha áspero.
        _fLfo += _r.RespiraHz / Onda.Taxa;
        if (_fLfo >= 1.0) _fLfo -= 1.0;
        double deriva = 1.0 + _r.RespiraProf * Tabela.Sen(_fLfo)
                            + 0.004 * _calor * Tabela.Sen(_fLfo * 23.0);

        // Uivo: o vibrato rápido e fundo das vozes de caça. Fica em zero no
        // propulsor, onde só a respiração lenta toca a afinação.
        if (_r.UivoProf > 0.0)
        {
            _fUivo += _r.UivoHz / Onda.Taxa;
            if (_fUivo >= 1.0) _fUivo -= 1.0;
            // Fundo quando o motor força, quase parado em marcha lenta — o
            // grito é do esforço, não do repouso.
            deriva += _r.UivoProf * (0.35 + 0.65 * e) * Tabela.Sen(_fUivo);
        }

        // O golpe empurra a afinação para cima e volta: o motor "engole" o
        // combustível a cada aperto. Isso faz o ritmo do martelar aparecer sem
        // nenhum estalo somado por cima.
        double f = (_r.FreqBase + _r.FreqGanho * e) * _tom * deriva * (1.0 + 0.022 * _golpe);
        double fb = f * 0.5;   // a série inteira é construída sobre a sub-oitava

        _faseSub += fb / Onda.Taxa;
        _faseGemea += f * 1.0018 / Onda.Taxa;
        if (_faseSub >= 1.0) _faseSub -= Math.Floor(_faseSub);
        if (_faseGemea >= 1.0) _faseGemea -= Math.Floor(_faseGemea);

        // Corpo aditivo sobre a sub-oitava. As parciais PARES são a série
        // harmônica normal do motor; as ÍMPARES caem no meio dela e são o
        // rosnado. Como todas pertencem à série de fb, elas fundem numa altura
        // só — engrossa sem criar aspereza. O rosnado cresce com o esforço:
        // é o motor "abrindo a garganta" quando o jogador força.
        double corpo = 0.0;
        for (int n = 1; n <= _nAtivas; n++)
        {
            if (fb * n > Teto)
                break;
            corpo += Tabela.Sen(_faseSub * n + Fases[n]) * _pesos[n];
        }
        // Normalizar pela soma dos pesos mantém o volume firme enquanto o
        // timbre muda: abrir o motor não pode significar só ficar mais alto.
        corpo /= Math.Max(0.8, _somaPesos);

        // Gêmea 0,18% acima: o batimento fica abaixo de 0,3 Hz — movimento,
        // não aspereza. Só as duas primeiras parciais, que basta.
        double gemea = (Tabela.Sen(_faseGemea) + Tabela.Sen(_faseGemea * 2.0) * 0.4) * 0.26;

        // Inarmonicidade: um oscilador numa razão NÃO inteira, que por isso
        // não cai em cima de nenhum harmônico. É o brilho metálico e "errado"
        // de máquina que não queima combustível. Dose pequena: acima de ~0,3
        // o motor vira sino desafinado.
        if (_r.FmIndice > 0.0)
        {
            _faseFm += f * _r.FmRazao / Onda.Taxa;
            if (_faseFm >= 1.0) _faseFm -= Math.Floor(_faseFm);
            corpo += Tabela.Sen(_faseFm) * _r.FmIndice;
        }

        // Ar turbulento: o corte do ruído passeia sozinho, então o sopro nunca
        // fica parado. Ruído estático soa como chiado de rádio; ruído que se
        // mexe soa como alguma coisa queimando lá dentro.
        double ar = _lpAr.Passar(_rng.NextDouble() * 2.0 - 1.0)
                    * (_r.ArBase + _r.ArGanho * e) * (1.0 + _r.ArBurn * _burn);

        double seco = (corpo + gemea) * _r.Corpo + ar;

        // FORMANTES. Três ressonâncias em frequências FIXAS, que não seguem a
        // afinação. É o que separa "sintetizador tocando uma nota" de "coisa
        // com corpo": conforme o motor sobe, os harmônicos atravessam os
        // formantes e o timbre se transforma sozinho. É o equivalente sintético
        // do que se consegue processando um bicho de verdade.
        double fmt = _fmt1.Passar(seco) * _r.FmtGanho[0]
                   + _fmt2.Passar(seco) * _r.FmtGanho[1]
                   + _fmt3.Passar(seco) * (_r.FmtGanho[2] + _r.FmtGanhoBurn * _burn);

        double bruto = seco * _r.Seco + fmt;

        // CASCO. Um atraso de 3 ms realimentado: ressonâncias fixas a cada
        // ~320 Hz que dão a impressão de metal em volta do propulsor. É o mesmo
        // truque do disparo, aplicado de forma contínua.
        double atras = _casco[(_cascoPos + Casco - _cascoAtraso) % Casco];
        _casco[_cascoPos] = bruto + atras * _r.CascoRealim;
        _cascoPos = (_cascoPos + 1) % Casco;
        bruto += atras * _r.CascoMix;

        // Saturação ANTES do filtro: ela dá liga entre as camadas, e os
        // harmônicos que inventa são aparados logo em seguida. Na
        // pós-combustão ela aperta mais — o motor rasga.
        double drive = _r.DriveBase + _r.DriveGanho * e + 0.25 * _calor + _r.DriveBurn * _burn;
        bruto = Math.Tanh(bruto * drive) / Math.Tanh(drive);

        double s = _lp.Passar(bruto);

        // O golpe também empurra o volume — é o "pump" que se sente no peito
        // quando o jogador acelera o martelar.
        s *= 1.0 + 0.26 * _golpe;

        // Motor morto (bomba ou superaquecimento): crepita e apaga.
        if (_crepita > 0.0)
        {
            _crepita = Math.Max(0.0, _crepita - Dt * 2.2);
            if (_rng.NextDouble() < 0.003)
                s += (_rng.NextDouble() * 2.0 - 1.0) * _crepita * 0.3;
        }

        // Marcha lenta: mesmo sem aperto nenhum o motor respira, senão a nave
        // parada na largada soa quebrada em vez de parada.
        return s * _vivo * (0.16 + 0.84 * e) * _r.Volume;
    }
}

public partial class MotorSom : AudioStreamPlayer
{
    private readonly int _lane;
    private VozMotor _voz;
    private readonly double _ganhoEsq, _ganhoDir;
    private AudioStreamGeneratorPlayback? _saida;

    public MotorSom(int lane, PerfilMotor perfil)
    {
        _lane = lane;
        _voz = new VozMotor(lane, ReceitaMotor.De(perfil));

        double pan = Banco.Pan(lane);
        _ganhoEsq = Math.Sqrt((1.0 - pan) * 0.5);
        _ganhoDir = Math.Sqrt((1.0 + pan) * 0.5);

        Stream = new AudioStreamGenerator
        {
            MixRate = Onda.Taxa,
            // 120 ms de folga: aguenta um quadro pesado sem estalar, e não
            // tanto a ponto de o som atrasar em relação ao aperto do dedo.
            BufferLength = 0.12f,
        };
        Bus = Mixagem.Motor;
    }

    public override void _Ready()
    {
        Play();
        _saida = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public void Definir(double esforco, double calor, bool travado, bool lento, bool ligado, bool clique) =>
        _voz.Definir(esforco, calor, travado, lento, ligado, clique);

    /// <summary>
    /// Troca a voz sem parar o motor. A voz nova nasce em silêncio e sobe
    /// junto com o esforço; trocar no meio da corrida dá um corte seco de
    /// menos de um quadro, o que é o preço de poder comparar as duas ao vivo.
    /// </summary>
    public void Trocar(PerfilMotor perfil) => _voz = new VozMotor(_lane, ReceitaMotor.De(perfil));

    public override void _Process(double delta)
    {
        if (_saida is null)
            return;
        int quadros = _saida.GetFramesAvailable();
        if (quadros <= 0)
            return;

        var buffer = new Vector2[quadros];
        for (int i = 0; i < quadros; i++)
        {
            float v = (float)_voz.Proxima();
            buffer[i] = new Vector2(v * (float)_ganhoEsq, v * (float)_ganhoDir);
        }
        _saida.PushBuffer(buffer);
    }

    /// <summary>
    /// O motor não vem do Banco — ele é sintetizado ao vivo. Esta varredura
    /// desenha o que ele faria numa corrida inteira, para poder ser exportada
    /// e ouvida: marcha lenta, aceleração com os golpes do martelar, calor
    /// subindo até o corte, e a volta com o teto reduzido pelo tiro.
    /// </summary>
    public static Onda Varredura(int lane, PerfilMotor perfil)
    {
        const double dur = 11.0;
        var o = new Onda(dur);
        var voz = new VozMotor(lane, ReceitaMotor.De(perfil));
        double proximoClique = 0.0;

        for (int i = 0; i < o.N; i++)
        {
            double t = Onda.T(i);
            double esforco;
            double calor = 0.0;
            bool travado = false, lento = false;

            if (t < 1.0) esforco = 0.0;                              // parada, respirando
            else if (t < 5.0) esforco = (t - 1.0) / 4.0;             // subindo até o teto
            else if (t < 7.0) { esforco = 1.0; calor = Math.Min(1.0, (t - 5.0) / 2.0); }
            else if (t < 8.2) { esforco = 0.0; calor = 0.75; travado = true; }   // superaqueceu
            else { esforco = 0.75; calor = 0.4; lento = true; }      // voltou, mas com tiro

            // Cliques na cadência que aquele esforço exigiria: 8 por segundo
            // no teto. É o ritmo real do dedo do jogador.
            bool clique = false;
            if (!travado && esforco > 0.02 && t >= proximoClique)
            {
                clique = true;
                proximoClique = t + 1.0 / (esforco * 8.0);
            }

            voz.Definir(esforco, calor, travado, lento, ligado: true, clique);
            o.X[i] = (float)voz.Proxima();
        }
        return o.Normalizar(0.85);
    }
}
