// ÍRIS-9: estação esférica coberta de placas hexagonais de verdade (geometria,
// não textura), com uma abertura em diafragma no polo. Sem trincheira
// equatorial e sem canhão: a identidade é roxa, hexagonal e com íris.
//
// A varredura está desligada por enquanto (IRIS_EVENTO_ATIVO na versão 2D);
// aqui ela é cenário e só respira — o diafragma abre e fecha devagar e a luz
// do núcleo acompanha.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace OrbitalDerby.Mundo;

public partial class Estacao : Node3D
{
    // O tamanho e o lugar vêm do circuito: o ANEL DE ÍCARO a põe no miolo com
    // 16 m, o INTERLAGOS ORBITAL a joga para 74 m de altura com 24 m, porque o
    // miolo dele é pista. Os detalhes (balizas, aro da íris) continuam em
    // tamanho absoluto, então a estação maior ganha densidade de detalhe em vez
    // de virar a mesma bola esticada.
    private float Raio => Tracado.Atual.EstacaoRaio;

    /// <summary>Raio do circuito em uso. Só quem posiciona cenário precisa.</summary>
    public static float RaioAtual => Tracado.Atual.EstacaoRaio;
    private const float AnguloAbertura = 21f;   // graus, medidos a partir do polo da íris
    private const int Laminas = 9;

    private Node3D _corpo = null!;
    private readonly List<MeshInstance3D> _laminas = new();
    private StandardMaterial3D _matNucleo = null!;
    public OmniLight3D LuzNucleo { get; private set; } = null!;
    private readonly List<(StandardMaterial3D mat, float fase)> _balizas = new();
    private double _t;

    private float RaioBoca => Raio * MathF.Sin(Mathf.DegToRad(AnguloAbertura));
    private float AlturaBoca => Raio * MathF.Cos(Mathf.DegToRad(AnguloAbertura));

    public override void _Ready()
    {
        Position = Tracado.Atual.EstacaoPos;
        RotationDegrees = new Vector3(24f, 0f, 0f);   // íris inclinada para o lado da câmera
        _corpo = new Node3D();
        AddChild(_corpo);
        ConstruirPlacas();
        ConstruirIris();
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _corpo.RotateY((float)(0.02 * delta));

        // Respiração do diafragma: 0 fechado, 1 aberto.
        float abertura = 0.4f + 0.28f * MathF.Sin((float)_t * 0.45f);
        for (int i = 0; i < _laminas.Count; i++)
            _laminas[i].Rotation = new Vector3(0f, -abertura * 1.05f, 0f);
        _matNucleo.EmissionEnergyMultiplier = 5f + 9f * abertura;
        LuzNucleo.LightEnergy = 2.5f + 6f * abertura;

        foreach (var (mat, fase) in _balizas)
        {
            float pisca = MathF.Sin((float)_t * 2.6f + fase) > 0.82f ? 1f : 0f;
            mat.EmissionEnergyMultiplier = 0.3f + 7f * pisca;
        }
    }

    // -- casco -----------------------------------------------------------------

    private void ConstruirPlacas()
    {
        // Placas = poliedro dual de uma icosfera: cada vértice vira um hexágono
        // (e 12 pentágonos), com as faces vizinhas ordenadas em volta dele.
        var (vs, fs) = Geo.Icosfera(3);
        var vizinhas = new List<int>[vs.Count];
        for (int i = 0; i < vs.Count; i++)
            vizinhas[i] = new List<int>(6);
        for (int f = 0; f < fs.Count; f++)
            foreach (int v in fs[f])
                vizinhas[v].Add(f);
        var centros = fs.Select(f => ((vs[f[0]] + vs[f[1]] + vs[f[2]]) / 3f).Normalized()).ToArray();

        var rng = new RandomNumberGenerator { Seed = 99 };
        float limite = Mathf.DegToRad(AnguloAbertura);
        var cinza = new Color(0.34f, 0.3f, 0.46f);
        var roxo = new Color(0.5f, 0.38f, 0.85f);
        var placas = new ConstrutorMalha();

        for (int vi = 0; vi < vs.Count; vi++)
        {
            Vector3 n = vs[vi];
            if (n.AngleTo(Vector3.Up) < limite)
                continue;                                   // é ali que fica a íris

            Vector3 e1 = n.Cross(MathF.Abs(n.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
            Vector3 e2 = n.Cross(e1);
            var contorno = vizinhas[vi]
                .Select(f => centros[f])
                .OrderBy(c => MathF.Atan2((c - n).Dot(e2), (c - n).Dot(e1)))
                .ToList();

            float h = Raio * (1f + rng.RandfRange(0f, 0.016f));   // relevo entre placas
            bool janela = rng.Randf() < 0.07f;
            Color c0 = cinza.Lerp(roxo, rng.Randf() * 0.5f);
            float tom = rng.RandfRange(0.72f, 1.15f);
            var cor = new Color(c0.R * tom, c0.G * tom, c0.B * tom, janela ? 1f : 0f);
            var corLado = new Color(cor.R * 0.55f, cor.G * 0.55f, cor.B * 0.55f, 0f);

            var topo = contorno.Select(c => (n + (c - n) * 0.88f).Normalized() * h).ToList();
            var fundo = contorno.Select(c => (n + (c - n) * 0.88f).Normalized() * (Raio * 0.985f)).ToList();
            Vector3 centroTopo = n * h;

            int ic = placas.V(centroTopo, n, default, cor);
            var it = topo.Select(p => placas.V(p, n, default, cor)).ToArray();
            for (int k = 0; k < it.Length; k++)
                placas.Tri(ic, it[k], it[(k + 1) % it.Length], n);

            for (int k = 0; k < topo.Count; k++)
            {
                int k2 = (k + 1) % topo.Count;
                Vector3 fora = (topo[k] + topo[k2]) * 0.5f - centroTopo;
                placas.QuadPlano(topo[k], topo[k2], fundo[k2], fundo[k], fora, corLado);
            }

            if (!janela && rng.Randf() < 0.02f)
                Baliza(n * (h + 0.08f), rng);
        }

        _corpo.AddChild(new MeshInstance3D
        {
            Mesh = placas.Construir(),
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/estacao.gdshader") },
        });

        // Casco interno escuro, que aparece nas frestas — sem cobrir a abertura.
        var casca = new ConstrutorMalha();
        foreach (var f in fs)
        {
            Vector3 c = (vs[f[0]] + vs[f[1]] + vs[f[2]]).Normalized();
            if (c.AngleTo(Vector3.Up) < limite * 0.97f)
                continue;
            float r = Raio * 0.985f;
            casca.TriPlano(vs[f[0]] * r, vs[f[1]] * r, vs[f[2]] * r, c);
        }
        _corpo.AddChild(new MeshInstance3D
        {
            Mesh = casca.Construir(),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.03f, 0.025f, 0.05f),
                Metallic = 0.6f,
                Roughness = 0.7f,
            },
        });
    }

    private void Baliza(Vector3 pos, RandomNumberGenerator rng)
    {
        bool vermelha = rng.Randf() < 0.6f;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = Colors.Black,
            EmissionEnabled = true,
            Emission = vermelha ? new Color(1f, 0.15f, 0.12f) : new Color(1f, 0.95f, 0.9f),
            EmissionEnergyMultiplier = 1f,
        };
        _balizas.Add((mat, rng.RandfRange(0f, Mathf.Tau)));
        _corpo.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.11f, Height = 0.22f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = mat,
            Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // -- íris ------------------------------------------------------------------

    private void ConstruirIris()
    {
        float rb = RaioBoca;
        float hb = AlturaBoca;
        var metal = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.13f, 0.24f),
            Metallic = 0.88f,
            Roughness = 0.28f,
        };

        // Garganta: o poço que desce da abertura até o núcleo.
        float yNucleo = hb - rb * 1.15f;
        AddChild(new MeshInstance3D { Mesh = Garganta(rb * 0.97f, hb, yNucleo - rb * 0.3f), MaterialOverride = metal });

        // Aro da abertura, com um filete aceso.
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = rb - 0.15f, OuterRadius = rb + 0.4f, Rings = 64, RingSegments = 12 },
            MaterialOverride = metal,
            Position = new Vector3(0f, hb, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = rb + 0.18f, OuterRadius = rb + 0.26f, Rings = 64, RingSegments = 6 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Colors.Black,
                EmissionEnabled = true,
                Emission = Paleta.Estacao,
                EmissionEnergyMultiplier = 4f,
            },
            Position = new Vector3(0f, hb + 0.3f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // Lâminas do diafragma: cada uma gira em torno de um pivô no aro, como
        // numa íris de câmera. Fechadas, as pontas se cruzam no centro.
        var matLamina = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.17f, 0.3f),
            Metallic = 0.9f,
            Roughness = 0.24f,
        };
        var malhaLamina = Lamina(rb);
        for (int i = 0; i < Laminas; i++)
        {
            float fi = Mathf.Tau * i / Laminas;
            var radial = new Vector3(MathF.Cos(fi), 0f, MathF.Sin(fi));
            var tangente = new Vector3(-MathF.Sin(fi), 0f, MathF.Cos(fi));
            var pivo = new Node3D
            {
                Transform = new Transform3D(
                    new Basis(tangente, Vector3.Up, -radial),
                    radial * rb * 0.94f + Vector3.Up * (hb - 0.45f + i * 0.012f)),
            };
            AddChild(pivo);
            var lamina = new MeshInstance3D { Mesh = malhaLamina, MaterialOverride = matLamina };
            pivo.AddChild(lamina);
            _laminas.Add(lamina);
        }

        // Núcleo aceso lá no fundo, e a luz que escapa pela abertura.
        _matNucleo = new StandardMaterial3D
        {
            AlbedoColor = Colors.Black,
            EmissionEnabled = true,
            Emission = new Color(0.66f, 0.46f, 1f),
            EmissionEnergyMultiplier = 8f,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = rb * 0.8f, Height = rb * 1.6f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = _matNucleo,
            Position = new Vector3(0f, yNucleo, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        LuzNucleo = new OmniLight3D
        {
            LightColor = new Color(0.66f, 0.5f, 1f),
            LightEnergy = 5f,
            OmniRange = Raio * 6f,
            ShadowEnabled = true,
            LightVolumetricFogEnergy = 4f,
            Position = new Vector3(0f, yNucleo + rb * 0.9f, 0f),
        };
        AddChild(LuzNucleo);
    }

    private static ArrayMesh Garganta(float raio, float yTopo, float yFundo)
    {
        const int lados = 48;
        var m = new ConstrutorMalha();
        var topo = new int[lados + 1];
        var fundo = new int[lados + 1];
        for (int j = 0; j <= lados; j++)
        {
            float a = Mathf.Tau * j / lados;
            var dir = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            topo[j] = m.V(dir * raio + Vector3.Up * yTopo, -dir, new Vector2((float)j / lados, 0f));
            fundo[j] = m.V(dir * raio + Vector3.Up * yFundo, -dir, new Vector2((float)j / lados, 1f));
        }
        for (int j = 0; j < lados; j++)
        {
            float a = Mathf.Tau * (j + 0.5f) / lados;
            var paraDentro = -new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            m.Quad(topo[j], fundo[j], fundo[j + 1], topo[j + 1], paraDentro);
        }
        return m.Construir();
    }

    /// <summary>Lâmina curva em forma de foice, com o pivô na origem e a ponta em +Z.</summary>
    private static ArrayMesh Lamina(float raioBoca)
    {
        const int passos = 10;
        float comprimento = raioBoca * 1.05f;
        var esquerda = new List<Vector2>();
        var direita = new List<Vector2>();
        for (int i = 0; i <= passos; i++)
        {
            float s = (float)i / passos;
            float x = 0.35f * comprimento * s * s;              // a lâmina curva de lado
            float z = comprimento * s;
            float largura = raioBoca * (0.46f * (1f - s) + 0.07f);
            esquerda.Add(new Vector2(x - largura * 0.5f, z));
            direita.Add(new Vector2(x + largura * 0.5f, z));
        }
        direita.Reverse();
        var contorno = esquerda.Concat(direita).ToArray();

        var m = new ConstrutorMalha();
        Geo.Placa(m, contorno, 0.05f, Transform3D.Identity);
        return m.Construir();
    }
}
