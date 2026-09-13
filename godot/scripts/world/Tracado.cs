// Geometria 3D do traçado: converte o t da regra (Core.Pista) em posição e
// orientação no espaço. Mesma forma da versão 2D — elipse com modulação
// r = 1 + 0,075·cos(3a) —, agora com morros suaves e inclinação nas curvas.
//
// Só o desenho usa isto. A regra continua raciocinando em t normalizado, e é
// por isso que os checkpoints físicos se remedem em t, não em metros.

using System;
using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby.Mundo;

public static class Tracado
{
    public const float RaioX = 44f;
    public const float RaioZ = 29f;
    public const float Modulacao = 0.075f;
    public const int Harmonica = 3;
    public const float Ondulacao = 2.2f;       // altura dos morros, m
    public const float InclinacaoMax = 0.30f;  // inclinação lateral máxima nas curvas, rad
    public const float OffsetFaixa = 2.0f;     // do centro até cada faixa, m
    public const float Largura = 9f;           // largura total do leito, m
    public const float AlturaVoo = 0.62f;      // a nave flutua acima do leito

    /// <summary>Pista 0 por dentro, pista 1 por fora — igual à versão 2D.</summary>
    public static float OffsetDaFaixa(int lane) => lane == 0 ? -OffsetFaixa : OffsetFaixa;

    public static Vector3 Centro(double t)
    {
        double a = Math.Tau * t;
        double r = 1.0 + Modulacao * Math.Cos(Harmonica * a);
        return new Vector3(
            (float)(RaioX * r * Math.Cos(a)),
            (float)(Ondulacao * Math.Sin(2.0 * a + 0.6)),
            (float)(RaioZ * r * Math.Sin(a)));
    }

    private static Vector3 Tangente(double t)
    {
        const double h = 1e-4;
        return (Centro(t + h) - Centro(t - h)).Normalized();
    }

    /// <summary>Curvatura horizontal com sinal: positiva quando a pista vira à esquerda.</summary>
    private static float Curvatura(double t)
    {
        const double h = 2e-3;
        return Tangente(t - h).Cross(Tangente(t + h)).Y / (float)(2 * h);
    }

    /// <summary>
    /// Quadro local num ponto da volta: X à direita, Y para cima (já inclinado
    /// na curva), -Z para a frente. <paramref name="offset"/> positivo afasta do
    /// centro da órbita; <paramref name="altura"/> sobe pela normal do leito.
    /// </summary>
    public static Transform3D Quadro(double t, float offset = 0f, float altura = 0f)
    {
        t = Pista.Mod1(t);
        Vector3 p = Centro(t);
        Vector3 frente = Tangente(t);
        Vector3 cimaReto = (Vector3.Up - frente * frente.Dot(Vector3.Up)).Normalized();
        Vector3 esquerda = cimaReto.Cross(frente).Normalized();

        float curva = Curvatura(t);
        float banco = InclinacaoMax * MathF.Tanh(MathF.Abs(curva) / 14f);
        Vector3 cima = (cimaReto + esquerda * MathF.Sign(curva) * MathF.Tan(banco)).Normalized();
        Vector3 direita = frente.Cross(cima).Normalized();

        Vector3 pos = p + Fora(direita, p) * offset + cima * altura;
        return new Transform3D(new Basis(direita, cima, -frente), pos);
    }

    /// <summary>Direção lateral que aponta para fora da órbita, no plano inclinado.</summary>
    public static Vector3 Fora(Transform3D quadro) => Fora(quadro.Basis.X, quadro.Origin);

    private static Vector3 Fora(Vector3 direita, Vector3 p)
    {
        Vector3 radial = new(p.X, 0f, p.Z);
        return direita.Dot(radial) >= 0f ? direita : -direita;
    }

    // -- comprimento de arco, para a textura do leito --------------------------

    private const int Amostras = 4096;
    private static float[]? _acumulado;

    public static float Comprimento
    {
        get
        {
            Preparar();
            return _acumulado![Amostras];
        }
    }

    /// <summary>Metros percorridos desde a linha de largada até t.</summary>
    public static float Distancia(double t)
    {
        Preparar();
        double x = Pista.Mod1(t) * Amostras;
        int i = (int)x;
        float f = (float)(x - i);
        return Mathf.Lerp(_acumulado![i], _acumulado[Math.Min(i + 1, Amostras)], f);
    }

    private static void Preparar()
    {
        if (_acumulado is not null)
            return;
        var a = new float[Amostras + 1];
        for (int i = 1; i <= Amostras; i++)
            a[i] = a[i - 1] + Centro((double)(i - 1) / Amostras).DistanceTo(Centro((double)i / Amostras));
        _acumulado = a;
    }
}
