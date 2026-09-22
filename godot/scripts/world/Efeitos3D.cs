// Animações de efeito no mundo 3D — implementa Core.IEfeitos.
//
// Nada aqui decide jogo. Os projéteis seguem a lista oficial da regra
// (Corrida.EmVoo): enquanto um ataque está no ar, a regra diz em que ponto do
// voo ele está e a animação só desenha; quando a regra resolve a chegada,
// chama Impacto() e o estouro cai no mesmo frame em que o PWM do alvo muda.

using System;
using System.Collections.Generic;
using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby.Mundo;

public partial class Efeitos3D : Node3D, IEfeitos
{
    private sealed class Projetil
    {
        public Node3D No = null!;
        public double T0;
        public float Off0;
        public Vector3 Ultima;
    }

    private sealed class Perdido
    {
        public Node3D No = null!;
        public double T0;
        public float Off;
        public double Idade;
        public int Sentido;
    }

    private Corrida _corrida = null!;
    private NaveVisual[] _naves = Array.Empty<NaveVisual>();
    private CameraRig _camera = null!;
    private readonly Dictionary<Voo, Projetil> _projeteis = new();
    private readonly List<Perdido> _perdidos = new();
    private double _t;

    /// <summary>Quem desenha as palavras que sobem da nave (o HUD, preso à nave na tela).</summary>
    public Action<int, string, Color, double>? Marcador;

    public void Configurar(Corrida corrida, NaveVisual[] naves, CameraRig camera)
    {
        _corrida = corrida;
        _naves = naves;
        _camera = camera;
    }

    // -- ciclo ------------------------------------------------------------------

    public void Atualizar(double dt)
    {
        _t += dt;

        // Um nó por ataque em voo; sai quando a regra tira o voo da lista.
        var vivos = new HashSet<Voo>(_corrida.EmVoo);
        foreach (var voo in _corrida.EmVoo)
        {
            if (!_projeteis.TryGetValue(voo, out var p))
            {
                var origem = _corrida.Naves[voo.Origem];
                p = new Projetil { No = CriarProjetil(voo.Tipo), T0 = origem.T, Off0 = Tracado.OffsetDaFaixa(voo.Origem) };
                AddChild(p.No);
                p.Ultima = PosicaoDoVoo(p, voo);
                p.No.Position = p.Ultima;
                _projeteis[voo] = p;
            }
            Vector3 pos = PosicaoDoVoo(p, voo);
            Orientar(p.No, pos, pos - p.Ultima);
            p.Ultima = pos;
            if (voo.Tipo == Item.Bomba)
                p.No.GetChild<Node3D>(0).RotateObjectLocal(Vector3.Right, (float)dt * 14f);
        }
        var sairam = new List<Voo>();
        foreach (var (voo, p) in _projeteis)
            if (!vivos.Contains(voo))
            {
                p.No.QueueFree();
                sairam.Add(voo);
            }
        foreach (var voo in sairam)
            _projeteis.Remove(voo);

        // Ataques que erraram seguem adiante e se desfazem.
        for (int i = _perdidos.Count - 1; i >= 0; i--)
        {
            var pd = _perdidos[i];
            pd.Idade += dt;
            if (pd.Idade > 0.45)
            {
                Particulas.Faiscas(this, pd.No.Position, Paleta.TextoFraco, 14, 3f);
                pd.No.QueueFree();
                _perdidos.RemoveAt(i);
                continue;
            }
            double t = pd.T0 + pd.Sentido * pd.Idade * 0.35;
            Vector3 pos = Tracado.Quadro(t, pd.Off, Tracado.AlturaVoo + 0.4f).Origin;
            Orientar(pd.No, pos, pos - pd.No.Position);
        }
    }

    private Vector3 PosicaoDoVoo(Projetil p, Voo voo)
    {
        var alvo = _corrida.Naves[voo.Alvo];
        double d = Pista.Mod1(alvo.T - p.T0);
        if (d > 0.5) d -= 1.0;                          // pelo caminho mais curto
        float k = (float)voo.Progresso;
        float arco = voo.Tipo == Item.Bomba ? 3.2f : 0.35f;
        float altura = Tracado.AlturaVoo + 0.3f + arco * MathF.Sin(Mathf.Pi * k);
        float off = Mathf.Lerp(p.Off0, Tracado.OffsetDaFaixa(voo.Alvo), k);
        return Tracado.Quadro(p.T0 + d * k, off, altura).Origin;
    }

    private static void Orientar(Node3D no, Vector3 pos, Vector3 movimento)
    {
        no.Position = pos;
        if (movimento.LengthSquared() > 1e-6f && no.IsInsideTree())
            no.LookAt(pos + movimento, Vector3.Up);
    }

    // -- construção dos projéteis ----------------------------------------------------

    private Node3D CriarProjetil(Item tipo)
    {
        var raiz = new Node3D();
        Color cor = Paleta.DoItem(tipo);
        if (tipo == Item.Bomba)
        {
            var casco = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f, RadialSegments = 24, Rings = 12 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.09f), Metallic = 0.7f, Roughness = 0.3f },
            };
            casco.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.28f, OuterRadius = 0.33f },
                MaterialOverride = Aceso(cor, 5f),
            });
            raiz.AddChild(casco);
            raiz.AddChild(new OmniLight3D { LightColor = cor, LightEnergy = 3f, OmniRange = 4f });
        }
        else
        {
            var dardo = new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.08f, Height = 0.9f },
                MaterialOverride = Aceso(cor, 9f),
                RotationDegrees = new Vector3(90f, 0f, 0f),     // o eixo da cápsula passa a apontar para a frente
            };
            raiz.AddChild(dardo);
            raiz.AddChild(new OmniLight3D { LightColor = cor, LightEnergy = 2.5f, OmniRange = 3.5f });
        }
        var rastro = Particulas.Exaustao(cor, 0.05f);
        rastro.Emitting = true;
        raiz.AddChild(rastro);
        return raiz;
    }

    private static StandardMaterial3D Aceso(Color cor, float energia) => new()
    {
        AlbedoColor = Colors.Black,
        EmissionEnabled = true,
        Emission = cor,
        EmissionEnergyMultiplier = energia,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    // -- peças de efeito ---------------------------------------------------------------

    private Vector3 PontoDaNave(int lane) =>
        _naves[lane].GlobalPosition + _naves[lane].GlobalTransform.Basis.Y * 0.3f;

    /// <summary>Anel que se expande no plano do leito.</summary>
    private void Onda(int lane, Color cor, float raio, double duracao = 0.5)
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/onda.gdshader") };
        mat.SetShaderParameter("cor", cor);
        var anel = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(2f, 2f) },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(anel);
        var nave = _naves[lane].GlobalTransform;
        anel.Transform = new Transform3D(nave.Basis.Scaled(new Vector3(raio, 1f, raio)), nave.Origin + nave.Basis.Y * 0.15f);
        var tw = CreateTween();
        tw.TweenMethod(Callable.From<float>(k => mat.SetShaderParameter("k", k)), 0f, 1f, duracao);
        tw.TweenCallback(Callable.From(anel.QueueFree));
    }

    /// <summary>Clarão de luz real, que ilumina pista e naves por um instante.</summary>
    private void Clarao(Vector3 pos, Color cor, float energia, float alcance, double duracao = 0.45)
    {
        var luz = new OmniLight3D { LightColor = cor, LightEnergy = energia, OmniRange = alcance, Position = pos };
        AddChild(luz);
        var tw = CreateTween();
        tw.TweenProperty(luz, "light_energy", 0f, duracao).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        tw.TweenCallback(Callable.From(luz.QueueFree));
    }

    /// <summary>Palavra curta que sobe da nave e some — em cima da pista, junto da nave.</summary>
    private void Marcar(int lane, string texto, Color cor, double duracao = 1.2) =>
        Marcador?.Invoke(lane, texto, cor, duracao);

    // -- IEfeitos -------------------------------------------------------------------------

    public void Limpar()
    {
        foreach (var p in _projeteis.Values) p.No.QueueFree();
        _projeteis.Clear();
        foreach (var pd in _perdidos) pd.No.QueueFree();
        _perdidos.Clear();
    }

    public void Largada() => _camera.Tremer(0.12f);

    public void Premio(Nave nave, Item item)
    {
        Vector3 pos = PontoDaNave(nave.Lane);
        Color cor = Paleta.DoItem(item);
        if (item == Item.Nada)
        {
            Particulas.Nuvem(this, pos, new Color(0.45f, 0.48f, 0.55f), 14);
            Marcar(nave.Lane, "nada", Paleta.TextoFraco);
            return;
        }
        Particulas.Faiscas(this, pos, cor, 30, 6f);
        Onda(nave.Lane, cor, 2.8f);
        Marcar(nave.Lane, Cfg.NomeItem(item).ToLowerInvariant(), cor);
    }

    public void Ataque(Nave origem, Nave alvo, Item tipo, bool errou)
    {
        Vector3 pos = PontoDaNave(origem.Lane);
        Color cor = Paleta.DoItem(tipo);
        Particulas.Faiscas(this, pos, cor, 12, 5f);
        Clarao(pos, cor, 8f, 6f, 0.2);
        if (!errou)
            return;

        // Errou: o tiro sai assim mesmo, segue adiante e se desfaz sozinho.
        var no = CriarProjetil(tipo);
        AddChild(no);
        no.Position = pos;
        double d = Pista.Mod1(alvo.T - origem.T);
        _perdidos.Add(new Perdido
        {
            No = no,
            T0 = origem.T,
            Off = Tracado.OffsetDaFaixa(origem.Lane),
            Sentido = d > 0.5 ? -1 : 1,
        });
        Marcar(origem.Lane, "errou", Paleta.TextoFraco, 0.9);
    }

    public void Impacto(Nave alvo, Item tipo, ResultadoAtaque resultado)
    {
        Vector3 pos = PontoDaNave(alvo.Lane);
        if (resultado == ResultadoAtaque.Bloqueado)
        {
            Color ciano = Paleta.DoItem(Item.Escudo);
            _naves[alvo.Lane].AcenderEscudo();
            Particulas.Faiscas(this, pos, ciano, 40, 10f);
            Onda(alvo.Lane, ciano, 3.6f);
            Clarao(pos, ciano, 14f, 8f);
            _camera.Tremer(0.2f);
            Marcar(alvo.Lane, "bloqueado", ciano);
            return;
        }

        if (tipo == Item.Bomba)
        {
            Color laranja = new(1f, 0.55f, 0.2f);
            Particulas.Explosao(this, pos, Paleta.DoItem(Item.Bomba), 1f);
            Onda(alvo.Lane, laranja, 7.5f, 0.6);
            Clarao(pos, laranja, 50f, 18f, 0.55);
            _camera.Tremer(0.7f);
            Marcar(alvo.Lane, "parado!", Paleta.Alerta, 1.4);
        }
        else
        {
            Color cor = Paleta.DoItem(Item.Tiro);
            Particulas.Faiscas(this, pos, cor, 46, 12f);
            Onda(alvo.Lane, cor, 3.8f);
            Clarao(pos, cor, 20f, 10f);
            _camera.Tremer(0.32f);
            Marcar(alvo.Lane, "atingido", Paleta.Alerta);
        }
    }

    public void EscudoLevantado(Nave nave)
    {
        Color ciano = Paleta.DoItem(Item.Escudo);
        Onda(nave.Lane, ciano, 3f);
        Marcar(nave.Lane, "escudo", ciano, 1.0);
    }

    /// <summary>
    /// O escudo venceu no sensor. A bolha some sozinha (NaveVisual segue
    /// nave.Escudo); aqui só fica o aviso, discreto de propósito: quem perdeu
    /// o escudo precisa saber, mas não é um evento de impacto.
    /// </summary>
    public void EscudoVenceu(Nave nave) =>
        Marcar(nave.Lane, "escudo caiu", Paleta.DoItem(Item.Escudo).Lerp(Paleta.TextoFraco, 0.45f), 1.0);

    public void Superaquecimento(Nave nave)
    {
        Vector3 pos = PontoDaNave(nave.Lane);
        Particulas.Nuvem(this, pos, new Color(0.2f, 0.19f, 0.19f), 20);
        Particulas.Faiscas(this, pos, new Color(1f, 0.45f, 0.15f), 26, 7f);
        Marcar(nave.Lane, "superaqueceu", Paleta.Alerta, 1.3);
    }
}
