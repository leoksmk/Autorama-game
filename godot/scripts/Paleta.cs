// Paleta da identidade visual: universo original, sem referência a franquias.

using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby;

public static class Paleta
{
    public static readonly Color Fundo = new("04060d");
    public static readonly Color Painel = new("0c1220");
    public static readonly Color Texto = new("e6edf7");
    public static readonly Color TextoFraco = new("74849e");
    public static readonly Color Estacao = new("7c5cff");
    public static readonly Color Alerta = new("ff4d6d");
    public static readonly Color Ok = new("3fe0a0");

    public static readonly Color Ion = new("2fd6ff");
    public static readonly Color Ignis = new("ffa02e");

    public static Color DoJogador(int lane) => lane == 0 ? Ion : Ignis;

    public static Color DoItem(Item item) => item switch
    {
        Item.Tiro => new Color("ff6b5c"),
        Item.Bomba => new Color("ffc43d"),
        Item.Escudo => new Color("5ae0d8"),
        _ => new Color("6b7486"),
    };

    public static Color DoTom(Tom tom) => tom switch
    {
        Tom.Ok => Ok,
        Tom.Fraco => TextoFraco,
        Tom.Alerta => Alerta,
        Tom.Ion => Ion,
        Tom.Ignis => Ignis,
        _ => Texto,
    };

    /// <summary>Cor HDR para emissão: acima de 1 alimenta o bloom.</summary>
    public static Color Hdr(Color c, float energia) => new(c.R * energia, c.G * energia, c.B * energia, c.A);
}
