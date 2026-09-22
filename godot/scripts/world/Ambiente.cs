// Céu procedural, a estrela distante e o pós-processamento que dá o
// acabamento: bloom em HDR, oclusão de ambiente, reflexos em tela e uma névoa
// volumétrica leve — é ela que torna visível o feixe de luz saindo da íris.

using Godot;

namespace OrbitalDerby.Mundo;

public partial class Ambiente : Node3D
{
    /// <summary>De onde vem a luz. O céu desenha o disco da estrela nessa mesma direção.</summary>
    public static readonly Vector3 DirecaoSol = new Vector3(-0.55f, 0.42f, -0.72f).Normalized();

    public WorldEnvironment Mundo { get; private set; } = null!;
    public DirectionalLight3D Sol { get; private set; } = null!;
    public DirectionalLight3D Preenchimento { get; private set; } = null!;

    public override void _Ready()
    {
        var ceu = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ceu.gdshader") };
        ceu.SetShaderParameter("direcao_sol", DirecaoSol);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = ceu,
                RadianceSize = Sky.RadianceSizeEnum.Size512,
                ProcessMode = Sky.ProcessModeEnum.Quality,
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.55f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,

            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.05f,
            TonemapWhite = 6f,

            GlowEnabled = true,
            GlowIntensity = 0.85f,
            GlowStrength = 1.05f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,

            SsaoEnabled = true,
            SsaoRadius = 1.4f,
            SsaoIntensity = 1.6f,
            SsrEnabled = true,
            SsrMaxSteps = 48,

            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.0028f,
            VolumetricFogAlbedo = new Color(0.55f, 0.55f, 0.7f),
            VolumetricFogAnisotropy = 0.55f,
            VolumetricFogLength = 240f,

            AdjustmentEnabled = true,
            AdjustmentContrast = 1.08f,
            AdjustmentSaturation = 1.12f,
        };
        float[] niveisBrilho = { 0.2f, 0.6f, 1f, 1f, 0.7f, 0.4f, 0.2f };
        for (int i = 0; i < niveisBrilho.Length; i++)
            env.SetGlowLevel(i, niveisBrilho[i]);

        Mundo = new WorldEnvironment { Environment = env };
        AddChild(Mundo);

        Sol = new DirectionalLight3D
        {
            LightColor = new Color(1f, 0.93f, 0.84f),
            LightEnergy = 1.7f,
            LightAngularDistance = 0.6f,         // sombra de borda macia
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 380f,   // a volta agora tem 388 m
            LightVolumetricFogEnergy = 0.12f,    // quem ilumina a névoa é a íris, não o sol
        };
        AddChild(Sol);
        Sol.LookAt(-DirecaoSol, Vector3.Up);

        // Preenchimento: o reflexo fraco da nebulosa, vindo do lado oposto ao
        // sol e um pouco de baixo. Sem ele a face noturna da estação e das
        // pedras vira um recorte preto chapado.
        Preenchimento = new DirectionalLight3D
        {
            LightColor = new Color(0.5f, 0.46f, 0.9f),
            LightEnergy = 0.2f,
            LightSpecular = 0.25f,
            ShadowEnabled = false,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            LightVolumetricFogEnergy = 0f,
        };
        AddChild(Preenchimento);
        var deOnde = new Vector3(-DirecaoSol.X, -0.25f, -DirecaoSol.Z).Normalized();
        Preenchimento.LookAt(-deOnde, Vector3.Up);
    }

    public enum Qualidade { Alta, Media, Baixa }

    public static string Nome(Qualidade q) => q switch
    {
        Qualidade.Alta => "alta",
        Qualidade.Media => "média",
        _ => "baixa",
    };

    /// <summary>
    /// Alta liga tudo. Média tira a névoa volumétrica, o antisserrilhado pesado
    /// e metade das cascatas de sombra. Baixa desliga também reflexos e oclusão.
    /// </summary>
    public void Aplicar(Qualidade q)
    {
        var env = Mundo.Environment;
        env.GlowIntensity = 0.6f;
        env.GlowStrength = 1f;
        env.GlowBloom = 0.02f;
        env.GlowHdrThreshold = 1.5f;
        env.VolumetricFogEnabled = q == Qualidade.Alta;
        env.SsrEnabled = q != Qualidade.Baixa;
        env.SsaoEnabled = q != Qualidade.Baixa;
        Sol.DirectionalShadowMode = q == Qualidade.Alta
            ? DirectionalLight3D.ShadowMode.Parallel4Splits
            : DirectionalLight3D.ShadowMode.Parallel2Splits;
        RenderingServer.DirectionalShadowAtlasSetSize(q == Qualidade.Alta ? 8192 : 4096, true);
        GetViewport().Msaa3D = q switch
        {
            Qualidade.Alta => Viewport.Msaa.Msaa4X,
            Qualidade.Media => Viewport.Msaa.Msaa2X,
            _ => Viewport.Msaa.Disabled,
        };
    }
}
