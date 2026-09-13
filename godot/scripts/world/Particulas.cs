// Fábrica de partículas: exaustão dos motores, fumaça, faíscas, explosões.
//
// Tudo procedural: a "textura" de cada partícula é um gradiente radial gerado
// em código. As rajadas únicas se apagam sozinhas depois de terminarem.

using System;
using Godot;

namespace OrbitalDerby.Mundo;

public static class Particulas
{
    private static Texture2D? _suave;
    private static StandardMaterial3D? _aditivo;
    private static StandardMaterial3D? _alfa;

    private static readonly Aabb CaixaGrande = new(new Vector3(-40, -40, -40), new Vector3(80, 80, 80));

    private static Texture2D Suave()
    {
        if (_suave is not null) return _suave;
        var g = new Gradient
        {
            Offsets = new[] { 0f, 0.35f, 1f },
            Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0) },
        };
        _suave = new GradientTexture2D
        {
            Gradient = g,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 64,
        };
        return _suave;
    }

    private static StandardMaterial3D Material(bool aditivo)
    {
        if (aditivo && _aditivo is not null) return _aditivo;
        if (!aditivo && _alfa is not null) return _alfa;
        var m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = aditivo ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = Suave(),
        };
        if (aditivo) _aditivo = m; else _alfa = m;
        return m;
    }

    private static GradientTexture1D Rampa(params (float t, Color c)[] pontos)
    {
        var offs = new float[pontos.Length];
        var cores = new Color[pontos.Length];
        for (int i = 0; i < pontos.Length; i++)
        {
            offs[i] = pontos[i].t;
            cores[i] = pontos[i].c;
        }
        return new GradientTexture1D { Gradient = new Gradient { Offsets = offs, Colors = cores } };
    }

    private static CurveTexture Curva(params Vector2[] pontos)
    {
        var c = new Curve();
        foreach (var p in pontos) c.AddPoint(p);
        return new CurveTexture { Curve = c };
    }

    private static Color Hdr(Color c, float e) => new(c.R * e, c.G * e, c.B * e, c.A);
    private static Color Transparente(Color c) => new(c.R, c.G, c.B, 0f);

    private static GpuParticles3D Emissor(int quantidade, double vida, ParticleProcessMaterial pm, float tamanho, bool aditivo) =>
        new()
        {
            Amount = quantidade,
            Lifetime = vida,
            ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = new Vector2(tamanho, tamanho) },
            MaterialOverride = Material(aditivo),
            LocalCoords = false,
            VisibilityAabb = CaixaGrande,
            FixedFps = 0,        // o padrão (30 Hz) soltava o rastro em "contas" separadas
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

    // -- contínuas (presas à nave) ---------------------------------------------------

    /// <summary>Jato do motor: rastro que fica para trás enquanto a nave anda.</summary>
    public static GpuParticles3D Exaustao(Color cor, float largura)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(largura, 0.04f, 0.45f),   // alongada: preenche o vão entre frames
            Direction = new Vector3(0, 0, 1),
            Spread = 5f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 5f,
            Gravity = Vector3.Zero,
            DampingMin = 2f,
            DampingMax = 3f,
            ScaleMin = 0.5f,
            ScaleMax = 0.9f,
            ScaleCurve = Curva(new Vector2(0f, 0.5f), new Vector2(0.2f, 1f), new Vector2(1f, 0.1f)),
            ColorRamp = Rampa((0f, Hdr(new Color(1f, 0.97f, 0.9f), 1.2f)), (0.25f, Hdr(cor, 0.9f)), (1f, Transparente(Hdr(cor, 0.6f)))),
        };
        return Emissor(300, 0.5, pm, 0.28f, aditivo: true);
    }

    /// <summary>Fumaça escura saindo do motor (superaquecido ou atingido pelo tiro).</summary>
    public static GpuParticles3D FumacaContinua()
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.15f,
            Direction = new Vector3(0, 0.4f, 1),
            Spread = 25f,
            InitialVelocityMin = 0.6f,
            InitialVelocityMax = 1.6f,
            Gravity = new Vector3(0, 0.7f, 0),
            DampingMin = 0.5f,
            DampingMax = 1f,
            ScaleMin = 0.7f,
            ScaleMax = 1.3f,
            ScaleCurve = Curva(new Vector2(0f, 0.3f), new Vector2(1f, 1.6f)),
            ColorRamp = Rampa((0f, new Color(0.25f, 0.23f, 0.22f, 0f)), (0.15f, new Color(0.22f, 0.2f, 0.2f, 0.55f)), (1f, new Color(0.12f, 0.12f, 0.13f, 0f))),
        };
        return Emissor(46, 1.6, pm, 0.9f, aditivo: false);
    }

    // -- rajadas únicas ---------------------------------------------------------------

    private static void Disparar(Node pai, GpuParticles3D p, Vector3 pos)
    {
        p.OneShot = true;
        p.Explosiveness = 0.95f;
        p.Position = pos;
        pai.AddChild(p);
        p.Emitting = true;
        var cronometro = pai.GetTree().CreateTimer(p.Lifetime + 0.6);
        cronometro.Timeout += () => { if (GodotObject.IsInstanceValid(p)) p.QueueFree(); };
    }

    /// <summary>Faíscas rápidas espalhadas em esfera.</summary>
    public static void Faiscas(Node pai, Vector3 pos, Color cor, int quantidade, float velocidade)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.2f,
            Direction = new Vector3(0, 1, 0),
            Spread = 180f,
            InitialVelocityMin = velocidade * 0.4f,
            InitialVelocityMax = velocidade,
            Gravity = new Vector3(0, -3f, 0),
            DampingMin = 2f,
            DampingMax = 4f,
            ScaleMin = 0.35f,
            ScaleMax = 0.8f,
            ScaleCurve = Curva(new Vector2(0f, 1f), new Vector2(1f, 0f)),
            ColorRamp = Rampa((0f, Hdr(new Color(1f, 0.95f, 0.85f), 4f)), (0.3f, Hdr(cor, 3f)), (1f, Transparente(cor))),
        };
        Disparar(pai, Emissor(quantidade, 0.6, pm, 0.22f, aditivo: true), pos);
    }

    /// <summary>Nuvem que se abre devagar: caixa vazia, superaquecimento.</summary>
    public static void Nuvem(Node pai, Vector3 pos, Color cor, int quantidade)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.4f,
            Direction = new Vector3(0, 1, 0),
            Spread = 180f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 2.2f,
            Gravity = new Vector3(0, 0.5f, 0),
            DampingMin = 1.5f,
            DampingMax = 2.5f,
            ScaleMin = 0.8f,
            ScaleMax = 1.4f,
            ScaleCurve = Curva(new Vector2(0f, 0.4f), new Vector2(1f, 1.8f)),
            ColorRamp = Rampa((0f, new Color(cor.R, cor.G, cor.B, 0f)), (0.1f, new Color(cor.R, cor.G, cor.B, 0.6f)), (1f, new Color(cor.R, cor.G, cor.B, 0f))),
        };
        Disparar(pai, Emissor(quantidade, 1.4, pm, 1.0f, aditivo: false), pos);
    }

    /// <summary>Explosão da bomba: bola de fogo, fumaça e estilhaços.</summary>
    public static void Explosao(Node pai, Vector3 pos, Color cor, float tamanho)
    {
        var fogo = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.3f * tamanho,
            Direction = new Vector3(0, 1, 0),
            Spread = 180f,
            InitialVelocityMin = 2.5f * tamanho,
            InitialVelocityMax = 7f * tamanho,
            Gravity = Vector3.Zero,
            DampingMin = 4f,
            DampingMax = 6f,
            ScaleMin = 0.9f,
            ScaleMax = 1.6f,
            ScaleCurve = Curva(new Vector2(0f, 0.5f), new Vector2(0.25f, 1.4f), new Vector2(1f, 0f)),
            AngularVelocityMin = -90f,
            AngularVelocityMax = 90f,
            ColorRamp = Rampa(
                (0f, Hdr(new Color(1f, 0.95f, 0.8f), 6f)),
                (0.18f, Hdr(new Color(1f, 0.65f, 0.25f), 4f)),
                (0.5f, Hdr(new Color(0.9f, 0.25f, 0.08f), 1.6f)),
                (1f, new Color(0.3f, 0.05f, 0.02f, 0f))),
        };
        Disparar(pai, Emissor(56, 0.9, fogo, 1.1f * tamanho, aditivo: true), pos);

        var fumaca = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.5f * tamanho,
            Direction = new Vector3(0, 1, 0),
            Spread = 180f,
            InitialVelocityMin = 1f,
            InitialVelocityMax = 3.5f * tamanho,
            Gravity = new Vector3(0, 0.6f, 0),
            DampingMin = 2f,
            DampingMax = 3f,
            ScaleMin = 1f,
            ScaleMax = 1.8f,
            ScaleCurve = Curva(new Vector2(0f, 0.3f), new Vector2(1f, 2.2f)),
            ColorRamp = Rampa((0f, new Color(0.1f, 0.09f, 0.09f, 0f)), (0.2f, new Color(0.14f, 0.12f, 0.12f, 0.7f)), (1f, new Color(0.08f, 0.08f, 0.09f, 0f))),
        };
        Disparar(pai, Emissor(30, 2.0, fumaca, 1.4f * tamanho, aditivo: false), pos);

        Faiscas(pai, pos, cor, 60, 16f * tamanho);
    }
}
