// Caixa de item de um jogador — porte de game/items.py.
//
// Ciclo:
//
//     Parada -> (cruzou checkpoint com o slot vazio) -> Oportunidade
//     Oportunidade -> (não apertou em RoletaOportunidade) -> Parada
//     Oportunidade -> (apertou a ação)                   -> Girando
//     Girando      -> (RoletaGiro, a nave segue andando)  -> Revelando
//     Revelando    -> (RoletaRevelacao)                   -> Parada, prêmio entregue
//
// O giro é do tipo caixa de corrida: a face troca rápido e vai travando. A
// sequência de faces é montada DE TRÁS PARA FRENTE a partir do prêmio já
// sorteado, então a última face exibida é sempre o prêmio de verdade.

using System;

namespace OrbitalDerby.Core;

public enum EstadoRoleta { Parada, Oportunidade, Girando, Revelando }

public sealed class Roleta
{
    /// <summary>Ordem fixa das faces: o jogador aprende a sequência e lê a desaceleração.</summary>
    public static readonly Item[] Ordem = { Item.Tiro, Item.Bomba, Item.Escudo, Item.Nada };

    /// <summary>Quantas trocas de face o giro inteiro tem.</summary>
    public const int CiclosDoGiro = 22;

    private const double IntervaloOcioso = 0.22;

    public EstadoRoleta Estado { get; private set; } = EstadoRoleta.Parada;
    public double Tempo { get; private set; }
    public Item? Resultado { get; private set; }
    public int? Checkpoint { get; private set; }
    public Item Face { get; private set; } = Ordem[0];

    private Item[] _faces = Array.Empty<Item>();
    private double _ocioso;

    /// <summary>Girando. Não trava a nave: ela segue enquanto a caixa roda.</summary>
    public bool Ativa => Estado == EstadoRoleta.Girando;
    public bool Visivel => Estado != EstadoRoleta.Parada;
    public bool PodeGirar => Estado == EstadoRoleta.Oportunidade;

    /// <summary>Quanto sobra da janela de oportunidade, de 1 a 0.</summary>
    public double FracaoRestante =>
        Estado != EstadoRoleta.Oportunidade
            ? 0.0
            : Math.Max(0.0, 1.0 - Tempo / Cfg.RoletaOportunidade);

    public double ProgressoGiro => Estado switch
    {
        EstadoRoleta.Girando => Math.Min(1.0, Tempo / Cfg.RoletaGiro),
        EstadoRoleta.Revelando => 1.0,
        _ => 0.0,
    };

    /// <summary>
    /// Sorteia uma face. Quem está atrás (<paramref name="atrasado"/>) recebe o
    /// viés de catch-up; <paramref name="catchup"/> permite testar o
    /// balanceamento cru.
    /// </summary>
    public static Item Sortear(bool atrasado, Random rng, bool catchup = Cfg.CatchupAtivo)
    {
        Span<double> pesos = stackalloc double[Cfg.ItemPesos.Length];
        double total = 0.0;
        for (int i = 0; i < Cfg.ItemPesos.Length; i++)
        {
            var (item, peso) = Cfg.ItemPesos[i];
            double p = peso;
            if (atrasado && catchup)
            {
                if (Array.IndexOf(Cfg.CatchupFavorece, item) >= 0)
                    p *= 1.0 + Cfg.CatchupBias;
                else if (Array.IndexOf(Cfg.CatchupDesfavorece, item) >= 0)
                    p *= Math.Max(0.0, 1.0 - Cfg.CatchupBias);
            }
            pesos[i] = p;
            total += p;
        }

        double r = rng.NextDouble() * total;
        for (int i = 0; i < pesos.Length; i++)
        {
            if (r < pesos[i])
                return Cfg.ItemPesos[i].Item;
            r -= pesos[i];
        }
        return Cfg.ItemPesos[^1].Item;
    }

    private static double EaseOut(double p) => 1.0 - Math.Pow(1.0 - p, 3);

    /// <summary>Cruzou um checkpoint com o slot vazio: a janela abre.</summary>
    public void Abrir(int checkpoint)
    {
        Estado = EstadoRoleta.Oportunidade;
        Tempo = 0.0;
        Checkpoint = checkpoint;
        Resultado = null;
        _ocioso = 0.0;
    }

    /// <summary>
    /// Apertou a ação dentro da janela. Devolve false se não havia janela.
    /// <paramref name="forcar"/> existe para demonstrações e testes.
    /// </summary>
    public bool Girar(bool atrasado, Random rng, Item? forcar = null)
    {
        if (Estado != EstadoRoleta.Oportunidade)
            return false;

        Estado = EstadoRoleta.Girando;
        Tempo = 0.0;
        Item premio = forcar ?? Sortear(atrasado, rng);
        Resultado = premio;

        int idx = Array.IndexOf(Ordem, premio);
        int n = CiclosDoGiro;
        _faces = new Item[n];
        for (int i = 0; i < n; i++)
        {
            int k = (idx - (n - 1 - i)) % Ordem.Length;
            if (k < 0) k += Ordem.Length;
            _faces[i] = Ordem[k];
        }
        Face = _faces[0];
        return true;
    }

    public void Cancelar()
    {
        Estado = EstadoRoleta.Parada;
        Tempo = 0.0;
        Resultado = null;
        Checkpoint = null;
        _faces = Array.Empty<Item>();
    }

    /// <summary>Avança a máquina de estados. Devolve o prêmio no frame em que a revelação termina.</summary>
    public Item? Atualizar(double dt)
    {
        if (Estado == EstadoRoleta.Parada)
            return null;

        Tempo += dt;

        switch (Estado)
        {
            case EstadoRoleta.Oportunidade:
                // A caixa troca de face devagar, convidando a apertar.
                _ocioso += dt;
                if (_ocioso >= IntervaloOcioso)
                {
                    _ocioso = 0.0;
                    Face = Ordem[(Array.IndexOf(Ordem, Face) + 1) % Ordem.Length];
                }
                if (Tempo >= Cfg.RoletaOportunidade)
                    Cancelar();
                return null;

            case EstadoRoleta.Girando:
            {
                double p = Math.Min(1.0, Tempo / Cfg.RoletaGiro);
                int i = (int)(EaseOut(p) * (_faces.Length - 1));
                Face = _faces[i];
                if (Tempo >= Cfg.RoletaGiro)
                {
                    Face = Resultado!.Value;
                    Estado = EstadoRoleta.Revelando;
                    Tempo = 0.0;
                }
                return null;
            }

            default: // Revelando
                Face = Resultado!.Value;
                if (Tempo >= Cfg.RoletaRevelacao)
                {
                    Item? premio = Resultado;
                    Cancelar();
                    return premio;
                }
                return null;
        }
    }
}
