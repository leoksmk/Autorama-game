// Aritmética da volta. A posição de cada nave é um t normalizado em [0,1).
//
// Aqui só existe a parte de REGRA do traçado (distâncias e checkpoints). A
// geometria 3D — onde t fica no espaço — mora em scripts/world, porque só o
// desenho precisa dela.
//
// Nota para o hardware: t é sempre uma ESTIMATIVA. Hoje vem da integração de
// speed * dt; com sensores, continua vindo da integração e é só corrigido
// quando um sensor dispara (Corrida.SincronizarPosicao).

using System;

namespace OrbitalDerby.Core;

public static class Pista
{
    /// <summary>Resto em [0,1), inclusive para negativos (o % do C# não faz isso).</summary>
    public static double Mod1(double x)
    {
        x %= 1.0;
        return x < 0.0 ? x + 1.0 : x;
    }

    /// <summary>
    /// Menor distância entre duas naves, em voltas, em qualquer sentido. Sempre
    /// em [0, 0.5]. É o que os ataques usam: perto é perto, à frente ou atrás.
    /// </summary>
    public static double DistanciaCurta(double tA, double tB)
    {
        double d = Math.Abs(tA - tB) % 1.0;
        return Math.Min(d, 1.0 - d);
    }

    /// <summary>
    /// Índice do checkpoint cruzado entre dois instantes, ou null.
    ///
    /// Detecta a PASSAGEM — o mesmo evento pontual que o sensor físico gera —
    /// e não "estar perto". O avanço é medido para frente, então a virada da
    /// volta não confunde a conta.
    /// </summary>
    public static int? CruzouCheckpoint(double tAnterior, double tNovo)
    {
        double avanco = Mod1(tNovo - tAnterior);
        if (avanco <= 0.0 || avanco >= 1.0)
            return null;
        for (int i = 0; i < Cfg.Checkpoints.Length; i++)
        {
            double ateCp = Mod1(Cfg.Checkpoints[i] - tAnterior);
            if (ateCp > 0.0 && ateCp <= avanco)
                return i;
        }
        return null;
    }
}
