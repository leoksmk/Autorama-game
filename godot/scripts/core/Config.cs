// Constantes de regra do ORBITAL DERBY — porte fiel de game/config.py.
//
// Este é o único lugar onde números de regra podem existir. As posições dos
// checkpoints terão de ser REMEDIDAS contra os sensores físicos do autorama,
// senão a nave na tela dessincroniza do carrinho de verdade.
//
// Nada aqui depende do Godot: o núcleo de regras é C# puro e roda nos testes
// (dotnet test) sem o motor gráfico aberto.

namespace OrbitalDerby.Core;

/// <summary>As quatro faces da caixa de item. "Nada" nunca chega ao slot.</summary>
public enum Item { Tiro, Bomba, Escudo, Nada }

public static class Cfg
{
    public const string NomeP1 = "ÍON";
    public const string NomeP2 = "ÍGNIS";

    public const int VoltasParaVencer = 5;
    public const double ContagemDuracao = 3.0;

    // ------------------------------------------------------------------
    // Acelerador de martelar
    // ------------------------------------------------------------------
    // O que vira PWM é a FREQUÊNCIA dos apertos, medida pelo intervalo entre
    // um clique e o próximo:
    //
    //     2 cliques/s -> esforço 0,25   (arrastando)
    //     4 cliques/s -> esforço 0,50
    //     8 cliques/s -> esforço 1,00   (PWM cheio)
    //
    // Medir o intervalo, e não acumular pulsos que decaem, mantém o PWM
    // ESTÁVEL entre um clique e outro — com acumulador o duty oscilaria ~50%
    // a cada aperto e o carrinho real trepidaria.
    public const double CadenciaPlenaHz = 8.0;
    public const double CadenciaSuavizacao = 0.55;   // quanto cada novo intervalo pesa
    public const double CliqueIntervaloMin = 0.04;   // 25 Hz: teto anti-repique
    public const double CliqueTimeout = 0.6;         // sem aperto por isso, o motor corta
    // O primeiro aperto depois de uma pausa não tem intervalo para medir; sem
    // este empurrão, apertar uma vez não faria nada e o botão pareceria morto.
    public const double CadenciaInicialHz = 2.5;

    // Velocidade em VOLTAS POR SEGUNDO.
    public const double Accel = 0.25;
    public const double Decel = 0.35;
    public const double Cap = 0.16;                  // ~6,2 s por volta no teto

    // Regra de ouro: o único atuador físico é o PWM da pista (0 a 1).
    public const double PwmBase = 0.55;
    public const double VelPorPwm = Cap / PwmBase;   // derivada, nunca digitada

    // ------------------------------------------------------------------
    // Calor
    // ------------------------------------------------------------------
    public const double CalorSubida = 1.0 / 4.5;     // 0 -> 1 em 4,5 s no máximo
    public const double CalorDescida = 1.0 / 3.0;    // 1 -> 0 em 3,0 s em ritmo baixo
    // Abaixo deste esforço o motor esfria; acima, esquenta proporcionalmente.
    public const double CalorLimiar = 0.55;
    public const double SuperaquecimentoDuracao = 1.6;
    public const double SuperaquecimentoCalorResidual = 0.75;

    // ------------------------------------------------------------------
    // Checkpoints e caixa de item
    // ------------------------------------------------------------------
    // ATENÇÃO: remedir contra a posição física dos sensores.
    //
    // Os quatro caem em marcos do traçado, não em frações redondas: entrada do
    // S, saída do curvão, fim da reta oposta e ápice da subida. Como o t é
    // reparametrizado por comprimento (Mundo.Tracado), cada valor aqui é uma
    // distância fixa em metros desde a largada — que é o que o sensor mede.
    //
    // Nenhum deles cai em trecho inclinado, e isso é requisito, não sorte: o
    // pórtico do checkpoint é construído no quadro local do leito, então um
    // checkpoint no meio de uma curva de 40° daria um portal tombado 40°. O
    // CP2 nasceu em 0,38, no curvão, e foi para 0,35 — a saída do S — por isso.
    //
    //   CP1 -> CP2   81 m   1,31 s no talo
    //   CP2 -> CP3   97 m   1,56 s
    //   CP3 -> CP4   89 m   1,44 s
    //   CP4 -> CP1  120 m   1,94 s
    public static readonly double[] Checkpoints = { 0.14, 0.35, 0.60, 0.83 };

    // A janela é de TEMPO, não de posição: cruzar o checkpoint abre a caixa e
    // ela fica aberta por RoletaOportunidade. Casa com o sensor físico, que
    // dispara um evento pontual.
    public const double RoletaOportunidade = 0.8;
    public const double RoletaGiro = 1.5;            // a nave segue andando no giro
    public const double RoletaRevelacao = 1.0;

    // ------------------------------------------------------------------
    // Itens
    // ------------------------------------------------------------------
    // Tiro derruba o teto de PWM, Bomba zera o PWM, Escudo cancela um ataque
    // enquanto durar. "Nada" é o único risco da caixa — como girar não custa
    // mais velocidade, é o que impede que apertar em todo checkpoint seja de graça.
    public static readonly (Item Item, double Peso)[] ItemPesos =
    {
        (Item.Tiro, 34),
        (Item.Bomba, 26),
        (Item.Escudo, 22),
        (Item.Nada, 18),
    };

    public const double BombaDuracao = 2.0;          // PWM do alvo zerado
    // Tempo de voo: o efeito só vale na CHEGADA, então dá para levantar o
    // Escudo com a bomba no ar. É regra, não animação.
    public const double BombaVoo = 0.55;
    public const double TiroVoo = 0.38;

    // Alcance em voltas, pelo caminho mais curto da pista, em qualquer sentido.
    public const double TiroAlcance = 0.12;
    public const double BombaAlcance = 0.12;

    public const double TiroMult = 0.45;             // teto de PWM do alvo
    public const double TiroDuracao = 2.5;

    // O Escudo não dura para sempre: ele cai numa PASSAGEM POR SENSOR, como
    // tudo mais que é temporal neste jogo. EscudoTrechos é quantas passagens
    // ele aguenta — 2, e não 1, por uma razão de aritmética:
    //
    // quem levanta o escudo está sempre NO MEIO de um trecho, então gastar a
    // primeira passagem deixaria uma proteção de duração imprevisível, em
    // média meio trecho (~0,7 s) — menos que o voo de uma bomba mais o tempo
    // de reação, ou seja, um item que não dá para usar. Com 2, o escudo cobre
    // o resto do trecho onde foi levantado MAIS o trecho seguinte inteiro:
    // de 1,3 s (levantou colado num sensor) a 3,3 s (levantou logo depois de
    // passar por um). Ele morre num checkpoint, que é a regra pedida, e sempre
    // vale pelo menos um trecho completo.
    public const int EscudoTrechos = 2;

    // Catch-up: quem está atrás sorteia mais ataque e menos caixa vazia.
    public const bool CatchupAtivo = true;
    public const double CatchupBias = 0.55;
    public static readonly Item[] CatchupFavorece = { Item.Tiro, Item.Bomba };
    public static readonly Item[] CatchupDesfavorece = { Item.Nada };

    public static string NomeItem(Item item) => item switch
    {
        Item.Tiro => "Tiro",
        Item.Bomba => "Bomba",
        Item.Escudo => "Escudo",
        _ => "Nada",
    };

    public static string DescricaoItem(Item item) => item switch
    {
        Item.Tiro => "deixa o adversário lento",
        Item.Bomba => "para o adversário por 2 s",
        Item.Escudo => "bloqueia um ataque até o próximo checkpoint",
        _ => "a caixa veio vazia",
    };
}
