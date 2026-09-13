// Uma nave em 3D, gerada por código. ÍON: casco angular, facetado, asas em
// delta. ÍGNIS: fuselagem longa e lisa, asas curtas e deriva dupla.
//
// Tudo o que aparece nela lê o estado da regra (Core.Nave): a chama e a luz do
// motor seguem o PWM, a fumaça aparece no superaquecimento ou com o teto
// reduzido, a bolha aparece com o Escudo. Nada daqui volta para a regra.

using System;
using System.Collections.Generic;
using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby.Mundo;

public partial class NaveVisual : Node3D
{
    public int Lane { get; private set; }

    private Node3D _corpo = null!;              // recebe balanço, arfagem e tremor
    private Color _cor;
    private Vector3[] _bocais = Array.Empty<Vector3>();
    private readonly List<MeshInstance3D> _chamas = new();
    private ShaderMaterial _matChama = null!;
    private StandardMaterial3D _matBocal = null!;
    private GpuParticles3D _exaustao = null!;
    private GpuParticles3D _fumaca = null!;
    private OmniLight3D _luzMotor = null!;
    private MeshInstance3D _escudo = null!;
    private ShaderMaterial _matEscudo = null!;
    private Vector3 _escalaEscudo;
    private readonly List<(StandardMaterial3D mat, float fase, bool estrobo)> _luzesNav = new();

    private float _pwmSuave, _arfagem, _escudoVisivel, _impactoEscudo;
    private double _t;

    public NaveVisual() { }
    public NaveVisual(int lane) { Lane = lane; }

    public override void _Ready()
    {
        _cor = Paleta.DoJogador(Lane);
        _corpo = new Node3D { Scale = Vector3.One * 1.25f };
        AddChild(_corpo);
        if (Lane == 0) ConstruirIon(); else ConstruirIgnis();
        ConstruirMotor();
        ConstruirEscudo();
    }

    // -- materiais ---------------------------------------------------------------

    private StandardMaterial3D Pintura() => new()
    {
        AlbedoColor = _cor.Darkened(0.12f),
        Metallic = 0.55f,
        Roughness = 0.3f,
        ClearcoatEnabled = true,
        Clearcoat = 0.8f,
        ClearcoatRoughness = 0.08f,
    };

    private static StandardMaterial3D Grafite() => new()
    {
        AlbedoColor = new Color(0.1f, 0.11f, 0.13f),
        Metallic = 0.85f,
        Roughness = 0.38f,
    };

    private static StandardMaterial3D Vidro() => new()
    {
        AlbedoColor = new Color(0.03f, 0.06f, 0.1f),
        Metallic = 0.9f,
        Roughness = 0.04f,
        EmissionEnabled = true,
        Emission = new Color(0.2f, 0.5f, 0.8f),
        EmissionEnergyMultiplier = 0.35f,
    };

    private StandardMaterial3D Filete() => new()
    {
        AlbedoColor = Colors.Black,
        EmissionEnabled = true,
        Emission = _cor,
        EmissionEnergyMultiplier = 3.5f,
    };

    private void Parte(ConstrutorMalha m, Material mat, bool sombra = true) =>
        _corpo.AddChild(new MeshInstance3D
        {
            Mesh = m.Construir(),
            MaterialOverride = mat,
            CastShadow = sombra ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });

    // -- ÍON ---------------------------------------------------------------------

    private void ConstruirIon()
    {
        // Seções hexagonais com sombreamento plano: casco angular, facetado.
        var fus = new ConstrutorMalha();
        Geo.Loft(fus, new[]
        {
            new Secao(-1.35f, 0.02f, 0.015f, 0.02f),
            new Secao(-0.95f, 0.2f, 0.13f, 0.03f),
            new Secao(-0.25f, 0.36f, 0.24f, 0.05f),
            new Secao(0.55f, 0.4f, 0.26f, 0.04f),
            new Secao(1.1f, 0.32f, 0.2f, 0.02f),
            new Secao(1.22f, 0.28f, 0.17f, 0.02f),
        }, 6, plano: true);
        Parte(fus, Pintura());

        // Asas em delta, com leve caída nas pontas.
        Vector2[] asa = { new(0.3f, -0.45f), new(1.5f, 0.78f), new(1.42f, 1.02f), new(0.32f, 0.95f) };
        Asas(asa, 0.07f, 0.08f, -0.02f);

        Cabine(-0.75f, 0.35f, 0.14f, 0.2f);
        Bocais(new[] { new Vector3(-0.18f, 0.02f, 1.2f), new Vector3(0.18f, 0.02f, 1.2f) }, 0.13f, 0.22f);
        Derivas(0.32f, 0.7f, 1.15f, 0.36f, 0.26f);
        LuzesNavegacao(new Vector3(-1.46f, -0.12f, 0.9f), new Vector3(1.46f, -0.12f, 0.9f), new Vector3(0f, 0.3f, 1.1f));
    }

    // -- ÍGNIS -------------------------------------------------------------------

    private void ConstruirIgnis()
    {
        // Fuselagem longa e lisa: muitos lados, normais suaves.
        var fus = new ConstrutorMalha();
        Geo.Loft(fus, new[]
        {
            new Secao(-1.95f, 0.02f, 0.02f, 0f),
            new Secao(-1.5f, 0.12f, 0.1f, 0.01f),
            new Secao(-0.9f, 0.22f, 0.18f, 0.02f),
            new Secao(-0.1f, 0.28f, 0.23f, 0.03f),
            new Secao(0.8f, 0.3f, 0.24f, 0.03f),
            new Secao(1.35f, 0.26f, 0.2f, 0.02f),
            new Secao(1.55f, 0.23f, 0.18f, 0.02f),
        }, 18, plano: false);
        Parte(fus, Pintura());

        Vector2[] asa = { new(0.26f, 0.1f), new(1.0f, 0.8f), new(0.95f, 1.05f), new(0.26f, 1.0f) };
        Asas(asa, 0.06f, 0.05f, 0f);

        Cabine(-1.35f, -0.3f, 0.12f, 0.19f);
        Bocais(new[] { new Vector3(0f, 0.02f, 1.52f) }, 0.21f, 0.26f);
        Derivas(0.2f, 0.9f, 1.5f, 0.5f, 0.35f);

        // Tomadas de ar laterais.
        var tomadas = new ConstrutorMalha();
        foreach (float lado in new[] { -1f, 1f })
        {
            var xf = new Transform3D(Basis.Identity, new Vector3(0.3f * lado, -0.03f, -0.4f));
            Vector2[] caixa = { new(-0.06f, -0.25f), new(0.06f, -0.25f), new(0.06f, 0.25f), new(-0.06f, 0.25f) };
            Geo.Placa(tomadas, caixa, 0.14f, xf);
        }
        Parte(tomadas, Grafite());

        LuzesNavegacao(new Vector3(-1.0f, -0.05f, 0.95f), new Vector3(1.0f, -0.05f, 0.95f), new Vector3(0f, 0.55f, 1.45f));
    }

    // -- peças comuns ---------------------------------------------------------------

    private void Asas(Vector2[] direita, float espessura, float caida, float y)
    {
        var asas = new ConstrutorMalha();
        var filetes = new ConstrutorMalha();
        foreach (float lado in new[] { 1f, -1f })
        {
            var contorno = new Vector2[direita.Length];
            for (int i = 0; i < direita.Length; i++)
                contorno[i] = new Vector2(direita[i].X * lado, direita[i].Y);
            var xf = new Transform3D(new Basis(Vector3.Back, -caida * lado), new Vector3(0f, y, 0f));
            Geo.Placa(asas, contorno, espessura, xf);

            // Filete aceso no bordo de ataque, na cor do jogador.
            var bordo = new List<Vector3>
            {
                xf * new Vector3(contorno[0].X, espessura * 0.4f, contorno[0].Y),
                xf * new Vector3(contorno[1].X, espessura * 0.4f, contorno[1].Y),
            };
            Geo.Tubo(filetes, bordo, 0.022f, 6, fechado: false);
        }
        Parte(asas, Grafite());
        Parte(filetes, Filete(), sombra: false);
    }

    private void Cabine(float zIni, float zFim, float largura, float y)
    {
        float meio = (zIni + zFim) * 0.5f;
        var m = new ConstrutorMalha();
        Geo.Loft(m, new[]
        {
            new Secao(zIni, 0.02f, 0.015f, y - 0.02f),
            new Secao(Mathf.Lerp(zIni, meio, 0.45f), largura * 0.8f, 0.07f, y),
            new Secao(meio, largura, 0.1f, y + 0.02f),
            new Secao(zFim, largura * 0.35f, 0.03f, y - 0.01f),
        }, 14, plano: false);
        Parte(m, Vidro());
    }

    private void Bocais(Vector3[] posicoes, float raio, float comprimento)
    {
        _bocais = posicoes;
        _matBocal = new StandardMaterial3D
        {
            AlbedoColor = Colors.Black,
            EmissionEnabled = true,
            Emission = _cor,
            EmissionEnergyMultiplier = 2f,
        };
        var carcaca = new ConstrutorMalha();
        var brasa = new ConstrutorMalha();
        foreach (var p in posicoes)
        {
            Geo.Loft(carcaca, new[]
            {
                new Secao(p.Z - comprimento, raio * 0.8f, raio * 0.8f, p.Y),
                new Secao(p.Z - comprimento * 0.3f, raio, raio, p.Y),
                new Secao(p.Z, raio * 0.92f, raio * 0.92f, p.Y),
            }, 16, plano: false);
            // Desloca a carcaça para o X do bocal (o loft nasce centrado em X=0).
            Deslocar(carcaca, p.X);
            Geo.Loft(brasa, new[]
            {
                new Secao(p.Z - 0.02f, raio * 0.78f, raio * 0.78f, p.Y),
                new Secao(p.Z + 0.01f, raio * 0.78f, raio * 0.78f, p.Y),
            }, 16, plano: false);
            Deslocar(brasa, p.X);
        }
        Parte(carcaca, Grafite());
        Parte(brasa, _matBocal, sombra: false);
    }

    private static void Deslocar(ConstrutorMalha m, float dx)
    {
        // Os lofts de um mesmo construtor precisam ser deslocados logo depois de
        // criados; como só há um bocal fora do eixo por vez, basta mover os
        // vértices criados desde a última chamada.
        m.DeslocarDesde(m.MarcaDeslocamento, new Vector3(dx, 0f, 0f));
        m.MarcaDeslocamento = m.Contagem;
    }

    private void Derivas(float x, float zIni, float zFim, float altura, float inclinacao)
    {
        var m = new ConstrutorMalha();
        foreach (float lado in new[] { -1f, 1f })
        {
            Vector2[] perfil = { new(0f, zIni), new(0f, zFim), new(altura, zFim + 0.08f), new(altura * 0.8f, zIni + (zFim - zIni) * 0.55f) };
            var b = new Basis(Vector3.Back, Mathf.Pi / 2f - inclinacao * lado);
            Geo.Placa(m, perfil, 0.05f, new Transform3D(b, new Vector3(x * lado, 0.12f, 0f)));
        }
        Parte(m, Pintura());
    }

    private void LuzesNavegacao(Vector3 esquerda, Vector3 direita, Vector3 cauda)
    {
        void Luz(Vector3 pos, Color cor, float fase, bool estrobo)
        {
            var mat = new StandardMaterial3D
            {
                AlbedoColor = Colors.Black,
                EmissionEnabled = true,
                Emission = cor,
                EmissionEnergyMultiplier = 3f,
            };
            _luzesNav.Add((mat, fase, estrobo));
            _corpo.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = mat,
                Position = pos,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        Luz(esquerda, new Color(1f, 0.1f, 0.08f), 0f, false);
        Luz(direita, new Color(0.1f, 1f, 0.3f), 0f, false);
        Luz(cauda, new Color(1f, 1f, 1f), Lane * 0.6f, true);
    }

    private void ConstruirMotor()
    {
        _matChama = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/chama.gdshader") };
        _matChama.SetShaderParameter("cor", _cor);
        var cone = Cone();
        Vector3 centro = Vector3.Zero;
        foreach (var b in _bocais)
        {
            centro += b;
            var chama = new MeshInstance3D
            {
                Mesh = cone,
                MaterialOverride = _matChama,
                Position = b,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _corpo.AddChild(chama);
            _chamas.Add(chama);
        }
        centro /= Math.Max(1, _bocais.Length);

        _exaustao = Particulas.Exaustao(_cor, _bocais.Length > 1 ? 0.2f : 0.1f);
        _exaustao.Position = centro + Vector3.Back * 0.55f;    // a caixa de emissão começa no bocal
        _corpo.AddChild(_exaustao);

        _fumaca = Particulas.FumacaContinua();
        _fumaca.Position = centro;
        _fumaca.Emitting = false;
        _corpo.AddChild(_fumaca);

        _luzMotor = new OmniLight3D
        {
            LightColor = _cor,
            OmniRange = 4.5f,
            LightEnergy = 0.5f,
            Position = centro + Vector3.Back * 0.5f,
        };
        _corpo.AddChild(_luzMotor);
    }

    /// <summary>Cone unitário ao longo de +Z: base no bocal (UV.y = 0), ponta em z = 1 (UV.y = 1).</summary>
    private static ArrayMesh Cone()
    {
        const int lados = 16;
        var m = new ConstrutorMalha();
        int ponta = m.V(new Vector3(0f, 0f, 1f), Vector3.Back, new Vector2(0.5f, 1f));
        var base_ = new int[lados + 1];
        for (int j = 0; j <= lados; j++)
        {
            float a = Mathf.Tau * j / lados;
            var d = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
            base_[j] = m.V(d, d, new Vector2((float)j / lados, 0f));
        }
        for (int j = 0; j < lados; j++)
            m.Tri(base_[j], base_[j + 1], ponta, m.NormalDoVertice(base_[j]) + m.NormalDoVertice(base_[j + 1]));
        return m.Construir();
    }

    private void ConstruirEscudo()
    {
        _matEscudo = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/escudo.gdshader") };
        _matEscudo.SetShaderParameter("cor", Paleta.DoItem(Item.Escudo));
        _escalaEscudo = Lane == 0 ? new Vector3(1.75f, 0.9f, 1.75f) : new Vector3(1.4f, 0.95f, 2.25f);
        _escudo = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = _matEscudo,
            Scale = _escalaEscudo,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _corpo.AddChild(_escudo);
    }

    // -- a cada frame --------------------------------------------------------------

    /// <summary>O bloqueio acende a bolha inteira por um instante.</summary>
    public void AcenderEscudo() => _impactoEscudo = 1f;

    public void Sincronizar(Nave nave, double dt)
    {
        _t += dt;
        float f = (float)dt;
        float t = (float)_t;

        Transform = Tracado.Quadro(nave.T, Tracado.OffsetDaFaixa(Lane), Tracado.AlturaVoo);

        _pwmSuave = Mathf.Lerp(_pwmSuave, (float)nave.Pwm, 1f - MathF.Exp(-f * 10f));
        float balanco = MathF.Sin(t * 2.1f + Lane * 1.7f) * 0.05f;
        float arfagemAlvo = -(float)nave.Pwm * 0.05f + (nave.Atordoado > 0 ? 0.1f : 0f);
        _arfagem = Mathf.Lerp(_arfagem, arfagemAlvo, 1f - MathF.Exp(-f * 4f));
        float rolagem = MathF.Sin(t * 1.3f + Lane) * 0.02f;
        if (nave.Atordoado > 0)
            rolagem += MathF.Sin(t * 19f) * 0.12f;           // bomba: nave chacoalhando sem motor

        // Tremor de impacto enquanto o flash da regra dura.
        float tremor = (float)nave.Flash * 0.35f;
        var sacode = new Vector3(MathF.Sin(t * 83f), MathF.Sin(t * 71f + 1f), MathF.Sin(t * 59f + 2f)) * tremor;
        _corpo.Position = new Vector3(0f, balanco, 0f) + sacode;
        _corpo.Rotation = new Vector3(_arfagem, 0f, rolagem);

        // Motor: chama, partículas, luz e brasa do bocal, tudo pelo PWM.
        bool quente = nave.Superaquecimento > 0;
        foreach (var chama in _chamas)
        {
            chama.Visible = _pwmSuave > 0.02f;
            float w = 0.85f + _pwmSuave * 0.35f;
            chama.Scale = new Vector3(w * 0.14f, w * 0.14f, 0.25f + _pwmSuave * 2.6f);
        }
        _matChama.SetShaderParameter("intensidade", 0.25f + _pwmSuave * 1.0f);
        _exaustao.Emitting = _pwmSuave > 0.03f;
        _exaustao.AmountRatio = Mathf.Clamp(_pwmSuave * 1.6f, 0.05f, 1f);
        _luzMotor.LightEnergy = 0.3f + _pwmSuave * 3f;
        _matBocal.Emission = quente ? new Color(1f, 0.28f, 0.1f) : _cor;
        _matBocal.EmissionEnergyMultiplier = 1f + _pwmSuave * 3.5f + (quente ? 4f : 0f);
        _fumaca.Emitting = quente || nave.Lento > 0;

        // Escudo: forma com um leve estufar, e acende no bloqueio.
        _escudoVisivel = Mathf.MoveToward(_escudoVisivel, nave.Escudo ? 1f : 0f, f * 3f);
        _impactoEscudo = Mathf.MoveToward(_impactoEscudo, 0f, f * 2.2f);
        _escudo.Visible = _escudoVisivel > 0.01f || _impactoEscudo > 0.01f;
        float estufa = 0.8f + 0.2f * _escudoVisivel + 0.12f * _impactoEscudo;
        _escudo.Scale = _escalaEscudo * estufa;
        _matEscudo.SetShaderParameter("forca", _escudoVisivel);
        _matEscudo.SetShaderParameter("impacto", _impactoEscudo);

        foreach (var (mat, fase, estrobo) in _luzesNav)
        {
            bool acesa = estrobo
                ? Mathf.PosMod(t + fase, 1.2f) < 0.07f
                : MathF.Sin(t * 3.2f + fase) > 0.2f;
            mat.EmissionEnergyMultiplier = acesa ? 6f : 0.2f;
        }
    }
}
