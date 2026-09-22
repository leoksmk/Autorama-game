// Um circuito: a forma da pista mais tudo que muda junto com ela.
//
// Cada circuito carrega o próprio polígono de controle, os próprios
// checkpoints e o próprio RITMO. O ritmo existe por um motivo que só aparece
// quando há mais de uma pista: `Speed` é medida em VOLTAS por segundo, então,
// sem um fator por circuito, qualquer traçado levaria os mesmos 6,25 s por
// volta — e o INTERLAGOS ORBITAL, que tem 709 m, passaria a 408 km/h. O fator
// converte o teto de voltas por segundo em metros por segundo comparáveis.
//
// Um circuito NÃO é escolhido no meio de uma corrida. Trocar reconstrói a
// malha da pista, o cinturão de pedras e a estação, então a troca só acontece
// no menu (Main.AplicarCircuito).

using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby.Mundo;

public sealed class Circuito
{
    public required string Nome { get; init; }
    public required string Resumo { get; init; }

    /// <summary>Polígono de controle da B-spline fechada, em ordem de percurso: (x, altura, z).</summary>
    public required Vector3[] Controle { get; init; }

    /// <summary>
    /// Onde ficam os sensores, em fração da volta. **Remedir contra a pista
    /// física.** Nenhum deles deve cair em trecho inclinado: o pórtico é
    /// construído no quadro local do leito, e num trecho de 40° ele sai
    /// tombado 40°.
    /// </summary>
    public required double[] Checkpoints { get; init; }

    /// <summary>
    /// Multiplica o teto de velocidade. 1,0 é o teto cru de <see cref="Cfg.Cap"/>,
    /// que dá 6,25 s de volta. Escolha-o pela velocidade em tela que o circuito
    /// deve ter: fator = (m/s desejados) / comprimento / Cfg.Cap.
    /// </summary>
    public double Ritmo { get; init; } = 1.0;

    public int Voltas { get; init; } = 5;

    public float Largura { get; init; } = 11f;
    public float OffsetFaixa { get; init; } = 2.6f;

    /// <summary>Inclinação lateral máxima nas curvas, em radianos.</summary>
    public float InclinacaoMax { get; init; } = 0.42f;

    /// <summary>Raio (m) em que a curva já vale meia inclinação. Em metros, não no parâmetro.</summary>
    public float RaioDeReferencia { get; init; } = 26f;

    /// <summary>Onde a ÍRIS-9 fica neste circuito, e de que tamanho.</summary>
    public Vector3 EstacaoPos { get; init; } = new(2f, 7f, -2f);
    public float EstacaoRaio { get; init; } = 16f;

    /// <summary>
    /// Passa o polígono por uma média móvel fechada: P[i] ← (P[i-1] + 2P[i] + P[i+1]) / 4.
    ///
    /// É um passa-baixa na FORMA, e é assim que a versão suave do ANEL DE ÍCARO
    /// nasce da bruta — sem redesenhar nada à mão, o que manteria as duas
    /// divergindo a cada ajuste. Duas passagens é o teto útil: na terceira o S
    /// deixa de inverter e o circuito vira um oval.
    /// </summary>
    public static Vector3[] Suavizar(Vector3[] pontos, int passagens, float escalaXZ = 1f, float escalaY = 1f)
    {
        var p = (Vector3[])pontos.Clone();
        int n = p.Length;
        for (int k = 0; k < passagens; k++)
        {
            var q = new Vector3[n];
            for (int i = 0; i < n; i++)
                q[i] = (p[(i - 1 + n) % n] + p[i] * 2f + p[(i + 1) % n]) / 4f;
            p = q;
        }
        for (int i = 0; i < n; i++)
            p[i] = new Vector3(p[i].X * escalaXZ, p[i].Y * escalaY, p[i].Z * escalaXZ);
        return p;
    }
}
