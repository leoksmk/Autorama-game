using System.Text.Json;
using Xunit;

namespace OrbitalDerby.Core.Tests;

/// <summary>
/// Prova de que o porte não mudou a física: o mesmo roteiro de cliques, rodado
/// na versão Python validada (tests/gerar_referencia.py) e aqui, tem de dar os
/// mesmos números.
/// </summary>
public class ReferenciaPythonTests
{
    private const double Tol = 1e-9;

    private static JsonElement Referencia()
    {
        string caminho = Path.Combine(AppContext.BaseDirectory, "referencia_python.json");
        Assert.True(File.Exists(caminho), "rode: python tests/gerar_referencia.py");
        return JsonDocument.Parse(File.ReadAllText(caminho)).RootElement;
    }

    [Fact]
    public void Trajetorias_batem_com_a_versao_python()
    {
        foreach (var caso in Referencia().GetProperty("trajetorias").EnumerateArray())
        {
            string nome = caso.GetProperty("nome").GetString()!;
            double hz = caso.GetProperty("hz").GetDouble();
            double segundos = caso.GetProperty("segundos").GetDouble();
            double? soltarEm = caso.GetProperty("soltar_em").ValueKind == JsonValueKind.Null
                ? null : caso.GetProperty("soltar_em").GetDouble();

            var pontos = caso.GetProperty("pontos").EnumerateArray().ToList();
            var nave = new Nave(0, Cfg.NomeP1, 0.0);
            double prox = 0.0;
            int k = 0;
            for (int i = 0; i < (int)(segundos * 60); i++)
            {
                bool clique = false;
                bool ativo = soltarEm is null || i < soltarEm.Value * 60;
                if (hz > 0 && ativo && i >= prox)
                {
                    clique = true;
                    prox += 60.0 / hz;
                }
                nave.Atualizar(Apoio.DT, clique, true);

                if (k < pontos.Count && pontos[k].GetProperty("frame").GetInt32() == i)
                {
                    var p = pontos[k++];
                    string onde = $"{nome}, frame {i}";
                    Assert.True(Math.Abs(p.GetProperty("t").GetDouble() - nave.T) < Tol, $"{onde}: t");
                    Assert.True(Math.Abs(p.GetProperty("speed").GetDouble() - nave.Speed) < Tol, $"{onde}: speed");
                    Assert.True(Math.Abs(p.GetProperty("pwm").GetDouble() - nave.Pwm) < Tol, $"{onde}: pwm");
                    Assert.True(Math.Abs(p.GetProperty("calor").GetDouble() - nave.Calor) < Tol, $"{onde}: calor");
                    Assert.True(Math.Abs(p.GetProperty("superaquecimento").GetDouble() - nave.Superaquecimento) < Tol,
                        $"{onde}: superaquecimento");
                    Assert.Equal(p.GetProperty("voltas").GetInt32(), nave.Voltas);
                }
            }
            Assert.Equal(pontos.Count, k);
        }
    }

    [Fact]
    public void Corridas_inteiras_batem_com_a_versao_python()
    {
        foreach (var caso in Referencia().GetProperty("corridas").EnumerateArray())
        {
            var hzs = caso.GetProperty("hz").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var r = new Corrida(new SaidaNula());
            var prox = new double[2];
            int frames = 0;
            for (int i = 0; i < 300 * 60; i++)
            {
                var cliques = new bool[2];
                for (int lane = 0; lane < 2; lane++)
                {
                    if (i >= prox[lane])
                    {
                        cliques[lane] = true;
                        prox[lane] += 60.0 / hzs[lane];
                    }
                }
                r.Atualizar(Apoio.DT, new Pulso(cliques[0], false), new Pulso(cliques[1], false), true);
                frames = i + 1;
                if (r.Naves.All(n => n.Terminou))
                    break;
            }

            string onde = $"corrida {hzs[0]} x {hzs[1]} Hz";
            Assert.Equal(caso.GetProperty("frames").GetInt32(), frames);
            Assert.Equal(caso.GetProperty("vencedor").GetInt32(), r.Vencedor?.Lane ?? -1);
            var tempos = caso.GetProperty("tempos").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            for (int lane = 0; lane < 2; lane++)
                Assert.True(Math.Abs(tempos[lane] - r.Naves[lane].TempoFinal) < Tol, $"{onde}: tempo da pista {lane}");
        }
    }
}
