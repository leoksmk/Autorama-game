// HUD e telas. Painéis de vidro fosco (a cena 3D desfocada por trás) com o
// conteúdo desenhado em código por cima: nada de imagem, fonte do sistema.
//
// A telemetria lê do barramento de saída, não das naves: é o contrato com o
// hardware mostrado ao vivo. Se um efeito não aparece ali, ele não chegou ao
// PWM e não seria sentido pelo carrinho físico.

using System;
using System.Linq;
using Godot;
using OrbitalDerby.Core;
using OrbitalDerby.Entrada;
// O minimapa e o velocímetro precisam do traçado: um para desenhar a volta,
// outro para saber quantos metros vale uma volta. É a única dependência do HUD
// no mundo 3D, e é de leitura — nada daqui volta para a geometria.
using OrbitalDerby.Mundo;

namespace OrbitalDerby.Interface;

/// <summary>Camada que desenha por cima dos painéis de vidro.</summary>
public partial class CamadaDesenho : Control
{
    public Action<CamadaDesenho>? Desenhar;
    public override void _Draw() => Desenhar?.Invoke(this);
}

/// <summary>Onde a nave está na tela neste frame (calculado pela câmera 3D).</summary>
public readonly record struct AncoraNave(bool Visivel, Vector2 Pos, float Alfa);

public partial class Hud : Control
{
    private sealed class Marca
    {
        public int Lane;
        public string Texto = "";
        public Color Cor;
        public double Duracao, Idade;
    }

    private enum Vidro { P1, P2, Caixa1, Caixa2, Telemetria, Cartao, Mapa, Config }

    public Font Fonte { get; private set; } = null!;
    public Font FonteForte { get; private set; } = null!;
    public bool MostrarFps { get; set; }

    private Corrida? _corrida;
    private SaidaNula _saida = null!;
    private IFonteEntrada[] _fontes = Array.Empty<IFonteEntrada>();
    private GerenteSerial _serial = null!;
    private CamadaDesenho _tinta = null!;
    private CamadaDesenho _marcas = null!;
    private readonly System.Collections.Generic.List<Marca> _listaMarcas = new();
    private readonly float[] _desvio = new float[2];
    private readonly float[] _livre = { 1f, 1f };
    public AncoraNave[] Ancoras { get; set; } = new AncoraNave[2];
    private readonly Panel[] _vidros = new Panel[8];
    private StyleBoxFlat _borda = null!;
    private StyleBoxFlat _preenche = null!;

    // Traçado do minimapa em coordenadas 0..1, medido uma vez do Tracado.
    private Vector2[]? _mapaLinha;
    private float _mapaLargura;        // largura do leito, na escala do mapa
    private Vector2[] _mapaCp = Array.Empty<Vector2>();
    private Vector2[] _mapaCpNormal = Array.Empty<Vector2>();
    private Vector2 _mapaLargada, _mapaLargadaNormal;

    /// <summary>A tela de configurações. O Main monta as opções e trata a entrada.</summary>
    public Configuracoes Config { get; } = new();

    private EstadoApp _estado;
    private double _contagem, _t;
    private string _modoCamera = "";
    // Tamanho da tela virtual. O Size do próprio Control ficava zerado por
    // ele estar pendurado num CanvasLayer — e o HUD inteiro ia para o canto.
    private Vector2 Tela => GetViewportRect().Size;

    private static readonly Color Fraco = Paleta.TextoFraco;
    private static readonly Color Neutra = new(0.35f, 0.45f, 0.62f, 0.55f);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        string[] nomes = { "Bahnschrift", "Segoe UI", "Arial" };
        Fonte = new SystemFont { FontNames = nomes, FontWeight = 500 };
        FonteForte = new SystemFont { FontNames = nomes, FontWeight = 700 };

        // Nomes e marcadores ficam por baixo dos vidros: o painel cobre, não é coberto.
        _marcas = new CamadaDesenho { MouseFilter = MouseFilterEnum.Ignore, Desenhar = DesenharMarcas };
        _marcas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_marcas);

        var vidro = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/vidro.gdshader") };
        for (int i = 0; i < _vidros.Length; i++)
        {
            var sb = new StyleBoxFlat { BgColor = Colors.White, AntiAliasing = true, CornerDetail = 8 };
            sb.SetCornerRadiusAll(16);
            var p = new Panel { Material = vidro, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            p.AddThemeStyleboxOverride("panel", sb);
            AddChild(p);
            _vidros[i] = p;
        }

        _borda = new StyleBoxFlat { DrawCenter = false, AntiAliasing = true, CornerDetail = 8 };
        _borda.SetCornerRadiusAll(16);
        _preenche = new StyleBoxFlat { AntiAliasing = true, CornerDetail = 8 };
        _preenche.SetCornerRadiusAll(12);

        _tinta = new CamadaDesenho { MouseFilter = MouseFilterEnum.Ignore, Desenhar = Desenhar };
        _tinta.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_tinta);
    }

    public void Configurar(Corrida corrida, SaidaNula saida, IFonteEntrada[] fontes, GerenteSerial serial)
    {
        _corrida = corrida;
        _saida = saida;
        _fontes = fontes;
        _serial = serial;
    }

    /// <summary>
    /// Joga fora a planta do circuito. Chamado ao trocar de pista: o traçado
    /// é medido uma vez e guardado, então sem isto o mapa continuaria mostrando
    /// a pista anterior.
    /// </summary>
    public void EsquecerMapa() => _mapaLinha = null;

    public void Atualizar(double dt, EstadoApp estado, double contagem, string modoCamera)
    {
        _t += dt;
        _estado = estado;
        _contagem = contagem;
        _modoCamera = modoCamera;
        if (_corrida is null) return;
        PosicionarVidros();
        foreach (var m in _listaMarcas) m.Idade += dt;
        _listaMarcas.RemoveAll(m => m.Idade >= m.Duracao);
        SepararNomes((float)dt);
        ChecarObstrucao((float)dt);
        _marcas.QueueRedraw();
        _tinta.QueueRedraw();
    }

    /// <summary>Palavra curta que sobe da nave e some ("atingido", "bloqueado"...).</summary>
    public void Marcar(int lane, string texto, Color cor, double duracao) =>
        _listaMarcas.Add(new Marca { Lane = lane, Texto = texto, Cor = cor, Duracao = duracao });

    // Lado a lado na tela os nomes se encavalavam: quando encostam, cada um vai para um lado.
    private void SepararNomes(float dt)
    {
        var a = Ancoras[0];
        var b = Ancoras[1];
        float empurra = 0f, lado = 1f;
        if (a.Visivel && b.Visivel)
        {
            var d = b.Pos - a.Pos;
            lado = d.X >= 0f ? 1f : -1f;
            if (MathF.Abs(d.Y) < 44f)
                empurra = MathF.Max(0f, 120f - MathF.Abs(d.X)) * 0.5f;
        }
        float k = 1f - MathF.Exp(-dt * 12f);
        _desvio[0] = Mathf.Lerp(_desvio[0], -lado * empurra, k);
        _desvio[1] = Mathf.Lerp(_desvio[1], lado * empurra, k);
    }

    // Nome que cairia sobre um painel ou uma caixa do HUD some devagar: colado
    // no rótulo da caixa de item, os dois viravam uma palavra só.
    private void ChecarObstrucao(float dt)
    {
        float k = 1f - MathF.Exp(-dt * 10f);
        for (int lane = 0; lane < 2; lane++)
        {
            var an = Ancoras[lane];
            var nome = new Rect2(an.Pos.X + _desvio[lane] - 55f, an.Pos.Y - 56f, 110f, 34f);
            _livre[lane] = Mathf.Lerp(_livre[lane], SobreHud(nome) ? 0f : 1f, k);
        }
    }

    private bool SobreHud(Rect2 r)
    {
        for (int lane = 0; lane < 2; lane++)
        {
            if (_estado != EstadoApp.Atracao && RectPainel(lane).Grow(12f).Intersects(r))
                return true;
            // A caixa tem rótulo e dica embaixo dela: a margem de baixo é maior.
            if (_estado == EstadoApp.Corrida && _corrida!.Naves[lane].Roleta.Visivel
                && RectCaixa(lane).GrowIndividual(24f, 12f, 24f, 96f).Intersects(r))
                return true;
        }
        return RectTelemetria().Intersects(r)
            || (_estado != EstadoApp.Atracao && RectMapa().Grow(12f).Intersects(r));
    }

    // -- geometria da tela ---------------------------------------------------------

    private Rect2 RectPainel(int lane)
    {
        const float w = 404f, h = 316f;
        return lane == 0 ? new Rect2(28f, 28f, w, h) : new Rect2(Tela.X - 28f - w, 28f, w, h);
    }

    /// <summary>A engrenagem, no alto da tela inicial. Só aparece no menu.</summary>
    public Rect2 RectEngrenagem() => new(Tela.X - 104f, 40f, 64f, 64f);

    private Rect2 RectConfig()
    {
        float h = Config.MostrandoRegras ? 500f : 170f + Config.Opcoes.Count * 62f;
        float w = Config.MostrandoRegras ? 1120f : 940f;
        return new Rect2((Tela.X - w) / 2f, (Tela.Y - h) / 2f, w, h);
    }

    private Rect2 RectLinhaConfig(int i)
    {
        var r = RectConfig();
        return new Rect2(r.Position.X + 28f, r.Position.Y + 112f + i * 62f, r.Size.X - 56f, 56f);
    }

    /// <summary>Qual linha das configurações está sob o ponto, ou -1.</summary>
    public int LinhaConfigEm(Vector2 pos)
    {
        for (int i = 0; i < Config.Opcoes.Count; i++)
            if (RectLinhaConfig(i).HasPoint(pos))
                return i;
        return -1;
    }

    /// <summary>Metade direita de uma linha avança a opção; a esquerda, volta.</summary>
    public int DirecaoConfigEm(Vector2 pos, int linha) =>
        pos.X > RectLinhaConfig(linha).GetCenter().X ? 1 : -1;

    /// <summary>Minimapa: canto de baixo à esquerda, logo acima da telemetria.</summary>
    private Rect2 RectMapa()
    {
        const float w = 340f, h = 262f;
        return new Rect2(28f, Tela.Y - 92f - 20f - h, w, h);
    }

    private Rect2 RectCaixa(int lane)
    {
        var p = RectPainel(lane);
        const float l = 160f;
        var r = new Rect2(p.Position.X + (p.Size.X - l) / 2f, p.End.Y + 24f, l, l);
        // Na revelação a caixa dá um "estufo" curto e volta.
        var ro = _corrida!.Naves[lane].Roleta;
        if (ro.Estado == EstadoRoleta.Revelando)
        {
            float k = (float)Math.Min(1.0, ro.Tempo / 0.22);
            float s = 1f + 0.22f * MathF.Sin(k * Mathf.Pi);
            r = r.Grow(l * (s - 1f) * 0.5f);
        }
        return r;
    }

    private Rect2 RectTelemetria() => new(0f, Tela.Y - 92f, Tela.X, 92f);

    /// <summary>
    /// A tela inicial tem UMA frase. Tudo que era escolha — controle de cada
    /// nave, som, qualidade, câmera — foi para a engrenagem, e as regras foram
    /// com elas: quem chega perto precisa saber como começar, e só.
    /// </summary>
    private Rect2 RectCartaoAtracao()
    {
        const float w = 620f, h = 132f;
        return new Rect2((Tela.X - w) / 2f, Tela.Y * 0.60f - h / 2f, w, h);
    }

    private Rect2 RectCartaoResultado()
    {
        const float w = 880f, h = 372f;
        return new Rect2((Tela.X - w) / 2f, Tela.Y * 0.54f - h / 2f, w, h);
    }

    private void PosicionarVidros()
    {
        bool jogo = _estado != EstadoApp.Atracao;
        Mostrar(Vidro.P1, jogo, RectPainel(0));
        Mostrar(Vidro.P2, jogo, RectPainel(1));
        for (int lane = 0; lane < 2; lane++)
            Mostrar(lane == 0 ? Vidro.Caixa1 : Vidro.Caixa2,
                    _estado == EstadoApp.Corrida && _corrida!.Naves[lane].Roleta.Visivel, RectCaixa(lane));
        Mostrar(Vidro.Telemetria, true, RectTelemetria());
        Mostrar(Vidro.Mapa, jogo, RectMapa());
        Mostrar(Vidro.Config, Config.Aberta, RectConfig());
        Mostrar(Vidro.Cartao, !Config.Aberta && _estado is EstadoApp.Atracao or EstadoApp.Resultado,
                _estado == EstadoApp.Atracao ? RectCartaoAtracao() : RectCartaoResultado());
    }

    private void Mostrar(Vidro qual, bool visivel, Rect2 r)
    {
        var p = _vidros[(int)qual];
        p.Visible = visivel;
        if (!visivel) return;
        p.Position = r.Position;
        p.Size = r.Size;
    }

    // -- utilidades de desenho ---------------------------------------------------------

    private void Texto(CanvasItem c, string s, Vector2 pos, int tam, Color cor, bool forte = false)
    {
        var fonte = forte ? FonteForte : Fonte;
        // Contorno escuro por baixo: sobre a pista clara o texto sumia.
        c.DrawStringOutline(fonte, pos, s, HorizontalAlignment.Left, -1f, tam, Math.Clamp(tam / 5, 3, 10),
            new Color(0f, 0f, 0f, 0.5f * cor.A));
        c.DrawString(fonte, pos, s, HorizontalAlignment.Left, -1f, tam, cor);
    }

    private float Largura(string s, int tam, bool forte) =>
        (forte ? FonteForte : Fonte).GetStringSize(s, HorizontalAlignment.Left, -1f, tam).X;

    private void TextoDir(CanvasItem c, string s, float xDir, float y, int tam, Color cor, bool forte = false) =>
        Texto(c, s, new Vector2(xDir - Largura(s, tam, forte), y), tam, cor, forte);

    private void TextoCentro(CanvasItem c, string s, float xc, float y, int tam, Color cor, bool forte = false) =>
        Texto(c, s, new Vector2(xc - Largura(s, tam, forte) / 2f, y), tam, cor, forte);

    private static string Voltas(int v) => v == 1 ? "1 volta" : $"{v} voltas";

    private void Borda(CanvasItem c, Rect2 r, Color cor, int largura = 2)
    {
        _borda.BorderColor = cor;
        _borda.SetBorderWidthAll(largura);
        c.DrawStyleBox(_borda, r);
    }

    private static void Barra(CanvasItem c, Rect2 r, float fracao, Color cor)
    {
        c.DrawRect(r, new Color(0.06f, 0.09f, 0.15f, 0.9f));
        float f = Mathf.Clamp(fracao, 0f, 1f);
        if (f > 0f)
            c.DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * f, r.Size.Y)), cor);
        c.DrawRect(r, new Color(0.25f, 0.32f, 0.45f, 0.8f), false, 1f);
    }

    private Color Pulsando(float velocidade = 4f) =>
        Fraco.Lerp(Paleta.Texto, 0.5f + 0.5f * MathF.Sin((float)_t * velocidade));

    private static string Tempo(double s) => $"{(int)(s / 60)}:{s % 60:00.0}";

    /// <summary>
    /// Símbolo vetorial de cada face da caixa. Forma em vez de texto porque a
    /// caixa troca de face muitas vezes por segundo: palavra nessa velocidade
    /// não se lê, forma se lê.
    /// </summary>
    private static void Icone(CanvasItem c, Item item, Vector2 centro, float esc, Color cor)
    {
        Vector2 P(float x, float y) => centro + new Vector2(x, y) * esc;
        float traco = 0.8f * esc;
        switch (item)
        {
            case Item.Tiro:
                c.DrawColoredPolygon(new[] { P(7f, 0f), P(-4f, -5.5f), P(-1.5f, 0f), P(-4f, 5.5f) }, cor);
                c.DrawLine(P(-7f, -3f), P(-10f, -3f), cor, traco, true);
                c.DrawLine(P(-7f, 3f), P(-10f, 3f), cor, traco, true);
                break;
            case Item.Bomba:
                c.DrawCircle(P(0f, 1.5f), 6f * esc, cor);
                c.DrawLine(P(2.5f, -4f), P(6f, -8f), cor, traco, true);
                c.DrawCircle(P(6.5f, -8.5f), 1.6f * esc, new Color(1f, 0.94f, 0.75f));
                break;
            case Item.Escudo:
                var hex = new Vector2[7];
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.Tau / 6f - Mathf.Pi / 2f;
                    hex[k] = P(7f * MathF.Cos(a), 7f * MathF.Sin(a));
                }
                hex[6] = hex[0];
                c.DrawPolyline(hex, cor, traco, true);
                c.DrawLine(P(0f, -3.5f), P(0f, 3.5f), cor, traco, true);
                break;
            default:
                c.DrawArc(centro, 7f * esc, 0f, Mathf.Tau, 40, cor, traco, true);
                c.DrawLine(P(-5f, -5f), P(5f, 5f), cor, traco, true);
                break;
        }
    }

    // -- desenho ------------------------------------------------------------------------

    private void Desenhar(CamadaDesenho c)
    {
        if (_corrida is null) return;

        if (_estado != EstadoApp.Atracao)
        {
            for (int lane = 0; lane < 2; lane++)
            {
                PainelJogador(c, lane);
                CaixaItem(c, lane);
            }
            Relogio(c);
            Minimapa(c);
        }
        Telemetria(c);

        switch (_estado)
        {
            // Com as configurações abertas, o cartão do menu sai de cena: os
            // dois ocupam o mesmo meio da tela e um por cima do outro não se lê.
            case EstadoApp.Atracao:
                if (!Config.Aberta) TelaAtracao(c);
                Engrenagem(c);
                break;
            case EstadoApp.Contagem: TelaContagem(c); break;
            case EstadoApp.Resultado: TelaResultado(c); break;
            case EstadoApp.Corrida when _corrida.Tempo < 0.9:
                // "Vai!" some nos primeiros instantes da prova.
                float a = 1f - (float)(_corrida.Tempo / 0.9);
                TextoCentro(c, "Vai!", Tela.X / 2f, Tela.Y * 0.47f, 170, new Color(Paleta.Ok, a), true);
                break;
        }

        if (Config.Aberta)
            TelaConfig(c);

        if (MostrarFps)
            TextoDir(c, $"{Engine.GetFramesPerSecond():0} fps", Tela.X - 24f, 22f, 16, Fraco);
    }

    // -- configurações --------------------------------------------------------------------

    /// <summary>
    /// A engrenagem. Desenhada em vetor como todo o resto — nenhum ícone vem de
    /// arquivo —, e pulsando de leve quando o painel está fechado, para quem
    /// nunca abriu perceber que dá para clicar.
    /// </summary>
    private void Engrenagem(CanvasItem c)
    {
        var r = RectEngrenagem();
        var centro = r.GetCenter();
        float raio = r.Size.X * 0.34f;
        Color cor = Config.Aberta ? Paleta.Texto : Pulsando(2.2f);

        const int dentes = 8;
        for (int k = 0; k < dentes; k++)
        {
            float a = k * Mathf.Tau / dentes + (float)_t * 0.25f;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            var lado = new Vector2(-dir.Y, dir.X) * (raio * 0.26f);
            c.DrawColoredPolygon(new[]
            {
                centro + dir * raio * 0.82f - lado,
                centro + dir * raio * 1.34f - lado * 0.6f,
                centro + dir * raio * 1.34f + lado * 0.6f,
                centro + dir * raio * 0.82f + lado,
            }, cor);
        }
        c.DrawArc(centro, raio * 0.92f, 0f, Mathf.Tau, 36, cor, raio * 0.3f, true);
        c.DrawArc(centro, raio * 0.42f, 0f, Mathf.Tau, 24, cor, raio * 0.18f, true);
        TextoCentro(c, "configurações", centro.X, r.End.Y + 4f, 16, Fraco);
    }

    private void TelaConfig(CanvasItem c)
    {
        var r = RectConfig();
        TextoCentro(c, "ORBITAL DERBY", Tela.X / 2f, r.Position.Y - 108f, 76, Paleta.Texto, true);
        Borda(c, r, Neutra);

        if (Config.MostrandoRegras)
        {
            TelaRegras(c, r);
            return;
        }

        Texto(c, "CONFIGURAÇÕES", r.Position + new Vector2(30f, 58f), 40, Paleta.Texto, true);
        TextoDir(c, "vale para a próxima corrida", r.End.X - 30f, r.Position.Y + 34f, 18, Fraco);
        c.DrawLine(r.Position + new Vector2(30f, 78f), new Vector2(r.End.X - 30f, r.Position.Y + 78f), Neutra, 1f);

        for (int i = 0; i < Config.Opcoes.Count; i++)
        {
            var linha = RectLinhaConfig(i);
            var o = Config.Opcoes[i];
            bool aqui = i == Config.Linha;

            if (aqui)
            {
                _preenche.BgColor = new Color(Paleta.Estacao, 0.20f);
                c.DrawStyleBox(_preenche, linha);
                Borda(c, linha, new Color(Paleta.Estacao.Lerp(Paleta.Texto, 0.4f), 0.8f), 2);
            }

            float x = linha.Position.X + 24f;
            Texto(c, o.Rotulo, new Vector2(x, linha.Position.Y + 36f), 24, aqui ? Paleta.Texto : Fraco);

            // As setas só aparecem na linha escolhida: em todas, o cartão vira
            // uma parede de sinais e some a noção de onde o foco está.
            float xv = linha.Position.X + 300f;
            if (aqui)
            {
                Texto(c, "‹", new Vector2(xv - 34f, linha.Position.Y + 38f), 30, Paleta.Estacao.Lerp(Paleta.Texto, 0.6f), true);
                TextoDir(c, "›", linha.End.X - 18f, linha.Position.Y + 38f, 30, Paleta.Estacao.Lerp(Paleta.Texto, 0.6f), true);
            }
            Texto(c, o.Valor(), new Vector2(xv, linha.Position.Y + 36f), 26, aqui ? Paleta.Texto : Paleta.Texto.Lerp(Fraco, 0.4f), true);

            string detalhe = o.Detalhe();
            if (!string.IsNullOrEmpty(detalhe) && detalhe != o.Valor())
                Texto(c, detalhe, new Vector2(xv + Largura(o.Valor(), 26, true) + 18f, linha.Position.Y + 34f), 17, Fraco);
        }

        c.DrawLine(new Vector2(r.Position.X + 30f, r.End.Y - 52f), new Vector2(r.End.X - 30f, r.End.Y - 52f), Neutra, 1f);
        TextoCentro(c, "↑ ↓ escolhe   ·   ← → muda   ·   clique também vale   ·   Esc ou Enter fecha",
                    r.GetCenter().X, r.End.Y - 38f, 19, Fraco);
    }

    /// <summary>
    /// Painel de uma nave. A hierarquia é deliberada: primeiro quem é e em que
    /// posição está, depois a VELOCIDADE — o número que o jogador olha de canto
    /// de olho enquanto martela —, e só então ritmo e calor, que ele consulta
    /// de vez em quando em vez de monitorar.
    /// </summary>
    private void PainelJogador(CanvasItem c, int lane)
    {
        var r = RectPainel(lane);
        var n = _corrida!.Naves[lane];
        Color cor = Paleta.DoJogador(lane);
        Borda(c, r, new Color(cor, 0.55f));

        float x = r.Position.X + 22f, y = r.Position.Y, dir = r.End.X - 22f, larg = r.Size.X - 44f;

        // Faixa de identidade: nome à esquerda, posição à direita, fio embaixo.
        int pos = _corrida.Posicao(n);
        Texto(c, n.Nome, new Vector2(x, y + 44f), 34, cor, true);
        TextoDir(c, $"{pos}º", dir, y + 44f, 34, pos == 1 ? Paleta.Texto : Fraco, true);
        c.DrawLine(new Vector2(x, y + 76f), new Vector2(dir, y + 76f), new Color(cor, 0.35f), 1.5f);

        // Velocímetro. Em km/h porque a volta passou a ter comprimento de
        // verdade: Speed é em voltas por segundo e uma volta são
        // Tracado.Comprimento metros — nada de escala inventada.
        float kmh = (float)n.Speed * Tracado.Comprimento * 3.6f;
        string num = $"{kmh:0}";
        Texto(c, num, new Vector2(x, y + 140f), 64, n.Speed > 0.005 ? Paleta.Texto : Fraco, true);
        Texto(c, "km/h", new Vector2(x + Largura(num, 64, true) + 10f, y + 138f), 22, Fraco);

        // Voltas e diferença para o rival, do outro lado do velocímetro.
        int voltas = Math.Min(n.Voltas, Cfg.VoltasParaVencer);
        TextoDir(c, $"{voltas}/{Cfg.VoltasParaVencer}", dir, y + 112f, 40, Paleta.Texto, true);
        var (txtDif, corDif) = Diferenca(n);
        TextoDir(c, txtDif, dir, y + 140f, 24, corDif, true);

        Texto(c, "ritmo", new Vector2(x, y + 172f), 17, Fraco);
        Barra(c, new Rect2(x, y + 180f, larg, 12f), (float)n.Esforco, n.Esforco > 0.05 ? cor : new Color(0.2f, 0.25f, 0.35f));
        float lx = x + larg * (float)Cfg.CalorLimiar;          // daqui para cima o motor esquenta
        c.DrawLine(new Vector2(lx, y + 176f), new Vector2(lx, y + 196f), Fraco, 1.5f);

        // Com o calor desligado a barra fica parada em zero a prova inteira, o
        // que lê como painel quebrado. Melhor dizer que está desligado.
        Texto(c, "calor", new Vector2(x, y + 216f), 17, Fraco);
        if (!Cfg.AquecimentoAtivo)
        {
            Texto(c, "desligado", new Vector2(x + 56f, y + 216f), 17, Fraco);
        }
        else
        {
            Color corCalor = n.Superaquecimento > 0 ? Paleta.Alerta : Paleta.Ok.Lerp(Paleta.Alerta, (float)n.Calor);
            var rc = new Rect2(x, y + 224f, larg, 14f);
            Barra(c, rc, (float)n.Calor, corCalor);
            if (n.Calor > 0.78 && n.Superaquecimento <= 0 && Math.Sin(_t * 14) > 0)
                c.DrawRect(rc, Paleta.Alerta, false, 2f);
        }

        Texto(c, "slot", new Vector2(x, y + 268f), 17, Fraco);
        var rs = new Rect2(x + 52f, y + 248f, larg - 52f, 30f);
        c.DrawRect(rs, new Color(0.05f, 0.08f, 0.14f, 0.85f));
        if (n.Slot is Item item)
        {
            Color ci = Paleta.DoItem(item);
            c.DrawRect(rs, ci, false, 2f);
            Icone(c, item, rs.Position + new Vector2(20f, 15f), 1.2f, ci);
            Texto(c, Cfg.NomeItem(item), rs.Position + new Vector2(42f, 23f), 22, ci, true);
        }
        else if (n.Roleta.Visivel)
        {
            string rotulo = n.Roleta.Estado switch
            {
                EstadoRoleta.Oportunidade => "caixa aberta",
                EstadoRoleta.Girando => "girando",
                _ => "prêmio",
            };
            c.DrawRect(rs, cor, false, 1f);
            Texto(c, rotulo, rs.Position + new Vector2(12f, 22f), 20, cor);
        }
        else
        {
            Texto(c, "vazio", rs.Position + new Vector2(12f, 22f), 20, Fraco);
        }
        EscudoRestante(c, n, new Vector2(rs.End.X - 12f, rs.GetCenter().Y));

        var fonte = _fontes[lane];
        c.DrawCircle(new Vector2(x + 5f, y + 294f), 5f, fonte.Pronta ? Paleta.Ok : Paleta.Alerta);
        Texto(c, fonte.Descricao, new Vector2(x + 17f, y + 300f), 16, Fraco);

        if (!string.IsNullOrEmpty(n.Aviso))
            Texto(c, n.Aviso, new Vector2(x, y + 332f), 20, cor, true);
    }

    /// <summary>
    /// Diferença para o rival, em segundos de pista. Convertida pelo ritmo de
    /// quem vai na frente: "0,2 volta atrás" não diz nada, "1,4 s atrás" diz.
    /// </summary>
    private (string, Color) Diferenca(Nave n)
    {
        var outro = _corrida!.Adversario(n);
        double d = n.Progresso - outro.Progresso;
        if (Math.Abs(d) < 2e-4)
            return ("lado a lado", Fraco);
        // Nunca dividir pela velocidade instantânea crua: com a nave parada por
        // uma bomba a diferença explodiria para minutos e o painel pareceria
        // quebrado justo no momento em que o jogador mais olha para ele.
        double ritmo = Math.Max(Cfg.Cap * 0.5, Math.Max(n.Speed, outro.Speed));
        double seg = Math.Abs(d) / ritmo;
        string txt = (d > 0 ? "+" : "-") + $"{seg:0.0} s";
        return (txt, d > 0 ? Paleta.Ok : Paleta.Alerta);
    }

    /// <summary>
    /// Quanto sobra do Escudo, em trechos entre checkpoints. Pastilhas e não
    /// barra: o que resta é contável e inteiro, e uma barra sugeriria um
    /// relógio escorrendo — que é exatamente o que o Escudo deixou de ser.
    /// </summary>
    private void EscudoRestante(CanvasItem c, Nave n, Vector2 direita)
    {
        if (n.EscudoTrechos <= 0) return;
        Color ci = Paleta.DoItem(Item.Escudo);
        if (n.EscudoNoUltimoTrecho)
            ci = ci.Lerp(Paleta.Alerta, 0.35f + 0.35f * MathF.Sin((float)_t * 13f));
        const float raio = 5f, passo = 15f;
        for (int k = 0; k < Cfg.EscudoTrechos; k++)
        {
            var p = new Vector2(direita.X - k * passo, direita.Y);
            if (k < n.EscudoTrechos)
                c.DrawCircle(p, raio, ci);
            else
                c.DrawArc(p, raio, 0f, Mathf.Tau, 16, new Color(ci, 0.4f), 1.5f, true);
        }
    }

    private void CaixaItem(CanvasItem c, int lane)
    {
        var ro = _corrida!.Naves[lane].Roleta;
        if (_estado != EstadoApp.Corrida || !ro.Visivel) return;

        var r = RectCaixa(lane);
        Color ci = Paleta.DoItem(ro.Face);
        bool girando = ro.Estado == EstadoRoleta.Girando;
        bool revelando = ro.Estado == EstadoRoleta.Revelando;
        Color borda = girando || revelando ? ci : Paleta.DoJogador(lane);

        if (girando || revelando)
        {
            float brilho = girando ? 0.16f : 0.3f + 0.22f * MathF.Sin((float)_t * 18f);
            _preenche.BgColor = new Color(ci, brilho);
            c.DrawStyleBox(_preenche, r.Grow(-8f));
        }
        Borda(c, r, borda, 3);

        // Cantoneiras: a caixa lê como caixa, não como botão.
        float m = 12f, l = 14f;
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var canto = new Vector2(sx < 0 ? r.Position.X + m : r.End.X - m, sy < 0 ? r.Position.Y + m : r.End.Y - m);
            c.DrawLine(canto, canto + new Vector2(-sx * l, 0f), borda, 2.5f, true);
            c.DrawLine(canto, canto + new Vector2(0f, -sy * l), borda, 2.5f, true);
        }

        Icone(c, ro.Face, r.GetCenter(), 3.4f * r.Size.X / 160f, ci);

        var fonte = _fontes[lane];
        if (ro.Estado == EstadoRoleta.Oportunidade)
        {
            float frac = (float)ro.FracaoRestante;
            bool urgente = frac < 0.35f;
            Color corBarra = urgente ? Paleta.Alerta : Paleta.DoJogador(lane);
            var rb = new Rect2(r.Position.X, r.End.Y + 12f, r.Size.X, 9f);
            Barra(c, rb, frac, corBarra);
            Color pisca = Fraco.Lerp(corBarra, 0.5f + 0.5f * MathF.Sin((float)_t * (urgente ? 18f : 9f)));
            TextoCentro(c, fonte.RotuloAcao, r.GetCenter().X, rb.End.Y + 42f, 40, pisca, true);
            TextoCentro(c, "aperte", r.GetCenter().X, rb.End.Y + 68f, 20, pisca);
        }
        else if (revelando)
        {
            TextoCentro(c, Cfg.NomeItem(ro.Face), r.GetCenter().X, r.End.Y + 38f, 30, ci, true);
        }
    }

    // -- minimapa -----------------------------------------------------------------------

    /// <summary>
    /// Mede o traçado uma vez e guarda tudo em coordenadas 0..1, para o desenho
    /// só precisar de uma multiplicação por quadro. A projeção é de cima: X do
    /// mundo vira X da tela, Z vira Y, e a altura some — mapa de circuito é
    /// planta baixa, não perspectiva.
    /// </summary>
    private void PrepararMapa()
    {
        if (_mapaLinha is not null) return;

        const int n = 300;
        var bruto = new Vector2[n + 1];
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i <= n; i++)
        {
            var o = Tracado.Centro((double)i / n);
            bruto[i] = new Vector2(o.X, o.Z);
            minX = MathF.Min(minX, o.X); maxX = MathF.Max(maxX, o.X);
            minY = MathF.Min(minY, o.Z); maxY = MathF.Max(maxY, o.Z);
        }

        // Escala única nos dois eixos: circuito espremido num eixo deixa de ser
        // reconhecível, e reconhecer a forma é a única coisa que o mapa faz.
        float esc = 1f / MathF.Max(maxX - minX, maxY - minY);
        var centroCaixa = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
        Vector2 Normalizar(Vector2 v) => (v - centroCaixa) * esc + new Vector2(0.5f, 0.5f);

        _mapaLinha = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
            _mapaLinha[i] = Normalizar(bruto[i]);
        _mapaLargura = Tracado.Largura * esc;

        _mapaCp = new Vector2[Cfg.Checkpoints.Length];
        _mapaCpNormal = new Vector2[Cfg.Checkpoints.Length];
        for (int i = 0; i < Cfg.Checkpoints.Length; i++)
            (_mapaCp[i], _mapaCpNormal[i]) = PontoENormal(Cfg.Checkpoints[i], Normalizar);
        (_mapaLargada, _mapaLargadaNormal) = PontoENormal(0.0, Normalizar);
    }

    private static (Vector2, Vector2) PontoENormal(double t, Func<Vector2, Vector2> normalizar)
    {
        var a = Tracado.Centro(t - 0.002);
        var b = Tracado.Centro(t + 0.002);
        var pa = normalizar(new Vector2(a.X, a.Z));
        var pb = normalizar(new Vector2(b.X, b.Z));
        var dir = (pb - pa).Normalized();
        return (normalizar(new Vector2(Tracado.Centro(t).X, Tracado.Centro(t).Z)), new Vector2(-dir.Y, dir.X));
    }

    /// <summary>Onde t cai dentro do retângulo do mapa.</summary>
    private static Vector2 NoMapa(Vector2 normalizado, Rect2 r) =>
        r.Position + new Vector2(normalizado.X * r.Size.X, normalizado.Y * r.Size.Y);

    /// <summary>
    /// Planta do circuito com as duas naves em cima. Com o traçado antigo — um
    /// oval — um mapa não valeria o pixel; com o S, o curvão e a subida, saber
    /// em que parte da volta o rival está muda a hora de usar o item.
    /// </summary>
    private void Minimapa(CanvasItem c)
    {
        PrepararMapa();
        var caixa = RectMapa();
        Borda(c, caixa, Neutra);
        Texto(c, "circuito", caixa.Position + new Vector2(18f, 28f), 16, Fraco);
        TextoDir(c, $"{Tracado.Comprimento:0} m", caixa.End.X - 18f, 12f + caixa.Position.Y, 16, Fraco);

        // Área útil: sobra margem em cima para o rótulo e em volta para as naves
        // não encostarem na borda de vidro.
        var r = new Rect2(caixa.Position + new Vector2(30f, 44f), caixa.Size - new Vector2(60f, 74f));
        float lado = MathF.Min(r.Size.X, r.Size.Y);
        r = new Rect2(r.Position + (r.Size - new Vector2(lado, lado)) / 2f, new Vector2(lado, lado));

        var pontos = new Vector2[_mapaLinha!.Length];
        for (int i = 0; i < pontos.Length; i++)
            pontos[i] = NoMapa(_mapaLinha[i], r);

        // O leito é uma polilinha grossa, e não um anel de polígonos: uma chamada
        // de desenho em vez de trezentas, e a espessura já sai na escala certa.
        float grossura = MathF.Max(5f, _mapaLargura * lado);
        c.DrawPolyline(pontos, new Color(0.10f, 0.14f, 0.22f, 0.95f), grossura, true);
        c.DrawPolyline(pontos, new Color(0.35f, 0.47f, 0.68f, 0.55f), 1.5f, true);

        // Checkpoints: tracinho atravessado, aceso na cor de quem tem a caixa
        // aberta ali — a mesma regra do portal na pista, para o mapa e o mundo
        // nunca contarem histórias diferentes.
        for (int i = 0; i < _mapaCp.Length; i++)
        {
            var dono = _corrida!.Naves.FirstOrDefault(n =>
                n.Roleta.Estado == EstadoRoleta.Oportunidade && n.Roleta.Checkpoint == i);
            Color cor = dono is null ? new Color(0.45f, 0.58f, 0.8f, 0.8f) : Paleta.DoJogador(dono.Lane);
            float esp = dono is null ? 2f : 3.5f;
            var p = NoMapa(_mapaCp[i], r);
            var d = _mapaCpNormal[i] * (grossura * 0.75f);
            c.DrawLine(p - d, p + d, cor, esp, true);
        }

        var pl = NoMapa(_mapaLargada, r);
        var dl = _mapaLargadaNormal * (grossura * 0.9f);
        c.DrawLine(pl - dl, pl + dl, Paleta.Texto, 3f, true);

        // As naves por cima de tudo. O líder ganha um anel: numa planta pequena
        // a diferença de cor sozinha não diz quem está na frente.
        foreach (var n in _corrida!.Naves)
        {
            var p = NoMapa(_mapaLinha[Mathf.PosMod((int)MathF.Round((float)(n.T * (_mapaLinha.Length - 1))), _mapaLinha.Length - 1)], r);
            Color cor = Paleta.DoJogador(n.Lane);
            if (_corrida.Posicao(n) == 1)
                c.DrawArc(p, 9f, 0f, Mathf.Tau, 20, new Color(cor, 0.55f), 2f, true);
            c.DrawCircle(p, 5.5f, cor);
            if (n.Escudo)
                c.DrawArc(p, 7.5f, 0f, Mathf.Tau, 18, Paleta.DoItem(Item.Escudo), 1.5f, true);
        }
    }

    private void Relogio(CanvasItem c)
    {
        TextoCentro(c, Tempo(_corrida!.Tempo), Tela.X / 2f, 64f, 44, Paleta.Texto, true);
        TextoCentro(c, "tempo de prova", Tela.X / 2f, 88f, 16, Fraco);
    }

    private void Telemetria(CanvasItem c)
    {
        var r = RectTelemetria();
        float y = r.Position.Y;
        Texto(c, "telemetria · saída para as pistas", new Vector2(24f, y + 26f), 16, Fraco);

        string serial = _serial.Conectados switch
        {
            0 => "nenhum controle ESP conectado",
            1 => "1 controle ESP conectado",
            int k => $"{k} controles ESP conectados",
        };
        Texto(c, serial, new Vector2(400f, y + 26f), 16, _serial.Conectados > 0 ? Paleta.Ok : Fraco);
        if (_serial.IdsRepetidos)
            Texto(c, "dois controles com o mesmo id: tools/controle_esp.py --definir-id", new Vector2(640f, y + 26f), 16, Paleta.Alerta);
        else
            Texto(c, $"câmera: {_modoCamera}", new Vector2(640f, y + 26f), 16, Fraco);

        string[] alertas = { "superaquecido", "parado pela bomba", "teto reduzido" };
        for (int lane = 0; lane < 2; lane++)
        {
            // 560 e não 520: o rótulo de efeito mais longo ("teto reduzido +
            // escudo (1 trecho)") encostava no bloco da pista 2.
            float bx = 24f + lane * 560f, by = y + 64f;
            Color cor = Paleta.DoJogador(lane);
            float pwm = (float)_saida.Pwm(lane);
            string tag = string.IsNullOrEmpty(_saida.Tag(lane)) ? "livre" : _saida.Tag(lane);
            Texto(c, $"pista {lane + 1}", new Vector2(bx, by), 20, cor, true);
            Barra(c, new Rect2(bx + 88f, by - 14f, 150f, 14f), pwm, cor);
            Texto(c, $"pwm {pwm:0.00}", new Vector2(bx + 250f, by), 20, Paleta.Texto);
            Texto(c, tag, new Vector2(bx + 350f, by), 18, alertas.Any(a => tag.Contains(a)) ? Paleta.Alerta : Fraco);
        }

        float ly = y + 26f;
        foreach (var linha in _corrida!.Log.Skip(Math.Max(0, _corrida.Log.Count - 3)))
        {
            TextoDir(c, linha.Texto, Tela.X - 24f, ly, 17, Paleta.DoTom(linha.Tom));
            ly += 22f;
        }
    }

    private void TelaAtracao(CanvasItem c)
    {
        float cx = Tela.X / 2f;
        float topo = Tela.Y * 0.26f;
        TextoCentro(c, "ORBITAL DERBY", cx, topo, 118, Paleta.Texto, true);
        TextoCentro(c, "Dois cargueiros, um anel de detritos e a \u00cdRIS-9 vigiando.", cx, topo + 52f, 26, Fraco);
        TextoCentro(c, $"{Tracado.Atual.Nome}  \u00b7  {Tracado.Comprimento:0} m  \u00b7  {Cfg.VoltasParaVencer} voltas",
                    cx, topo + 86f, 21, Paleta.Estacao.Lerp(Paleta.Texto, 0.7f));

        var r = RectCartaoAtracao();
        Borda(c, r, Neutra);
        TextoCentro(c, "Espa\u00e7o para come\u00e7ar", cx, r.Position.Y + 34f, 42, Pulsando(), true);
        TextoCentro(c, "ou o bot\u00e3o de A\u00c7\u00c3O do controle", cx, r.Position.Y + 84f, 19, Fraco);
    }

    /// <summary>
    /// As regras, dentro das configura\u00e7\u00f5es. Sa\u00edram da tela inicial porque ela
    /// \u00e9 o que algu\u00e9m v\u00ea de longe, e de longe s\u00f3 cabe o nome do jogo e como
    /// come\u00e7ar. Os n\u00fameros s\u00e3o lidos da regra, n\u00e3o digitados: com o calor
    /// desligado ou outro circuito escolhido, o texto acompanha.
    /// </summary>
    private void TelaRegras(CanvasItem c, Rect2 r)
    {
        Texto(c, "COMO SE JOGA", r.Position + new Vector2(30f, 58f), 40, Paleta.Texto, true);
        TextoDir(c, "qualquer seta volta", r.End.X - 30f, r.Position.Y + 34f, 18, Fraco);
        c.DrawLine(r.Position + new Vector2(30f, 78f), new Vector2(r.End.X - 30f, r.Position.Y + 78f), Neutra, 1f);

        var linhas = new System.Collections.Generic.List<(string texto, bool forte)>
        {
            ("O acelerador \u00e9 de MARTELAR: aperte r\u00e1pido para ir r\u00e1pido. Segurar n\u00e3o faz nada.", true),
        };
        if (Cfg.AquecimentoAtivo)
            linhas.Add(($"Ritmo alto demais esquenta o motor, e calor cheio corta por {Cfg.SuperaquecimentoDuracao:0.0} s.", false));
        else
            linhas.Add(("O calor do motor est\u00e1 DESLIGADO: d\u00e1 para martelar no talo a prova inteira.", false));
        linhas.Add(($"Cruzar um checkpoint abre a caixa por {Cfg.RoletaOportunidade:0.0} s \u2014 aperte A\u00c7\u00c3O para girar.", false));
        linhas.Add(("A caixa gira sem parar a nave, mas \u00e0s vezes vem vazia.", false));
        linhas.Add(($"Tiro deixa lento por {Cfg.TiroDuracao:0.0} s, Bomba para por {Cfg.BombaDuracao:0.0} s, Escudo apara um ataque.", false));
        linhas.Add(("O Escudo N\u00c3O espera: ele cai no segundo checkpoint depois de levantado.", true));
        linhas.Add(("Tiro e Bomba s\u00f3 pegam o advers\u00e1rio de perto. Longe, o item queima.", false));
        linhas.Add(($"Vence quem completar {Cfg.VoltasParaVencer} voltas. O circuito de hoje é o {Tracado.Atual.Nome}, de {Tracado.Comprimento:0} m.", false));

        float y = r.Position.Y + 126f;
        foreach (var (texto, forte) in linhas)
        {
            Texto(c, texto, new Vector2(r.Position.X + 30f, y), 23, forte ? Paleta.Texto : Fraco, forte);
            y += 38f;
        }

        c.DrawLine(new Vector2(r.Position.X + 30f, r.End.Y - 52f), new Vector2(r.End.X - 30f, r.End.Y - 52f), Neutra, 1f);
        TextoCentro(c, "R durante a corrida volta para este menu  \u00b7  F11 tela cheia  \u00b7  Esc sai",
                    r.GetCenter().X, r.End.Y - 38f, 19, Fraco);
    }

    /// <summary>
    /// Nome de cada nave e os marcadores de efeito, presos à nave na tela. São
    /// 2D de propósito: como texto 3D transparente eles não gravam profundidade,
    /// e o desfoque de distância da câmera os borrava contra o fundo.
    /// </summary>
    private void DesenharMarcas(CamadaDesenho c)
    {
        if (_corrida is null || _estado == EstadoApp.Atracao) return;
        for (int lane = 0; lane < 2; lane++)
        {
            var an = Ancoras[lane];
            if (!an.Visivel) continue;
            var cor = Paleta.DoJogador(lane);
            float x = an.Pos.X + _desvio[lane];
            float yNome = an.Pos.Y - 30f;
            float alfaNome = an.Alfa * _livre[lane];
            if (alfaNome > 0.01f)
            {
                // A seta fica sobre a nave; o nome pode ter sido empurrado para o lado.
                var ponta = an.Pos + new Vector2(0f, -8f);
                c.DrawColoredPolygon(new[] { ponta, ponta + new Vector2(-7f, -11f), ponta + new Vector2(7f, -11f) },
                                     new Color(cor, 0.9f * alfaNome));
                TextoCentro(c, lane == 0 ? Cfg.NomeP1 : Cfg.NomeP2, x, yNome, 28, new Color(cor, alfaNome), true);
            }

            int pilha = 0;
            for (int i = _listaMarcas.Count - 1; i >= 0; i--)
            {
                var m = _listaMarcas[i];
                if (m.Lane != lane) continue;
                float k = (float)(m.Idade / m.Duracao);
                float sobe = 1f - MathF.Pow(1f - k, 3f);                        // sai rápido e desacelera
                float alfa = k < 0.55f ? 1f : 1f - (k - 0.55f) / 0.45f;
                float estufo = 1f + 0.4f * MathF.Max(0f, 1f - (float)m.Idade / 0.14f);
                float y = yNome - 36f - sobe * 46f - pilha * 36f;
                TextoCentro(c, m.Texto, x, y, (int)(34 * estufo), new Color(m.Cor, alfa), true);
                pilha++;
            }
        }
    }

    private void TelaContagem(CanvasItem c)
    {
        int n = (int)Math.Ceiling(_contagem);
        float fase = (float)(_contagem - Math.Floor(_contagem));
        int tam = (int)(160 * (1f + 0.35f * fase));
        TextoCentro(c, n.ToString(), Tela.X / 2f, Tela.Y * 0.47f + tam * 0.3f, tam, Paleta.Texto, true);
        TextoCentro(c, "martele o acelerador quando aparecer Vai", Tela.X / 2f, Tela.Y * 0.47f + 150f, 24, Fraco);
    }

    private void TelaResultado(CanvasItem c)
    {
        var v = _corrida!.Vencedor;
        if (v is null) return;
        float cx = Tela.X / 2f;
        var r = RectCartaoResultado();
        TextoCentro(c, $"{v.Nome} venceu", cx, r.Position.Y - 36f, 92, Paleta.DoJogador(v.Lane), true);
        Borda(c, r, Neutra);

        float x = r.Position.X + 40f, y = r.Position.Y + 70f;
        var ordem = _corrida.Classificacao();
        for (int i = 0; i < ordem.Count; i++)
        {
            var n = ordem[i];
            Texto(c, $"{i + 1}º", new Vector2(x, y), 34, Fraco, true);
            Texto(c, n.Nome, new Vector2(x + 70f, y), 34, Paleta.DoJogador(n.Lane), true);
            Texto(c, Voltas(Math.Min(n.Voltas, Cfg.VoltasParaVencer)), new Vector2(x + 290f, y - 4f), 24, Paleta.Texto);
            string fim = n.Terminou ? Tempo(n.TempoFinal) : $"a {n.T * 100:0}% da volta";
            TextoDir(c, fim, r.End.X - 40f, y - 4f, 26, n.Terminou ? Paleta.Texto : Fraco, n.Terminou);
            y += 62f;
        }
        Texto(c, $"tempo da prova: {Tempo(_corrida.Tempo)}", new Vector2(x, r.End.Y - 96f), 20, Fraco);

        c.DrawLine(new Vector2(r.Position.X + 40f, r.End.Y - 76f), new Vector2(r.End.X - 40f, r.End.Y - 76f), Neutra, 1f);
        TextoCentro(c, "R, Espaço ou AÇÃO para correr de novo", cx, r.End.Y - 30f, 28, Pulsando(), true);
    }
}
