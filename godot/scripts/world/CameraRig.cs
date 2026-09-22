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
                DofBlurFarDistance = 230f,
                DofBlurFarTransition = 110f,
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
            float a = t * 0.06f + 0.6f;
            posAlvo = new Vector3(2f + MathF.Cos(a) * 116f, 26f + 8f * MathF.Sin(t * 0.13f), -2f + MathF.Sin(a) * 98f);
            olharAlvo = new Vector3(2f, 2f, -2f);
            fov = 46f;
        }
        else if (ModoAtual == Modo.VisaoGeral)
        {
            // Enquadra os 130 x 94 m do circuito inteiro com folga nas bordas.
            posAlvo = new Vector3(4f, 124f, 56f);
            olharAlvo = new Vector3(4f, 0f, -4f);
            fov = 44f;
        }
        else if (ModoAtual == Modo.Perseguicao)
        {
            var nave = naves[lider];
            var b = nave.GlobalTransform.Basis;
            posAlvo = nave.GlobalPosition + b.Z * 10f + b.Y * 3.4f;
            olharAlvo = nave.GlobalPosition - b.Z * 7f + b.Y * 0.8f;
            // O campo abre com o empuxo: a 220 km/h a periferia correndo é o
            // que dá sensação de velocidade, e é de graça — nenhum efeito de tela.
            fov = 60f + 13f * nave.Empuxo;
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
            float d = Mathf.Clamp(26f + sep * 0.5f, 26f, 104f);
            posAlvo = foco + radial * d + Vector3.Up * (8f + d * 0.4f) - frente * (d * 0.3f);
            olharAlvo = foco + frente * 4f;
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
