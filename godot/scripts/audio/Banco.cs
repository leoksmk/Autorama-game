// As receitas de cada som do jogo.
//
// Uma função por som, devolvendo uma Onda pronta. Nenhuma delas conhece o
// estado da partida — quem toca o quê é o Som.
//
// Três princípios guiam as receitas, todos herdados da escola analógica de som
// de nave (a técnica, não os sons registrados de ninguém):
//
//   1. GRAVE PRIMEIRO. Todo impacto tem um seno abaixo de 90 Hz descendo. É o
//      que faz a caixa de som empurrar o ar e o que sobrevive ao barulho de
//      uma sala cheia.
//   2. NADA DE TOM PURO. Tudo passa por filtro ressonante e saturação. O que
//      soa "máquina" são os harmônicos que a distorção inventa.
//   3. ATRASO CURTO É METAL. Um eco de 2 a 6 ms não soa como eco: soa como
//      corpo metálico ressoando. É com isso que o disparo vira pancada em cabo
//      de aço esticado, sem sample nenhum.
//
// As duas pistas têm timbres separados de propósito: o ÍON é mais agudo e
// elétrico, o ÍGNIS mais grave e rouco. Com isso, e com o estéreo aberto,
// quem está de costas para a tela ainda sabe qual nave levou a bomba.

using System;
using OrbitalDerby.Core;

namespace OrbitalDerby.Audio;

public static class Banco
{
    /// <summary>Deslocamento de timbre por pista. Não é regra: é identidade sonora.</summary>
    public static double Tom(int lane) => lane == 0 ? 1.12 : 0.88;

    /// <summary>Onde cada pista fica no estéreo: ÍON à esquerda, ÍGNIS à direita.</summary>
    public static double Pan(int lane) => lane == 0 ? -0.55 : 0.55;

    /// <summary>Interpolação exponencial de <paramref name="a"/> a <paramref name="b"/> em p ∈ [0,1].</summary>
    private static double Varre(double a, double b, double p) =>
        a * Math.Pow(b / a, Math.Clamp(p, 0.0, 1.0));

    private static double Sino(double p) => Math.Sin(Math.Clamp(p, 0.0, 1.0) * Math.PI);

    // -- itens: disparo e impacto -------------------------------------------

    /// <summary>
    /// Tiro saindo. Sweep descendente curto e um eco de 5 ms que ressoa: o
    /// resultado é o estalo tensionado de um cabo de aço, não um "bip".
    /// </summary>
    public static Onda Disparo(double tom = 1.0)
    {
        const double dur = 0.34;
        var corpo = Onda.Serra(dur, t => Varre(1500 * tom, 190 * tom, t / 0.13))
            .Lp(p => Varre(5200, 700, p), q: 4.2)
            .Ad(0.0015, 0.30, 3.4);
        var faisca = Onda.Ruido(dur, 7)
            .Bp(p => Varre(4200, 1100, p), q: 3.0)
            .Ad(0.0008, 0.09, 4.0);
        return Onda.Mixar(dur, (corpo, 1.0), (faisca, 0.35))
            .Eco(0.0055 / tom, 0.70, 0.85)
            .Saturar(2.4)
            .Normalizar(0.85);
    }

    /// <summary>Bomba saindo do tubo: um baque e o assobio da queda.</summary>
    public static Onda BombaLancada(double tom = 1.0)
    {
        const double dur = 0.62;
        var baque = Onda.Seno(dur, t => Varre(150 * tom, 44 * tom, t / 0.09))
            .Ad(0.001, 0.20, 2.6);
        var tubo = Onda.Ruido(dur, 11)
            .Bp(p => Varre(2400 * tom, 520 * tom, p), q: 2.4)
            .Env(p => Sino(p) * 0.9);
        return Onda.Mixar(dur, (baque, 1.0), (tubo, 0.55))
            .Saturar(1.8)
            .Normalizar(0.8);
    }

    /// <summary>
    /// Estouro da bomba. O item mais pesado do jogo e o único que tem direito
    /// a cauda longa: sub descendo, corpo de ruído fechando o filtro e um
    /// estalo bem no ataque para dar o "agora".
    /// </summary>
    public static Onda Explosao()
    {
        const double dur = 1.35;
        var sub = Onda.Seno(dur, t => Varre(78, 26, t / 0.55))
            .Ad(0.004, 0.95, 2.0);
        var corpo = Onda.Ruido(dur, 3)
            .Lp(p => Varre(3600, 150, p), q: 1.3)
            .Ad(0.003, 0.85, 1.7);
        var estalo = Onda.Ruido(dur, 5)
            .Hp(2600)
            .Ad(0.0004, 0.07, 4.0);
        return Onda.Mixar(dur, (sub, 1.0), (corpo, 0.85), (estalo, 0.5))
            .Saturar(2.8)
            .Espaco(0.7, 0.2)
            .Normalizar(0.95);
    }

    /// <summary>Tiro acertando: elétrico e seco. Dói menos que a bomba, e tem que soar assim.</summary>
    public static Onda TiroImpacto(double tom = 1.0)
    {
        const double dur = 0.62;
        var descarga = Onda.Ruido(dur, 13)
            .Bp(p => Varre(2600 * tom, 900 * tom, p), q: 2.6)
            .Ad(0.001, 0.30, 2.4);
        var zumbido = Onda.Serra(dur, t => Varre(820 * tom, 170 * tom, t / 0.12))
            .Lp(p => Varre(3000, 600, p), q: 3.0)
            .Ad(0.001, 0.26, 3.0);
        var sub = Onda.Seno(dur, t => Varre(96, 48, t / 0.2)).Ad(0.002, 0.28, 2.2);
        return Onda.Mixar(dur, (descarga, 0.7), (zumbido, 1.0), (sub, 0.6))
            .Eco(0.004, 0.55, 0.6)
            .Saturar(2.2)
            .Normalizar(0.88);
    }

    // -- escudo --------------------------------------------------------------

    /// <summary>Escudo subindo: varredura ascendente e um zumbido que fica de pé.</summary>
    public static Onda EscudoLigado(double tom = 1.0)
    {
        const double dur = 0.85;
        var sobe = Onda.Seno(dur, t => Varre(170 * tom, 760 * tom, t / 0.38))
            .Env(p => Math.Min(1.0, p * 6.0) * Math.Pow(1.0 - Math.Clamp((p - 0.45) / 0.55, 0, 1), 2.0));
        var quinta = Onda.Triangulo(dur, t => Varre(255 * tom, 1140 * tom, t / 0.38))
            .Env(p => Math.Min(1.0, p * 6.0) * Math.Pow(1.0 - Math.Clamp((p - 0.45) / 0.55, 0, 1), 2.0));
        var brilho = Onda.Ruido(dur, 17)
            .Bp(p => Varre(900, 5200, Math.Min(1.0, p * 2.2)), q: 2.8)
            .Env(p => Sino(Math.Min(1.0, p * 1.6)) * 0.8);
        return Onda.Mixar(dur, (sobe, 1.0), (quinta, 0.5), (brilho, 0.4))
            .Espaco(0.6, 0.28)
            .Normalizar(0.72);
    }

    /// <summary>
    /// Escudo aparando um ataque. Ruído curto passando por um eco afinado em
    /// 520 Hz: o mesmo princípio de uma corda pinçada, que aqui soa como chapa
    /// de metal batida.
    /// </summary>
    public static Onda EscudoBloqueou(double tom = 1.0)
    {
        const double dur = 1.0;
        double f = 520 * tom;
        var chapa = new Onda(dur);
        chapa.Somar(Onda.Ruido(0.018, 23).Bordas(0.002), 1.0);
        chapa.Eco(1.0 / f, 0.93, 1.0)
             .Lp(p => Varre(6000, 1400, p), q: 1.0)
             .Env(p => Math.Pow(1.0 - p, 1.6));
        var golpe = Onda.Ruido(dur, 29)
            .Bp(1500 * tom, q: 1.8)
            .Ad(0.001, 0.16, 3.0);
        var sub = Onda.Seno(dur, t => Varre(110, 60, t / 0.15)).Ad(0.002, 0.2, 2.4);
        return Onda.Mixar(dur, (chapa, 1.0), (golpe, 0.5), (sub, 0.45))
            .Saturar(1.7)
            .Espaco(0.55, 0.22)
            .Normalizar(0.85);
    }

    /// <summary>
    /// Ataque que passou longe. Sobe e desce em Doppler e o filtro fecha na
    /// saída: o ouvido entende "passou reto" sem precisar de legenda.
    /// </summary>
    public static Onda AtaqueErrou(double tom = 1.0)
    {
        const double dur = 0.75;
        var passagem = Onda.Serra(dur, t =>
        {
            double p = t / dur;
            return 620 * tom * (1.0 + 0.42 * Math.Cos(p * Math.PI));
        })
            .Lp(p => Varre(4200, 380, p), q: 2.2)
            .Env(p => Sino(p));
        var ar = Onda.Ruido(dur, 31)
            .Bp(p => Varre(2600, 600, p), q: 1.6)
            .Env(p => Sino(p) * 0.7);
        return Onda.Mixar(dur, (passagem, 1.0), (ar, 0.45))
            .Saturar(1.5)
            .Normalizar(0.55);
    }

    // -- caixa de item -------------------------------------------------------

    /// <summary>Janela da caixa abrindo: duas notas que sobem, um convite curto.</summary>
    public static Onda CaixaAbriu(double tom = 1.0)
    {
        const double dur = 0.26;
        var a = Onda.Triangulo(dur, 700 * tom).Ad(0.002, 0.07, 2.5);
        var b = Onda.Triangulo(dur, 1050 * tom).Ad(0.002, 0.09, 2.5);
        return Onda.Mixar(dur, (a, 0.8))
            .Somar(b, 0.8, 0.075)
            .Lp(5000)
            .Normalizar(0.5);
    }

    /// <summary>
    /// Um estalo da roleta. Disparado a cada troca de face — como a face troca
    /// cada vez mais devagar, o ritmo desacelera sozinho e o ouvido segue o
    /// sorteio travando.
    /// </summary>
    public static Onda RoletaTique(double tom = 1.0)
    {
        const double dur = 0.06;
        var clique = Onda.Ruido(dur, 37).Hp(1800).Ad(0.0004, 0.028, 3.5);
        var corpo = Onda.Seno(dur, 940 * tom).Ad(0.0006, 0.02, 3.0);
        return Onda.Mixar(dur, (clique, 0.7), (corpo, 0.7)).Normalizar(0.42);
    }

    /// <summary>O prêmio aparecendo. Cada item tem seu acorde; "Nada" tem o seu desgosto.</summary>
    public static Onda Revelacao(Item item, double tom = 1.0)
    {
        if (item == Item.Nada)
            return CaixaVazia(tom);

        const double dur = 0.7;
        (double f1, double f2, double drive) = item switch
        {
            Item.Tiro => (392.0, 587.0, 2.6),    // quinta tensa, saturada
            Item.Bomba => (196.0, 294.0, 3.2),   // uma oitava abaixo: peso
            _ => (523.0, 784.0, 1.4),            // escudo: limpo e aberto
        };
        var a = Onda.Serra(dur, f1 * tom).Ad(0.004, 0.55, 2.2);
        var b = Onda.Serra(dur, f2 * tom).Ad(0.004, 0.5, 2.4);
        var brilho = Onda.Ruido(dur, 41).Bp(p => Varre(3000, 1200, p), q: 3.0).Ad(0.002, 0.12, 3.0);
        return Onda.Mixar(dur, (a, 0.8), (b, 0.6), (brilho, 0.3))
            .Lp(p => Varre(4800, 1600, p), q: 1.2)
            .Saturar(drive)
            .Espaco(0.5, 0.2)
            .Normalizar(0.66);
    }

    /// <summary>Caixa vazia: duas notas que descem, abafadas. Frustração em 0,4 s.</summary>
    public static Onda CaixaVazia(double tom = 1.0)
    {
        const double dur = 0.5;
        var a = Onda.Quadrada(dur, 420 * tom, 0.35).Ad(0.004, 0.13, 2.5);
        var b = Onda.Quadrada(dur, 300 * tom, 0.35).Ad(0.004, 0.2, 2.5);
        return Onda.Mixar(dur, (a, 0.7))
            .Somar(b, 0.7, 0.14)
            .Lp(1500, q: 1.0)
            .Normalizar(0.42);
    }

    /// <summary>Passagem por sensor de checkpoint. Curtíssimo, quase um toque.</summary>
    public static Onda Checkpoint(double tom = 1.0)
    {
        const double dur = 0.11;
        var pip = Onda.Seno(dur, 1380 * tom).Ad(0.001, 0.05, 3.0);
        var ar = Onda.Ruido(dur, 43).Bp(3200, q: 2.5).Ad(0.0006, 0.03, 3.0);
        return Onda.Mixar(dur, (pip, 0.7), (ar, 0.3)).Normalizar(0.3);
    }

    // -- largada e estados ---------------------------------------------------

    /// <summary>Bipe da contagem. Grave, de buzzer — parente do semáforo de grid.</summary>
    public static Onda ContagemBipe()
    {
        const double dur = 0.34;
        return Onda.Quadrada(dur, 230, 0.45)
            .Lp(1300, q: 1.4)
            .Ad(0.005, 0.26, 2.2)
            .Saturar(1.9)
            .Normalizar(0.6);
    }

    /// <summary>Bipe do "vai": uma oitava acima e sustentado.</summary>
    public static Onda ContagemVerde()
    {
        const double dur = 0.8;
        var nota = Onda.Quadrada(dur, 460, 0.45)
            .Lp(3000, q: 1.4)
            .Env(p => p < 0.02 ? p / 0.02 : Math.Pow(1.0 - Math.Clamp((p - 0.35) / 0.65, 0, 1), 1.8));
        return nota.Saturar(2.0).Espaco(0.4, 0.15).Normalizar(0.7);
    }

    /// <summary>A arrancada: sub subindo e uma lufada de ar que abre o filtro.</summary>
    public static Onda Largada()
    {
        const double dur = 1.3;
        var sub = Onda.Seno(dur, t => Varre(38, 105, t / 0.6)).Ad(0.02, 1.0, 1.6);
        var lufada = Onda.Ruido(dur, 47)
            .Bp(p => Varre(180, 4200, Math.Min(1.0, p * 1.4)), q: 1.2)
            .Env(p => Sino(Math.Min(1.0, p * 1.25)));
        var motor = Onda.Serra(dur, t => Varre(55, 165, t / 0.7))
            .Lp(p => Varre(400, 2600, p), q: 2.6)
            .Env(p => Math.Min(1.0, p * 3.0) * (1.0 - Math.Clamp((p - 0.5) / 0.5, 0, 1)));
        return Onda.Mixar(dur, (sub, 1.0), (lufada, 0.5), (motor, 0.7))
            .Saturar(2.2)
            .Espaco(0.6, 0.2)
            .Normalizar(0.85);
    }

    /// <summary>Volta completada: dois pips claros, sem roubar a cena.</summary>
    public static Onda Volta(double tom = 1.0)
    {
        const double dur = 0.3;
        var a = Onda.Seno(dur, 880 * tom).Ad(0.002, 0.08, 2.5);
        var b = Onda.Seno(dur, 1320 * tom).Ad(0.002, 0.12, 2.5);
        return Onda.Mixar(dur, (a, 0.7)).Somar(b, 0.7, 0.085).Normalizar(0.45);
    }

    /// <summary>Última volta: um sino trêmulo. É o aviso de que agora vale tudo.</summary>
    public static Onda UltimaVolta()
    {
        const double dur = 1.6;
        var sino = Onda.Seno(dur, t => 330 * (1.0 + 0.012 * Math.Sin(t * Math.Tau * 5.5)))
            .Ad(0.004, 1.4, 1.8);
        var alto = Onda.Seno(dur, t => 495 * (1.0 + 0.012 * Math.Sin(t * Math.Tau * 5.5)))
            .Ad(0.004, 1.1, 2.0);
        var batida = Onda.Seno(dur, t => Varre(140, 60, t / 0.2)).Ad(0.002, 0.3, 2.0);
        return Onda.Mixar(dur, (sino, 0.8), (alto, 0.45), (batida, 0.6))
            .Espaco(0.8, 0.3)
            .Normalizar(0.62);
    }

    /// <summary>Ultrapassagem: uma lufada que cruza o estéreo.</summary>
    public static Onda Ultrapassagem()
    {
        const double dur = 0.55;
        return Onda.Ruido(dur, 53)
            .Bp(p => Varre(400, 3600, p), q: 1.5)
            .Env(p => Sino(p))
            .Saturar(1.6)
            .Normalizar(0.5);
    }

    /// <summary>Vitória: três notas subindo, com corpo saturado e sala grande.</summary>
    /// <summary>
    /// Um acorde: várias notas na mesma voz, com o filtro fechando ao longo
    /// do tempo. Tocar as notas JUNTAS, e não em sequência, é o que separa um
    /// acorde de uma escadinha de notas soltas.
    /// </summary>
    private static Onda Acorde(double dur, double[] notas, double brilhoIni, double brilhoFim,
                               double ataque, double queda, double curva = 1.6)
    {
        var m = new Onda(dur);
        foreach (double f in notas)
        {
            // Curva baixa segura o corpo do acorde por mais tempo antes de
            // cair; é o que dá sustentação a um acorde que precisa preencher
            // a tela de resultado inteira.
            var v = Onda.Serra(dur, f)
                .Lp(p => Varre(brilhoIni, brilhoFim, p), q: 1.0)
                .Ad(ataque, queda, curva);
            m.Somar(v, 1.0 / notas.Length);
        }
        return m;
    }

    /// <summary>
    /// Vitória. Montada como um "stinger" de jogo, que é uma forma com quatro
    /// partes e não uma melodia:
    ///
    ///   1. IMPACTO. Um golpe grave no instante zero, marcando o momento. Sem
    ///      ele o som começa sem autoridade, e era esse o problema da versão
    ///      anterior — três serras em sequência, que soavam como MIDI barato.
    ///   2. DOMINANTE. Um acorde de sol, curto e tenso: a pergunta.
    ///   3. TÔNICA. O acorde de dó entrando em cima, largo e sustentado, com
    ///      graves e agudos juntos: a resposta. Essa resolução (V → I) é o que
    ///      o ouvido lê como "conquistado", e nenhuma nota solta consegue.
    ///   4. CAUDA. Brilho que decai e sala grande, para o som não terminar
    ///      seco no meio da comemoração.
    /// </summary>
    public static Onda Vitoria()
    {
        const double dur = 2.9;
        var m = new Onda(dur);

        var golpe = Onda.Seno(dur, t => Varre(98, 40, t / 0.42)).Ad(0.003, 0.75, 1.9);
        var pancada = Onda.Ruido(dur, 91)
            .Lp(p => Varre(2400, 260, p), q: 1.0)
            .Ad(0.002, 0.24, 2.6);
        m.Somar(golpe, 0.95).Somar(pancada, 0.42);

        // V: sol, com a sétima, pedindo resolução.
        m.Somar(Acorde(0.60, new[] { 196.0, 294.0, 392.0, 494.0 }, 2600, 1200, 0.008, 0.5), 0.60);
        // I: dó em três oitavas, sustentado até o fim.
        m.Somar(Acorde(dur - 0.38, new[] { 131.0, 196.0, 262.0, 392.0, 523.0, 659.0 },
                       3200, 850, 0.014, 2.5, curva: 1.15), 0.92, 0.38);

        var brilho = Onda.Ruido(dur, 93)
            .Bp(p => Varre(2800, 6200, Math.Min(1.0, p * 2.0)), q: 2.0)
            .Env(p => Sino(Math.Min(1.0, p * 1.9)) * 0.55);
        m.Somar(brilho, 0.20, 0.38);

        return m.Saturar(1.9).Espaco(0.95, 0.34).Normalizar(0.92);
    }

    /// <summary>Derrota: o mesmo gesto ao contrário, com o filtro fechando.</summary>
    public static Onda Derrota()
    {
        const double dur = 2.6;
        var m = new Onda(dur);

        // Baque abafado: o peso sem a comemoração.
        var baque = Onda.Seno(dur, t => Varre(72, 32, t / 0.6)).Ad(0.012, 0.95, 1.7);
        m.Somar(baque, 0.85);

        // Quintas e oitavas descendo, SEM a terça. Isso não é economia: as
        // duas faixas tocam ao mesmo tempo, uma em cada lado da cabine, e uma
        // terça menor aqui brigaria com o dó maior da vitória. Sem terça, a
        // queda soa derrotada e ainda assim encaixa no acorde do vencedor.
        m.Somar(Acorde(1.3, new[] { 392.0, 523.0 }, 1600, 480, 0.02, 1.05), 0.55);
        m.Somar(Acorde(1.5, new[] { 294.0, 392.0 }, 1200, 380, 0.03, 1.25), 0.55, 0.42);
        m.Somar(Acorde(dur - 0.88, new[] { 131.0, 196.0, 262.0 }, 850, 240, 0.04, 1.7, curva: 1.2),
                0.72, 0.88);

        // Ar escapando: a nave desligando.
        var vapor = Onda.Ruido(dur, 97)
            .Lp(p => Varre(1500, 280, p), q: 0.8)
            .Env(p => Math.Pow(1.0 - p, 2.0) * 0.6);
        m.Somar(vapor, 0.3);

        return m.Saturar(1.4).Espaco(0.9, 0.3).Normalizar(0.62);
    }

    // -- motor em sofrimento -------------------------------------------------

    /// <summary>
    /// Superaquecimento: o motor engasga. Tom despencando com saturação pesada,
    /// vapor escapando e um estalo de metal que trinca.
    /// </summary>
    public static Onda Superaquecimento(double tom = 1.0)
    {
        const double dur = 1.5;
        var tosse = Onda.Serra(dur, t => Varre(240 * tom, 38 * tom, t / 0.4))
            .Lp(p => Varre(2400, 260, p), q: 3.4)
            .Ad(0.004, 0.55, 2.0);
        var vapor = Onda.Ruido(dur, 59)
            .Bp(p => Varre(3400, 1100, p), q: 1.4)
            .Env(p => Math.Min(1.0, p * 14.0) * Math.Pow(1.0 - p, 1.5));
        var trinco = Onda.Ruido(dur, 61).Hp(2200).Ad(0.0006, 0.05, 4.0);
        return Onda.Mixar(dur, (tosse, 1.0), (vapor, 0.5), (trinco, 0.35))
            .Saturar(3.0)
            .Normalizar(0.8);
    }

    /// <summary>
    /// Alarme de calor, em loop enquanto a nave estiver no vermelho. Dois
    /// bipes e um silêncio: é o padrão de alarme que o ouvido não consegue
    /// ignorar, e ele para no instante em que o jogador alivia o ritmo.
    /// </summary>
    public static Onda AlarmeCalor(double tom = 1.0)
    {
        const double dur = 0.66;
        var m = new Onda(dur);
        for (int i = 0; i < 2; i++)
        {
            var bipe = Onda.Quadrada(0.1, 1180 * tom, 0.4)
                .Lp(4000, q: 1.2)
                .Ad(0.004, 0.07, 2.0);
            m.Somar(bipe, 0.75, i * 0.16);
        }
        return m.Normalizar(0.42);
    }

    // -- interface -----------------------------------------------------------

    /// <summary>Clique de interface: trocar fonte de controle, ligar carrinhos.</summary>
    public static Onda Clique()
    {
        const double dur = 0.08;
        var c = Onda.Ruido(dur, 67).Bp(2200, q: 2.0).Ad(0.0006, 0.035, 3.0);
        var t = Onda.Seno(dur, 660).Ad(0.001, 0.03, 3.0);
        return Onda.Mixar(dur, (c, 0.5), (t, 0.6)).Normalizar(0.3);
    }

    // -- camadas em loop -----------------------------------------------------
    //
    // Loops perfeitos: as parciais são calculadas por SenoEmLoop, que ajusta a
    // frequência para caber um número inteiro de ciclos no buffer. Sem isso
    // haveria um estalo audível a cada volta.

    /// <summary>
    /// O casco. Duas fundamentais desafinadas em 0,3 Hz produzem um batimento
    /// lento — a respiração que impede o drone de soar sintético e parado.
    /// </summary>
    public static Onda Ambiente()
    {
        const double dur = 8.0;
        var m = new Onda(dur);
        m.Somar(Onda.SenoEmLoop(dur, 55.0), 0.5);
        m.Somar(Onda.SenoEmLoop(dur, 55.3), 0.45);
        m.Somar(Onda.SenoEmLoop(dur, 82.5), 0.22);
        m.Somar(Onda.SenoEmLoop(dur, 110.0, 0.25), 0.16);
        m.Somar(Onda.SenoEmLoop(dur, 164.8), 0.07);
        var vento = Onda.Ruido(dur, 71).Lp(340, q: 0.8);
        m.Somar(vento, 0.16);
        return m.Saturar(1.3).Normalizar(0.5);
    }

    /// <summary>
    /// A camada da corrida: pulso grave a 112 bpm com um chiado nos
    /// contratempos. Fica embaixo de tudo e dá o andamento sem competir com os
    /// motores, que ocupam a mesma faixa.
    /// </summary>
    public static Onda PulsoCorrida()
    {
        const double compasso = 4.0 * 60.0 / 112.0;   // 4 tempos a 112 bpm
        double passo = compasso / 8.0;
        var m = new Onda(compasso);
        for (int i = 0; i < 8; i++)
        {
            if (i % 2 == 0)
            {
                var golpe = Onda.Seno(0.4, t => Varre(96, 44, t / 0.09)).Ad(0.002, 0.26, 2.2);
                m.Somar(golpe, i % 4 == 0 ? 0.9 : 0.55, i * passo);
            }
            else
            {
                var chiado = Onda.Ruido(0.12, 73 + i).Hp(4200).Ad(0.0008, 0.05, 3.2);
                m.Somar(chiado, 0.18, i * passo);
            }
        }
        m.Somar(Onda.SenoEmLoop(compasso, 55.0), 0.3);
        m.Somar(Onda.SenoEmLoop(compasso, 110.0), 0.1);
        return m.Saturar(1.5).Normalizar(0.55);
    }

    /// <summary>
    /// A camada da última volta. Mesmo andamento, meio-tom de tensão por cima
    /// e um tremido agudo: entra por cima do pulso sem precisar cortá-lo.
    /// </summary>
    public static Onda TensaoFinal()
    {
        const double compasso = 4.0 * 60.0 / 112.0;
        double passo = compasso / 8.0;
        var m = new Onda(compasso);
        m.Somar(Onda.SenoEmLoop(compasso, 103.8), 0.34);   // segunda menor acima da tônica
        m.Somar(Onda.SenoEmLoop(compasso, 155.6), 0.2);
        m.Somar(Onda.SenoEmLoop(compasso, 207.6, 0.3), 0.12);
        for (int i = 0; i < 8; i++)
        {
            var tique = Onda.Ruido(0.1, 83 + i).Bp(2600, q: 3.0).Ad(0.0008, 0.045, 3.0);
            m.Somar(tique, i % 2 == 0 ? 0.22 : 0.12, i * passo);
        }
        return m.Lp(2600, q: 1.1).Saturar(1.6).Normalizar(0.5);
    }
}
