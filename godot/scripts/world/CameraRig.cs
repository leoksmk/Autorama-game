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

        // Enquadramento na medida do circuito, e derivado dele — não um
        // conjunto de números por pista, que sempre acaba desatualizado.
        //
        // `alturaGeral` é a altura de câmera que faz o circuito caber na
        // vertical do quadro: metade do lado que manda (a largura conta
        // dividida pela proporção da tela, porque na horizontal sobra campo),
        // dividida pela tangente de meio FOV, com 25% de folga.
        var caixa = Tracado.Caixa;
        Vector3 meio = caixa.Position + caixa.Size * 0.5f;
        float proporcao = MathF.Max(1f, GetViewport().GetVisibleRect().Size.Aspect());
        float meioLado = MathF.Max(caixa.Size.X * 0.5f / proporcao, caixa.Size.Z * 0.5f);
        // 1,38 de folga e não 1,25: a inclinação da câmera come parte do
        // alcance, e na PLANTA BAIXA os dois grampos encostavam nas bordas.
        float alturaGeral = meioLado / MathF.Tan(Mathf.DegToRad(22f)) * 1.38f;
        float escala = MathF.Max(caixa.Size.X, caixa.Size.Z) / 133f;

        if (Cinematica)
        {
            float a = t * 0.06f + 0.6f;
            posAlvo = meio + new Vector3(MathF.Cos(a) * 116f * escala,
                                         (26f + 8f * MathF.Sin(t * 0.13f)) * escala,
                                         MathF.Sin(a) * 98f * escala);
            olharAlvo = meio;
            fov = 46f;
        }
        else if (ModoAtual == Modo.VisaoGeral)
        {
            // Quase de cima. A inclinação é pequena de propósito: cada grau
            // que a câmera deita rouba alcance do lado de perto do quadro, e é
            // justamente ali que fica a reta de largada.
            posAlvo = meio + new Vector3(0f, alturaGeral, alturaGeral * 0.28f);
            olharAlvo = meio;
            fov = 44f;
        }
        else if (ModoAtual == Modo.Perseguicao)
        {
            var nave = naves[lider];
            var b = nave.GlobalTransform.Basis;
            // As distâncias acompanham o tamanho da nave: com ela em 0,85 e a
            // câmera onde estava, a nave virava um ponto no meio da pista.
            posAlvo = nave.GlobalPosition + (b.Z * 10f + b.Y * 3.4f) * NaveVisual.Escala;
            olharAlvo = nave.GlobalPosition + (-b.Z * 7f + b.Y * 0.8f) * NaveVisual.Escala;
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
            // Para fora do circuito, medido a partir do MEIO DA CAIXA e não da
            // origem do mundo: o miolo do INTERLAGOS ORBITAL passa perto da
            // origem, e ali a direção radial virava do avesso a cada volta.
            var radial = new Vector3(lid.X - meio.X, 0f, lid.Z - meio.Z);
            radial = radial.LengthSquared() > 25f ? radial.Normalized() : Vector3.Back;
            var frente = -naves[lider].GlobalTransform.Basis.Z;
            frente = new Vector3(frente.X, 0f, frente.Z).Normalized();
            float d = Mathf.Clamp(26f + sep * 0.5f, 26f, 104f * escala);
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
