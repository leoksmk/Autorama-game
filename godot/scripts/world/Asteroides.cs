// Cinturão de asteroides em volta da pista, mais um campo distante de rochas
// grandes para dar profundidade.
//
// Quatro formas de rocha geradas por ruído, repetidas por MultiMesh: milhares
// de rochas em quatro chamadas de desenho. O giro de cada uma é feito no shader.

using System;
using System.Linq;
using Godot;

namespace OrbitalDerby.Mundo;

public partial class Asteroides : Node3D
{
    private const int PertoPorForma = 220;
    private const int LongePorForma = 70;

    private readonly System.Collections.Generic.List<MultiMeshInstance3D> _perto = new();

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = 4711 };
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/rocha.gdshader") };

        for (int k = 0; k < 4; k++)
        {
            var forma = Rocha(k, rng);
            forma.SurfaceSetMaterial(0, mat);
            var perto = Campo(forma, PertoPorForma, rng, perto: true);
            _perto.Add(perto);
            AddChild(perto);
            AddChild(Campo(forma, LongePorForma, rng, perto: false));
        }
    }

    /// <summary>
    /// Sombra das pedras próximas. É o item mais caro da cena: cada pedra entra
    /// no mapa de sombra da estrela. Só fica ligada na qualidade alta.
    /// </summary>
    public void Sombras(bool ligadas)
    {
        foreach (var mmi in _perto)
            mmi.CastShadow = ligadas ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private static MultiMeshInstance3D Campo(ArrayMesh forma, int n, RandomNumberGenerator rng, bool perto)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,       // tem de vir antes de InstanceCount
            Mesh = forma,
        };
        mm.InstanceCount = n;

        for (int i = 0; i < n; i++)
        {
            Vector3 pos;
            float escala;
            if (perto)
            {
                // Longe o bastante do leito para nunca cobrir uma nave.
                bool dentro = rng.Randf() < 0.35f;
                float off = dentro ? -rng.RandfRange(6.5f, 13f) : rng.RandfRange(6.5f, 40f);
                var q = Tracado.Quadro(rng.Randf(), off, 0f);
                float espalha = 0.8f + MathF.Abs(off) * 0.12f;
                pos = q.Origin + Vector3.Up * rng.RandfRange(-1f, 1f) * espalha * 2.2f;
                float longe = Mathf.Clamp((MathF.Abs(off) - 6.5f) / 30f, 0f, 1f);
                escala = Mathf.Lerp(0.14f, 1.9f, longe * longe) * rng.RandfRange(0.5f, 1.35f);

                var plano = new Vector2(pos.X, pos.Z);
                if (plano.Length() < Estacao.Raio + 4f)
                    pos += new Vector3(plano.X, 0f, plano.Y).Normalized() * 6f;
            }
            else
            {
                float a = rng.RandfRange(0f, Mathf.Tau);
                float r = rng.RandfRange(75f, 170f);
                pos = new Vector3(MathF.Cos(a) * r, rng.RandfRange(-28f, 22f), MathF.Sin(a) * r);
                escala = rng.RandfRange(2.5f, 9f);
            }

            var eixo = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f)).Normalized();
            var b = new Basis(eixo, rng.RandfRange(0f, Mathf.Tau)).Scaled(new Vector3(escala, escala, escala));
            mm.SetInstanceTransform(i, new Transform3D(b, pos));
            // Eixo e velocidade de giro, lidos pelo shader.
            mm.SetInstanceCustomData(i, new Color(rng.Randf(), rng.Randf(), rng.Randf(), rng.Randf()));
        }

        return new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = perto ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Icosfera deformada por ruído, achatada e com algumas crateras.</summary>
    private static ArrayMesh Rocha(int semente, RandomNumberGenerator rng)
    {
        var (vs, fs) = Geo.Icosfera(2);
        var ruido = new FastNoiseLite
        {
            Seed = semente * 31 + 7,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.9f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
        };
        var achatado = new Vector3(rng.RandfRange(0.75f, 1.3f), rng.RandfRange(0.55f, 0.95f), rng.RandfRange(0.75f, 1.3f));
        var crateras = Enumerable.Range(0, 3)
            .Select(_ => new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f)).Normalized())
            .ToArray();

        var m = new ConstrutorMalha();
        foreach (var v in vs)
        {
            float d = 1f + 0.36f * ruido.GetNoise3Dv(v * 1.4f);
            foreach (var c in crateras)
            {
                float k = v.Dot(c);
                if (k > 0.86f)
                    d -= Mathf.SmoothStep(0.86f, 1f, k) * 0.16f;
            }
            m.V(v * d * achatado, v);
        }
        foreach (var f in fs)
            m.Tri(f[0], f[1], f[2], vs[f[0]] + vs[f[1]] + vs[f[2]]);
        m.SuavizarNormais();
        return m.Construir();
    }
}
