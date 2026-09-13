// Orquestração da corrida — porte fiel de game/race.py.
//
// Junta as naves e os itens, e é o ÚNICO lugar que escreve no barramento de
// saída. Não lê teclado nem serial e não desenha nada: recebe os pulsos de
// cada jogador e escreve PWM.
//
// As animações entram por injeção (IEfeitos). Sem elas a corrida roda igual,
// com um objeto que engole as chamadas — é assim que os testes rodam sem o
// Godot.

using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbitalDerby.Core;

/// <summary>Bordas de subida de um jogador neste frame.</summary>
public readonly record struct Pulso(bool Acelerador, bool Acao)
{
    public static readonly Pulso Nenhum = new(false, false);
}

/// <summary>Tom de uma linha do rodapé; a interface decide a cor.</summary>
public enum Tom { Neutro, Ok, Fraco, Alerta, Ion, Ignis }

public readonly record struct LinhaLog(string Texto, Tom Tom);

/// <summary>Contrato com o hardware: um PWM por pista, mais um rótulo do efeito dominante.</summary>
public interface IBarramentoSaida
{
    void DefinirPista(int lane, double pwm, string tag);
}

public sealed class SaidaNula : IBarramentoSaida
{
    private readonly double[] _pwm = new double[2];
    private readonly string[] _tag = { "", "" };

    public void DefinirPista(int lane, double pwm, string tag)
    {
        // Clamp defensivo: nenhum valor fora de [0,1] vaza para o motor.
        _pwm[lane] = Math.Clamp(pwm, 0.0, 1.0);
        _tag[lane] = tag;
    }

    public double Pwm(int lane) => _pwm[lane];
    public string Tag(int lane) => _tag[lane];
}

/// <summary>Eventos que a camada visual quer saber. Nenhum deles altera o jogo.</summary>
public interface IEfeitos
{
    void Limpar();
    void Largada();
    void Premio(Nave nave, Item item);
    void Ataque(Nave origem, Nave alvo, Item tipo, bool errou);
    void Impacto(Nave alvo, Item tipo, ResultadoAtaque resultado);
    void EscudoLevantado(Nave nave);
    void Superaquecimento(Nave nave);
}

public sealed class SemEfeitos : IEfeitos
{
    public static readonly SemEfeitos Instancia = new();
    public void Limpar() { }
    public void Largada() { }
    public void Premio(Nave nave, Item item) { }
    public void Ataque(Nave origem, Nave alvo, Item tipo, bool errou) { }
    public void Impacto(Nave alvo, Item tipo, ResultadoAtaque resultado) { }
    public void EscudoLevantado(Nave nave) { }
    public void Superaquecimento(Nave nave) { }
}

/// <summary>Um ataque a caminho do alvo. O efeito só vale na chegada.</summary>
public sealed class Voo
{
    public int Origem;
    public int Alvo;
    public Item Tipo;
    public double Restante;
    public double Duracao;

    /// <summary>0 no disparo, 1 na chegada. Só a animação usa.</summary>
    public double Progresso => Duracao <= 0 ? 1.0 : Math.Clamp(1.0 - Restante / Duracao, 0.0, 1.0);
}

public sealed class Corrida
{
    public readonly IBarramentoSaida Saida;
    public IEfeitos Fx;
    public Nave[] Naves = Array.Empty<Nave>();
    public double Tempo;
    public Nave? Vencedor;
    public readonly List<LinhaLog> Log = new();
    public readonly List<Voo> EmVoo = new();

    private readonly Random _rng;

    public Corrida(IBarramentoSaida saida, IEfeitos? fx = null, int? semente = null)
    {
        Saida = saida;
        Fx = fx ?? SemEfeitos.Instancia;
        _rng = semente.HasValue ? new Random(semente.Value) : new Random();
        Reiniciar();
    }

    public Random Rng => _rng;

    // -- ciclo de vida ------------------------------------------------------

    public void Reiniciar()
    {
        Naves = new[] { new Nave(0, Cfg.NomeP1), new Nave(1, Cfg.NomeP2) };
        Tempo = 0.0;
        Vencedor = null;
        Log.Clear();
        EmVoo.Clear();
        Fx.Limpar();
        foreach (var nave in Naves)
            Saida.DefinirPista(nave.Lane, 0.0, "parado");
    }

    public Nave Adversario(Nave nave) => Naves[1 - nave.Lane];

    public static Tom TomDe(Nave nave) => nave.Lane == 0 ? Tom.Ion : Tom.Ignis;

    /// <summary>
    /// Do primeiro para o último. Quem já cruzou a linha vem antes, pelo tempo
    /// de chegada — ordenar só por progresso poria em primeiro quem andou mais
    /// depois da bandeira.
    /// </summary>
    public IReadOnlyList<Nave> Classificacao() =>
        Naves.OrderBy(n => n.Terminou ? 0 : 1)
             .ThenBy(n => n.Terminou ? n.TempoFinal : -n.Progresso)
             .ToList();

    public int Posicao(Nave nave)
    {
        var ordem = Classificacao();
        for (int i = 0; i < ordem.Count; i++)
            if (ReferenceEquals(ordem[i], nave))
                return i + 1;
        return ordem.Count;
    }

    public void Anotar(string texto, Tom tom = Tom.Neutro)
    {
        Log.Add(new LinhaLog(texto, tom));
        if (Log.Count > 4)
            Log.RemoveRange(0, Log.Count - 4);
    }

    // -- ponte para o hardware ---------------------------------------------

    /// <summary>
    /// Corrige a posição estimada de uma pista a partir de um sensor real. A
    /// lógica já trata t como estimativa; a passagem registrada aqui abre a
    /// caixa pelo mesmo caminho que a integração abre hoje.
    /// </summary>
    public void SincronizarPosicao(int lane, double t) => Naves[lane].SincronizarPosicao(t);

    // -- passo de simulação -------------------------------------------------

    public void Atualizar(double dt, Pulso p1, Pulso p2, bool correndo)
    {
        // O cronômetro corre enquanto houver alguém na pista: o segundo
        // colocado também merece um tempo registrado.
        if (correndo && !Naves.All(n => n.Terminou))
            Tempo += dt;

        if (correndo)
            ResolverVoos(dt);

        for (int i = 0; i < Naves.Length; i++)
        {
            var nave = Naves[i];
            Pulso pulso = i == 0 ? p1 : p2;

            // A caixa avança sua máquina de estados. A nave não para por ela.
            Item? premio = nave.Roleta.Atualizar(dt);
            if (premio.HasValue)
                EntregarPremio(nave, premio.Value);

            if (correndo && pulso.Acao)
                Acao(nave);

            // Acelerador de martelar: o que conta é a borda, não o estado.
            double overAntes = nave.Superaquecimento;
            nave.Atualizar(dt, pulso.Acelerador, correndo);
            if (nave.Superaquecimento > 0.0 && overAntes <= 0.0)
                Fx.Superaquecimento(nave);

            // Passou por um sensor com o slot vazio: a janela da caixa abre.
            if (correndo
                && nave.CheckpointCruzado.HasValue
                && nave.Slot is null
                && nave.Roleta.Estado == EstadoRoleta.Parada
                && !nave.Terminou)
            {
                nave.Roleta.Abrir(nave.CheckpointCruzado.Value);
            }
        }

        // Chegada. Marcar quem chegou é independente de quem venceu: durante
        // a espera do placar o segundo colocado ainda pode cruzar a linha.
        if (correndo)
        {
            foreach (var nave in Naves)
            {
                if (nave.Voltas >= Cfg.VoltasParaVencer && !nave.Terminou)
                {
                    nave.Terminou = true;
                    nave.TempoFinal = Tempo;
                    nave.Roleta.Cancelar();
                    if (Vencedor is null)
                    {
                        Vencedor = nave;
                        Anotar($"{nave.Nome} completou {Cfg.VoltasParaVencer} voltas", TomDe(nave));
                    }
                    else
                    {
                        Anotar($"{nave.Nome} chegou em seguida", TomDe(nave));
                    }
                }
            }
        }

        // Único ponto de escrita no barramento.
        foreach (var nave in Naves)
            Saida.DefinirPista(nave.Lane, nave.Pwm, nave.EffectTag());
    }

    // -- ataques em voo -----------------------------------------------------

    /// <summary>
    /// A mira é decidida no disparo, mas o efeito no PWM só é aplicado na
    /// chegada — o que permite levantar o Escudo com a bomba no ar, e faz o
    /// impacto na tela cair no mesmo instante em que o carrinho perde força.
    /// </summary>
    private void ResolverVoos(double dt)
    {
        if (EmVoo.Count == 0)
            return;

        var chegaram = new List<Voo>();
        for (int i = EmVoo.Count - 1; i >= 0; i--)
        {
            EmVoo[i].Restante -= dt;
            if (EmVoo[i].Restante <= 0.0)
            {
                chegaram.Insert(0, EmVoo[i]);
                EmVoo.RemoveAt(i);
            }
        }

        foreach (var voo in chegaram)
        {
            var origem = Naves[voo.Origem];
            var alvo = Naves[voo.Alvo];
            var resultado = alvo.Receber(voo.Tipo);
            Fx.Impacto(alvo, voo.Tipo, resultado);
            if (resultado == ResultadoAtaque.Atingido)
            {
                Anotar(voo.Tipo == Item.Bomba
                        ? $"Bomba de {origem.Nome} parou {alvo.Nome}"
                        : $"Tiro de {origem.Nome} acertou {alvo.Nome}",
                    TomDe(origem));
            }
            else
            {
                Anotar($"{alvo.Nome} bloqueou o {Cfg.NomeItem(voo.Tipo).ToLowerInvariant()}", Tom.Ok);
            }
        }
    }

    // -- caixa de item ------------------------------------------------------

    private void EntregarPremio(Nave nave, Item premio)
    {
        if (premio == Item.Nada)
        {
            nave.Slot = null;
            nave.Mensagem("Não veio nada");
            Anotar($"{nave.Nome} girou e não veio nada", Tom.Fraco);
        }
        else
        {
            nave.Slot = premio;
            nave.Mensagem($"{Cfg.NomeItem(premio)} no slot");
            Anotar($"{nave.Nome} pegou {Cfg.NomeItem(premio)}", TomDe(nave));
        }
        Fx.Premio(nave, premio);
    }

    // -- ação do jogador ----------------------------------------------------

    /// <summary>
    /// Um botão, dois significados: com a janela aberta e o slot vazio, gira;
    /// com item no slot, usa — a qualquer momento.
    /// </summary>
    private void Acao(Nave nave)
    {
        if (nave.Terminou
            || nave.Roleta.Estado == EstadoRoleta.Girando
            || nave.Roleta.Estado == EstadoRoleta.Revelando)
            return;

        if (nave.Slot is null)
        {
            if (nave.Roleta.PodeGirar)
            {
                bool atrasado = nave.Progresso < Adversario(nave).Progresso;
                nave.Roleta.Girar(atrasado, _rng);
                nave.Mensagem("Girando", Cfg.RoletaGiro);
            }
            else
            {
                nave.Mensagem("Nada no slot");
            }
            return;
        }

        UsarItem(nave);
    }

    private void UsarItem(Nave nave)
    {
        Item item = nave.Slot!.Value;
        var alvo = Adversario(nave);
        nave.Slot = null;

        if (item is Item.Tiro or Item.Bomba)
        {
            // A mira é conferida no disparo: só pega alvo PERTO, pelo caminho
            // mais curto da pista. Errar queima o item.
            double alcance = item == Item.Tiro ? Cfg.TiroAlcance : Cfg.BombaAlcance;
            bool perto = Pista.DistanciaCurta(nave.T, alvo.T) < alcance;
            string nome = Cfg.NomeItem(item);
            if (perto)
            {
                nave.Mensagem(item == Item.Tiro ? "Tiro disparado" : "Bomba lançada");
                Lancar(nave, alvo, item, item == Item.Tiro ? Cfg.TiroVoo : Cfg.BombaVoo);
            }
            else
            {
                nave.Mensagem($"{nome} errou");
                Fx.Ataque(nave, alvo, item, errou: true);
                Anotar($"{nome} de {nave.Nome} passou longe", Tom.Fraco);
            }
        }
        else if (item == Item.Escudo)
        {
            nave.Escudo = true;
            nave.Mensagem("Escudo ativo");
            Anotar($"{nave.Nome} levantou Escudo", TomDe(nave));
            Fx.EscudoLevantado(nave);
        }
    }

    private void Lancar(Nave origem, Nave alvo, Item tipo, double voo)
    {
        EmVoo.Add(new Voo { Origem = origem.Lane, Alvo = alvo.Lane, Tipo = tipo, Restante = voo, Duracao = voo });
        Fx.Ataque(origem, alvo, tipo, errou: false);
    }
}
