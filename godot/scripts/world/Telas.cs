// Quantas telas, e quando. Três modos, no painel (Tab) e no --telas.
//
//   Uma tela          a câmera de sempre, presa em quem lidera
//   Duas telas        cada nave na sua metade, em perseguição, o tempo todo
//   Duas que juntam   divide quando as naves se separam e volta a uma só
//                     quando voltam a caber no mesmo quadro
//
// COMO FUNCIONA
//
// Duas SubViewport dividindo o MESMO World3D da cena. Não é uma segunda cópia
// do mundo: é o mesmo mundo visto de outro lugar, então pista, naves, pedras e
// efeitos são os mesmos objetos, atualizados uma vez só. Cada viewport tem seu
// CameraRig preso a uma nave (NaveFixa), e por isso a metade da esquerda não
// troca de dono quando a outra nave passa na frente.
//
// A câmera única continua existindo e rodando por baixo das metades. É ela que
// aparece no modo de uma tela, e é ela que fica embaixo durante a transição:
// juntar e dividir é um esmaecimento das metades por cima dela, e não um corte
// seco. Quando a divisão está firme a câmera única sai de cena (Current =
// false) e, quando a tela é uma só, as metades param de desenhar — senão o
// jogo pagaria três renderizações do mundo o tempo todo em vez de uma.
//
// POR QUE PERSEGUIÇÃO NAS METADES, E NÃO O MODO ESCOLHIDO NO PAINEL
//
// "Visão geral" mostra o circuito inteiro e "transmissão" enquadra as DUAS
// naves: nos dois casos as metades ficariam quase idênticas, e a tela dividida
// não serviria para nada. Dividir só faz sentido quando cada metade mostra uma
// coisa que a outra não mostra, e isso é a perseguição.

using System;
using Godot;

namespace OrbitalDerby.Mundo;

public sealed partial class Telas : Control
{
    public enum Modo { Uma, Duas, Juntando }

    /// <summary>Segundos do esmaecimento entre uma tela e duas.</summary>
    private const float Transicao = 0.28f;

    /// <summary>
    /// Tempo mínimo num estado antes de poder trocar, no modo que junta.
    /// Sem isso, duas naves emparelhadas na saída de uma curva fazem a tela
    /// piscar entre dividida e inteira várias vezes por segundo.
    /// </summary>
    private const float Descanso = 0.9f;

    /// <summary>
    /// Distância entre as naves que manda dividir, e a que manda voltar a
    /// juntar. A folga entre as duas é a histerese: entrar na divisão custa
    /// mais longe do que sair dela.
    ///
    /// São números fixos, e NÃO crescem com o circuito. A pergunta que eles
    /// respondem é "da minha câmera dá para ver a outra nave?", e quem decide
    /// isso é o tamanho da nave e a distância da câmera de perseguição — as
    /// duas iguais em todos os circuitos. Medidos contra a corrida: emparelhadas
    /// na largada dá 4 a 9, correndo separadas dá 60 a 70.
    /// </summary>
    private const float SepDivide = 30f, SepJunta = 18f;

    public Modo ModoAtual { get; set; } = Modo.Uma;

    /// <summary>
    /// As metades estão mandando no que se vê. O HUD usa isto para decidir por
    /// qual câmera projetar o nome de cada nave.
    /// </summary>
    public bool Dividido => _alfa >= 0.5f;

    private readonly SubViewportContainer[] _caixas = new SubViewportContainer[2];
    private readonly SubViewport[] _vistas = new SubViewport[2];
    private readonly CameraRig[] _rigs = new CameraRig[2];
    private ColorRect _risco = null!;
    private CameraRig _unica = null!;

    /// <summary>0 = uma tela só, 1 = duas telas. O meio é a transição.</summary>
    private float _alfa;
    private bool _querDividir;
    private float _desdeTroca = Descanso;

    /// <param name="unica">A câmera que continua desenhando por baixo.</param>
    public void Montar(CameraRig unica)
    {
        _unica = unica;
        SetAnchorsPreset(LayoutPreset.FullRect);
        // Sem isto o painel de configurações e os cliques do menu parariam nas
        // metades em vez de chegar em quem escuta.
        MouseFilter = MouseFilterEnum.Ignore;

        var mundo = GetViewport().World3D;
        for (int i = 0; i < 2; i++)
        {
            _vistas[i] = new SubViewport
            {
                World3D = mundo,
                OwnWorld3D = false,
                TransparentBg = false,
                HandleInputLocally = false,
                AudioListenerEnable3D = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            _rigs[i] = new CameraRig
            {
                NaveFixa = i,
                ModoAtual = CameraRig.Modo.Perseguicao,
            };
            _vistas[i].AddChild(_rigs[i]);

            _caixas[i] = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
            _caixas[i].AddChild(_vistas[i]);
            AddChild(_caixas[i]);
        }

        // O risco no meio existe para o olho saber na hora que são duas coisas
        // diferentes, e não um quadro largo com uma emenda estranha.
        _risco = new ColorRect { Color = new Color(0.02f, 0.03f, 0.06f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_risco);

        Visible = false;
    }

    public static string NomeDoModo(Modo m) => m switch
    {
        Modo.Duas => "duas telas",
        Modo.Juntando => "duas que se juntam",
        _ => "uma tela",
    };

    public static string DetalheDoModo(Modo m) => m switch
    {
        Modo.Duas => "cada nave na sua metade, sempre",
        Modo.Juntando => "divide quando elas se afastam, junta quando se acham",
        _ => "a câmera segue quem está na frente",
    };

    /// <summary>O tremor tem de chegar nas três câmeras, ou só uma sente a explosão.</summary>
    public void Tremer(float quanto)
    {
        _unica.Tremer(quanto);
        foreach (var r in _rigs) r.Tremer(quanto);
    }

    /// <summary>A câmera que enquadra a nave `lane` agora, para o HUD projetar nela.</summary>
    public Camera3D CameraDe(int lane) => Dividido ? _rigs[lane].Camera : _unica.Camera;

    /// <summary>Canto de onde começa a metade da nave `lane` na tela.</summary>
    public Vector2 CantoDe(int lane) => Dividido ? _caixas[lane].Position : Vector2.Zero;

    /// <summary>Tamanho do pedaço de tela que mostra a nave `lane`.</summary>
    public Vector2 TamanhoDe(int lane) => Dividido ? _caixas[lane].Size : Size;

    public void Atualizar(double dt, NaveVisual[] naves, int lider, bool cinematica)
    {
        float f = (float)dt;

        // As câmeras das metades continuam sendo calculadas mesmo escondidas.
        // É só aritmética, não custa desenho nenhum, e é o que faz elas já
        // estarem no lugar certo quando a divisão aparece — senão cada divisão
        // começaria com um solavanco de câmera chegando de longe.
        foreach (var r in _rigs)
        {
            r.Cinematica = false;
            r.Atualizar(dt, naves, lider);
        }

        _desdeTroca += f;
        bool quer = ModoAtual switch
        {
            Modo.Duas => true,
            Modo.Juntando => DecidirDividir(naves),
            _ => false,
        };
        // Na tela de atração a órbita cinematográfica é o cartão de visitas do
        // jogo: dividir ali não mostra nada e estraga a apresentação.
        if (cinematica) quer = false;

        if (quer != _querDividir && _desdeTroca >= Descanso)
        {
            _querDividir = quer;
            _desdeTroca = 0f;
        }

        float destino = _querDividir ? 1f : 0f;
        _alfa = Mathf.MoveToward(_alfa, destino, f / Transicao);

        Aplicar();
    }

    /// <summary>
    /// Divide quando uma nave saiu do alcance do quadro da outra, junta quando
    /// volta. Enquanto está dividido vale o limite mais curto — é o que evita
    /// a tela piscar com as duas dançando em volta de um número só.
    /// </summary>
    private bool DecidirDividir(NaveVisual[] naves)
    {
        float sep = naves[0].GlobalPosition.DistanceTo(naves[1].GlobalPosition);
        return _querDividir ? sep > SepJunta : sep > SepDivide;
    }

    private void Aplicar()
    {
        bool aparece = _alfa > 0.001f;
        Visible = aparece;
        Modulate = new Color(1f, 1f, 1f, _alfa);

        // Com as metades cobrindo tudo, a câmera única não precisa desenhar; e
        // com a tela inteira, as metades não precisam. Durante a transição as
        // três desenham, porque é disso que a transição é feita.
        _unica.Camera.Current = _alfa < 0.999f;
        var quando = aparece ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;

        // O tamanho vem da viewport, e não das âncoras: este Control pendura
        // num CanvasLayer, e ali a âncora só vira tamanho depois de uma passada
        // de layout — no primeiro quadro ele ainda mede zero.
        var tam = GetViewport().GetVisibleRect().Size;
        Position = Vector2.Zero;
        Size = tam;
        float meio = MathF.Floor(tam.X * 0.5f);
        const float risco = 3f;

        _caixas[0].Position = Vector2.Zero;
        _caixas[0].Size = new Vector2(MathF.Max(1f, meio - risco * 0.5f), tam.Y);
        _caixas[1].Position = new Vector2(meio + risco * 0.5f, 0f);
        _caixas[1].Size = new Vector2(MathF.Max(1f, tam.X - meio - risco * 0.5f), tam.Y);
        _risco.Position = new Vector2(meio - risco * 0.5f, 0f);
        _risco.Size = new Vector2(risco, tam.Y);

        foreach (var v in _vistas)
        {
            v.RenderTargetUpdateMode = quando;
            // O antisserrilhado das metades acompanha o do jogo: quem baixa a
            // qualidade no Q não pode continuar pagando 4x MSAA em duas telas.
            v.Msaa3D = GetViewport().Msaa3D;
        }
    }
}
