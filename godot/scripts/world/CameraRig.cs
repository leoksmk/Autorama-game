// Câmera. Três enquadramentos de corrida, trocados com C:
//
//   Transmissão   do lado de fora da pista, junto de quem lidera, olhando
//                 para dentro: a ÍRIS-9 fica no fundo, atrás das naves. O
//                 quadro abre conforme as duas se afastam, como numa TV
//   Visão geral   quase de cima, para ver as distâncias entre as naves
//   Perseguição   atrás de quem lidera
//
// Na tela de atração a câmera faz uma órbita lenta e baixa (Cinematica).
// O tremor é "trauma" que decai: explosões somam, o tempo apaga.

using System;
using Godot;

namespace OrbitalDerby.Mundo;

public partial class CameraRig : Node3D
{
    public enum Modo { Transmissao, VisaoGeral, Perseguicao }

    public Modo ModoAtual { get; set; } = Modo.Transmissao;
    public bool Cinematica { get; set; }
    public Camera3D Camera { get; private set; } = null!;

    private Vector3 _pos, _alvo;
    private float _trauma;
    private double _t;
    private bool _primeiro = true;

    public override void _Ready()
    {
        Camera = new Camera3D
        {
            Fov = 48f,
            Near = 0.1f,
            Far = 2500f,
            Current = true,
            Attributes = new CameraAttributesPractical
            {
                DofBlurFarEnabled = true,
                DofBlurFarDistance = 150f,
                DofBlurFarTransition = 80f,
                DofBlurAmount = 0.06f,
            },
        };
        AddChild(Camera);
    }

    public void ProximoModo() => ModoAtual = (Modo)(((int)ModoAtual + 1) % 3);

    public static string NomeDoModo(Modo m) => m switch
    {
        Modo.VisaoGeral => "visão geral",
        Modo.Perseguicao => "perseguição",
        _ => "transmissão",
    };

    public void Tremer(float quanto) => _trauma = Mathf.Min(1f, _trauma + quanto);

    public void Atualizar(double dt, NaveVisual[] naves, int lider)
    {
        _t += dt;
        float f = (float)dt;
        float t = (float)_t;

        Vector3 posAlvo, olharAlvo;
        float fov;
        float rapidez = 2.2f;

        if (Cinematica)
        {
            float a = t * 0.07f + 0.6f;
            posAlvo = new Vector3(MathF.Cos(a) * 64f, 17f + 5f * MathF.Sin(t * 0.13f), MathF.Sin(a) * 52f);
            olharAlvo = new Vector3(0f, 2f, 0f);
            fov = 46f;
        }
        else if (ModoAtual == Modo.VisaoGeral)
        {
            posAlvo = new Vector3(0f, 92f, 44f);
            olharAlvo = new Vector3(0f, 0f, 3f);
            fov = 44f;
        }
        else if (ModoAtual == Modo.Perseguicao)
        {
            var nave = naves[lider];
            var b = nave.GlobalTransform.Basis;
            posAlvo = nave.GlobalPosition + b.Z * 7.5f + b.Y * 2.8f;
            olharAlvo = nave.GlobalPosition - b.Z * 5f + b.Y * 0.6f;
            fov = 62f;
            rapidez = 6f;
        }
        else
        {
            var lid = naves[lider].GlobalPosition;
            var outra = naves[1 - lider].GlobalPosition;
            var foco = lid.Lerp(outra, 0.4f);
            float sep = lid.DistanceTo(outra);
            // O centro do oval é o centro da estação: "radial" aponta para fora da pista.
            var radial = new Vector3(lid.X, 0f, lid.Z);
            radial = radial.LengthSquared() > 1f ? radial.Normalized() : Vector3.Back;
            var frente = -naves[lider].GlobalTransform.Basis.Z;
            frente = new Vector3(frente.X, 0f, frente.Z).Normalized();
            float d = Mathf.Clamp(18f + sep * 0.55f, 18f, 72f);
            posAlvo = foco + radial * d + Vector3.Up * (6f + d * 0.42f) - frente * (d * 0.3f);
            olharAlvo = foco + frente * 3f;
            fov = 44f;
            rapidez = 3f;
        }

        float k = 1f - MathF.Exp(-f * rapidez);
        if (_primeiro)
        {
            _pos = posAlvo;
            _alvo = olharAlvo;
            _primeiro = false;
        }
        _pos = _pos.Lerp(posAlvo, k);
        _alvo = _alvo.Lerp(olharAlvo, k);
        Camera.Fov = Mathf.Lerp(Camera.Fov, fov, k);

        _trauma = Mathf.Max(0f, _trauma - f * 1.4f);
        float s = _trauma * _trauma;
        var sacode = new Vector3(MathF.Sin(t * 37f), MathF.Sin(t * 43f + 1.3f), MathF.Sin(t * 29f + 2.1f)) * s * 0.7f;

        Camera.GlobalPosition = _pos + sacode;
        Camera.LookAt(_alvo + sacode * 0.4f, Vector3.Up);
    }
}
