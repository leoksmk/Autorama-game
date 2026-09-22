// Estado de uma nave / pista — porte fiel de game/ship.py.
//
// Aqui mora a regra de ouro: o frame termina calculando UM número, Pwm, e a
// velocidade é consequência dele. Nada altera Speed diretamente; se um efeito
// quisesse mexer na velocidade sem passar pelo PWM, ele não existiria no
// carrinho físico.
//
// Ordem de resolução dentro de um frame:
//
//     1. envelhece os temporizadores
//     2. registra o clique do jogador, se ele for honrado
//     3. atualiza o calor conforme o esforço
//     4. calcula o teto de PWM e o PWM
//     5. persegue a velocidade correspondente com Accel / Decel
//     6. integra a posição e detecta a passagem por checkpoint

using System;
using System.Collections.Generic;

namespace OrbitalDerby.Core;

public enum ResultadoAtaque { Atingido, Bloqueado }

public sealed class Nave
{
    public int Lane { get; }
    public string Nome { get; }

    // Posição: ESTIMATIVA. Hoje vem só da integração; no hardware será
    // corrigida a cada sensor por SincronizarPosicao().
    public double T;
    public int Voltas;
    public double Speed;        // voltas por segundo
    public double Pwm;          // o que iria para o motor neste frame

    // Acelerador de martelar: frequência de clique estimada e há quanto tempo
    // aconteceu o último aperto.
    public double CadenciaHz;
    public double DesdeClique = 99.0;

    public double Calor;
    public double Superaquecimento;

    // Slot único, sem estoque.
    public Item? Slot;
    public readonly Roleta Roleta = new();

    public double Lento;        // teto x TiroMult (levou um tiro)
    public double Atordoado;    // PWM zerado (levou uma bomba)

    /// <summary>
    /// Quantas passagens por sensor o Escudo ainda aguenta. Zero é sem escudo.
    ///
    /// O escudo é o único efeito deste jogo que NÃO é contado em segundos, e
    /// isso é de propósito: um temporizador não tem como existir no autorama
    /// físico sem um relógio paralelo ao da pista, enquanto "cai no próximo
    /// sensor" é exatamente o tipo de evento que o hardware já gera. Também é
    /// o que o jogador lê na pista, sem olhar para o HUD.
    /// </summary>
    public int EscudoTrechos;

    /// <summary>Atalho: o escudo está de pé? Escrever true rearma pelo prazo cheio.</summary>
    public bool Escudo
    {
        get => EscudoTrechos > 0;
        set => EscudoTrechos = value ? Cfg.EscudoTrechos : 0;
    }

    /// <summary>Último trecho antes de cair. Só a interface usa, para avisar.</summary>
    public bool EscudoNoUltimoTrecho => EscudoTrechos == 1;

    // Passagem por sensor, válida só no frame em que acontece. Consumida pela
    // Corrida. Os sensores físicos preencherão este mesmo campo.
    public int? CheckpointCruzado;

    /// <summary>O escudo caiu de velho neste frame (não foi gasto num ataque).</summary>
    public bool EscudoVenceu;

    // Feedback para a interface (não afeta a simulação).
    public string Aviso = "";
    public double AvisoTempo;
    public double Flash;
    public bool Terminou;
    public double TempoFinal;

    public Nave(int lane, string nome, double tInicial = 0.0)
    {
        Lane = lane;
        Nome = nome;
        T = Pista.Mod1(tInicial);
    }

    /// <summary>Voltas acumuladas + fração da volta atual. Serve para ordenar.</summary>
    public double Progresso => Voltas + T;

    /// <summary>
    /// Cliques ignorados. Girar a caixa NÃO entra aqui: a nave segue andando
    /// enquanto a roleta roda.
    /// </summary>
    public bool Travado => Superaquecimento > 0.0 || Atordoado > 0.0;

    /// <summary>Ritmo de clique normalizado, 0 a 1. É o que vira PWM.</summary>
    public double Esforco => Math.Min(1.0, CadenciaHz / Cfg.CadenciaPlenaHz);

    public double TetoPwm()
    {
        double teto = Cfg.PwmBase;
        if (Lento > 0.0)
            teto *= Cfg.TiroMult;
        return Math.Min(1.0, teto);
    }

    /// <summary>Rótulo dominante para a telemetria, em ordem de prioridade.</summary>
    public string EffectTag()
    {
        if (Terminou) return "chegou";
        if (Superaquecimento > 0.0) return "superaquecido";
        if (Atordoado > 0.0) return "parado pela bomba";
        var partes = new List<string>(2);
        if (Lento > 0.0) partes.Add("teto reduzido");
        if (Escudo) partes.Add(EscudoTrechos == 1 ? "escudo (1 trecho)" : "escudo");
        return partes.Count > 0 ? string.Join(" + ", partes) : "livre";
    }

    /// <summary>
    /// Aplica um ataque do adversário. O Escudo é gasto no primeiro ataque que
    /// chegar, qualquer que seja ele.
    /// </summary>
    public ResultadoAtaque Receber(Item ataque)
    {
        if (Escudo)
        {
            EscudoTrechos = 0;
            Mensagem("Escudo bloqueou");
            Flash = 0.30;
            return ResultadoAtaque.Bloqueado;
        }

        if (ataque == Item.Bomba)
            Atordoado = Math.Max(Atordoado, Cfg.BombaDuracao);
        else if (ataque == Item.Tiro)
            Lento = Math.Max(Lento, Cfg.TiroDuracao);

        Flash = 0.35;
        return ResultadoAtaque.Atingido;
    }

    public void Mensagem(string texto, double duracao = 1.6)
    {
        Aviso = texto;
        AvisoTempo = duracao;
    }

    /// <summary>
    /// Corrige a estimativa de posição a partir de um sensor real. A integração
    /// continua entre um sensor e outro; o sensor só reancora. A passagem
    /// registrada aqui abre a caixa pelo mesmo caminho que a integração abre.
    /// </summary>
    public void SincronizarPosicao(double t)
    {
        double anterior = T;
        double novo = Pista.Mod1(t);
        if (anterior > 0.9 && novo < 0.1)
            Voltas++;
        int? cruzou = Pista.CruzouCheckpoint(anterior, novo);
        if (cruzou.HasValue)
            RegistrarPassagem(cruzou.Value);
        T = novo;
    }

    /// <summary>
    /// Passou por um sensor. Ponto único: a integração e o sensor físico
    /// chegam aqui pelo mesmo caminho, então o que vence com a passagem — hoje
    /// o Escudo — vence igual nos dois, sem regra duplicada.
    /// </summary>
    private void RegistrarPassagem(int checkpoint)
    {
        CheckpointCruzado = checkpoint;
        if (EscudoTrechos > 0 && --EscudoTrechos == 0)
        {
            EscudoVenceu = true;
            Mensagem("Escudo caiu", 1.2);
        }
    }

    /// <summary>
    /// <paramref name="clique"/> é a BORDA de subida do acelerador, não o
    /// estado dele. Segurar o botão não faz nada.
    /// </summary>
    public void Atualizar(double dt, bool clique, bool correndo)
    {
        // 0. os eventos de sensor valem um frame só
        CheckpointCruzado = null;
        EscudoVenceu = false;

        // 1. temporizadores
        Lento = Math.Max(0.0, Lento - dt);
        Atordoado = Math.Max(0.0, Atordoado - dt);
        AvisoTempo = Math.Max(0.0, AvisoTempo - dt);
        Flash = Math.Max(0.0, Flash - dt);
        if (AvisoTempo == 0.0)
            Aviso = "";

        bool emSuperaquecimento = Superaquecimento > 0.0;
        if (emSuperaquecimento)
            Superaquecimento = Math.Max(0.0, Superaquecimento - dt);

        // 2. cadência de clique
        DesdeClique += dt;
        bool vale = correndo && !Travado && !Terminou;

        if (emSuperaquecimento || Atordoado > 0.0 || !vale)
        {
            // Motor cortado ou nave travada: os apertos se perdem e o embalo
            // some junto. Voltar exige recomeçar o ritmo.
            CadenciaHz = 0.0;
            DesdeClique = 99.0;
        }
        else if (clique)
        {
            if (DesdeClique > Cfg.CliqueTimeout)
            {
                // Primeiro aperto depois de uma pausa: empurrão fixo.
                CadenciaHz = Cfg.CadenciaInicialHz;
            }
            else
            {
                double intervalo = Math.Max(Cfg.CliqueIntervaloMin, DesdeClique);
                double nova = 1.0 / intervalo;
                // Média entre os últimos intervalos: absorve um clique
                // irregular sem transformar o PWM num serrote.
                CadenciaHz += (nova - CadenciaHz) * Cfg.CadenciaSuavizacao;
            }
            DesdeClique = 0.0;
        }
        else
        {
            // Sem clique novo, a frequência não pode ser maior do que o tempo
            // já esperado permite: é assim que soltar o botão desacelera.
            double teto = 1.0 / Math.Max(1e-6, DesdeClique);
            if (CadenciaHz > teto)
                CadenciaHz = teto;
            // Passou do limite: parou de clicar, o motor corta de vez. Sem isso
            // a queda 1/t deixaria um duty residual para sempre.
            if (DesdeClique > Cfg.CliqueTimeout || CadenciaHz < 0.05)
                CadenciaHz = 0.0;
        }

        double esforco = vale ? Esforco : 0.0;

        // 3. calor: sobe proporcional ao esforço acima do limiar; abaixo, esfria
        if (emSuperaquecimento)
        {
            Calor = Math.Max(0.0, Calor - Cfg.CalorDescida * dt);
        }
        else if (esforco > Cfg.CalorLimiar)
        {
            double excesso = (esforco - Cfg.CalorLimiar) / (1.0 - Cfg.CalorLimiar);
            Calor = Math.Min(1.0, Calor + Cfg.CalorSubida * excesso * dt);
            if (Calor >= 1.0)
            {
                // Superaquecimento: corta o PWM e devolve o calor já parcial.
                Superaquecimento = Cfg.SuperaquecimentoDuracao;
                Calor = Cfg.SuperaquecimentoCalorResidual;
                CadenciaHz = 0.0;
                Mensagem("Superaquecimento", 1.6);
                esforco = 0.0;
            }
        }
        else
        {
            Calor = Math.Max(0.0, Calor - Cfg.CalorDescida * dt);
        }

        // 4. PWM. O único ponto do jogo que decide o que vai ao motor.
        Pwm = TetoPwm() * esforco;

        // 5. velocidade persegue o teto correspondente ao PWM
        double alvo = Pwm * Cfg.VelPorPwm;
        if (Speed < alvo)
            Speed = Math.Min(alvo, Speed + Cfg.Accel * dt);
        else if (Speed > alvo)
            Speed = Math.Max(alvo, Speed - Cfg.Decel * dt);

        // 6. posição (estimativa por integração) e passagem por checkpoint,
        // detectada como o sensor detectaria: pela travessia, não pela proximidade.
        if (correndo && !Terminou)
        {
            double anterior = T;
            T = Pista.Mod1(T + Speed * dt);
            int? cruzou = Pista.CruzouCheckpoint(anterior, T);
            if (cruzou.HasValue)
                RegistrarPassagem(cruzou.Value);
            if (anterior > 0.9 && T < 0.1)
                Voltas++;
        }
    }
}
