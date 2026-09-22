// A pista em 3D: leito metálico de duas faixas, guarda-corpos com filete de
// luz, estrutura por baixo (espinha + costelas), portais nos checkpoints e o
// pórtico de largada com as luzes de partida.

using System;
using System.Collections.Generic;
using Godot;
using OrbitalDerby.Core;

namespace OrbitalDerby.Mundo;

public partial class PistaVisual : Node3D
{
    // ~5,7 anéis por metro nos 388 m da volta. As curvas do S têm 16 m de raio;
    // com a metade disso os polígonos do leito apareciam de dentro do cockpit.
    private const int Segmentos = 2200;
    private const float Espessura = 0.35f;

    private readonly StandardMaterial3D[] _matPortal = new StandardMaterial3D[Cfg.Checkpoints.Length];
    private readonly StandardMaterial3D[] _luzesLargada = new StandardMaterial3D[5];

    private static readonly Color CorPortalRepouso = new(0.45f, 0.7f, 1f);

    private static StandardMaterial3D MetalEscuro() => new()
    {
        AlbedoColor = new Color(0.1f, 0.11f, 0.13f),
        Metallic = 0.85f,
        Roughness = 0.35f,
    };

    private static StandardMaterial3D Aceso(Color cor, float energia) => new()
    {
        AlbedoColor = Colors.Black,
        EmissionEnabled = true,
        Emission = cor,
        EmissionEnergyMultiplier = energia,
    };

    public override void _Ready()
    {
        ConstruirLeito();
        ConstruirGuardas();
        ConstruirEstrutura();
        for (int i = 0; i < Cfg.Checkpoints.Length; i++)
            _matPortal[i] = Portal(Cfg.Checkpoints[i], 0.55f, 1.2f, CorPortalRepouso, 1.6f);
        ConstruirLargada();
    }

    /// <summary>Cor do portal de um checkpoint (acende quando a caixa abre ali).</summary>
    public void DefinirPortal(int checkpoint, Color cor, float energia)
    {
        _matPortal[checkpoint].Emission = cor;
        _matPortal[checkpoint].EmissionEnergyMultiplier = energia;
    }

    public void PortalEmRepouso(int checkpoint) => DefinirPortal(checkpoint, CorPortalRepouso, 1.6f);

    /// <summary>Luzes do pórtico: <paramref name="vermelhas"/> acesas na contagem, ou todas verdes.</summary>
    public void LuzesLargada(int vermelhas, bool verde)
    {
        for (int k = 0; k < _luzesLargada.Length; k++)
        {
            var m = _luzesLargada[k];
            if (verde)
            {
                m.Emission = new Color(0.25f, 1f, 0.45f);
                m.EmissionEnergyMultiplier = 7f;
            }
            else if (k < vermelhas)
            {
                m.Emission = new Color(1f, 0.12f, 0.08f);
                m.EmissionEnergyMultiplier = 7f;
            }
            else
            {
                m.Emission = new Color(0.4f, 0.05f, 0.04f);
                m.EmissionEnergyMultiplier = 0.25f;
            }
        }
    }

    // -- leito ----------------------------------------------------------------

    private void ConstruirLeito()
    {
        float w = Tracado.Largura;
        float comp = Tracado.Comprimento;
        const int colunas = 7;
        var m = new ConstrutorMalha();
        var topo = new int[Segmentos + 1, colunas];
        var esq = new int[Segmentos + 1, 2];
        var dir = new int[Segmentos + 1, 2];
        var baixo = new int[Segmentos + 1, 2];

        for (int i = 0; i <= Segmentos; i++)
        {
            double t = (double)i / Segmentos;
            var q = Tracado.Quadro(t);
            float s = i == Segmentos ? comp : Tracado.Distancia(t);   // fecha a volta sem emenda
            Vector3 x = q.Basis.X, y = q.Basis.Y;

            // UV.x cresce SEMPRE de dentro para fora do circuito. Sem esta
            // correção, o lado do leito que UV.x = 0 aponta viraria conforme a
            // volta gira, e o shader pintaria a faixa do ÍON em cima da do
            // ÍGNIS em metade da pista.
            float sentido = Tracado.Fora(q).Dot(x) >= 0f ? 1f : -1f;
            // Inclinação normalizada: é ela que acende as zebras e o desgaste.
            var dados = new Vector2(Tracado.Inclinacao(t) / Tracado.InclinacaoMax, 0f);

            for (int c = 0; c < colunas; c++)
            {
                float lateral = ((float)c / (colunas - 1) - 0.5f) * w;
                float u = 0.5f + sentido * lateral / w;
                topo[i, c] = m.V(q.Origin + x * lateral, y, new Vector2(u, s), uv2: dados);
            }

            float ue = 0.5f - sentido * 0.5f, ud = 0.5f + sentido * 0.5f;
            Vector3 be = q.Origin - x * (w * 0.5f), bd = q.Origin + x * (w * 0.5f);
            esq[i, 0] = m.V(be, -x, new Vector2(ue, s), uv2: dados);
            esq[i, 1] = m.V(be - y * Espessura, -x, new Vector2(ue, s), uv2: dados);
            dir[i, 0] = m.V(bd, x, new Vector2(ud, s), uv2: dados);
            dir[i, 1] = m.V(bd - y * Espessura, x, new Vector2(ud, s), uv2: dados);
            baixo[i, 0] = m.V(be - y * Espessura, -y, new Vector2(ue, s), uv2: dados);
            baixo[i, 1] = m.V(bd - y * Espessura, -y, new Vector2(ud, s), uv2: dados);
        }

        for (int i = 0; i < Segmentos; i++)
        {
            Vector3 cima = m.NormalDoVertice(topo[i, 0]);
            for (int c = 0; c < colunas - 1; c++)
                m.Quad(topo[i, c], topo[i + 1, c], topo[i + 1, c + 1], topo[i, c + 1], cima);
            m.Quad(esq[i, 0], esq[i + 1, 0], esq[i + 1, 1], esq[i, 1], m.NormalDoVertice(esq[i, 0]));
            m.Quad(dir[i, 0], dir[i + 1, 0], dir[i + 1, 1], dir[i, 1], m.NormalDoVertice(dir[i, 0]));
            m.Quad(baixo[i, 0], baixo[i + 1, 0], baixo[i + 1, 1], baixo[i, 1], -cima);
        }

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/pista.gdshader") };
        mat.SetShaderParameter("comprimento", comp);
        mat.SetShaderParameter("largura", w);
        // Onde fica o eixo de cada faixa, em UV.x. Vem daqui e não de um número
        // no shader: se o leito ou o afastamento das faixas mudar, o desgaste
        // continua em cima do lugar por onde a nave passa de verdade.
        mat.SetShaderParameter("faixa_u", Tracado.OffsetFaixa / w);
        mat.SetShaderParameter("cor_pista1", Paleta.Ion);
        mat.SetShaderParameter("cor_pista2", Paleta.Ignis);
        AddChild(new MeshInstance3D { Mesh = m.Construir(), MaterialOverride = mat });
    }

    // -- guarda-corpos ----------------------------------------------------------

    private void ConstruirGuardas()
    {
        const float meia = 0.11f, alt = 0.28f;
        var corpo = new ConstrutorMalha();
        var luz = new ConstrutorMalha();

        foreach (int lado in new[] { -1, 1 })
        {
            var anelInterno = new List<(Vector3 baixo, Vector3 alto, Vector3 n)>();
            var linhas = new int[Segmentos + 1, 8];
            var luzes = new int[Segmentos + 1, 2];
            for (int i = 0; i <= Segmentos; i++)
            {
                var q = Tracado.Quadro((double)i / Segmentos);
                Vector3 x = q.Basis.X * lado, y = q.Basis.Y;
                float e = Tracado.Largura * 0.5f + meia;
                Vector3 ib = q.Origin + x * (e - meia) - y * Espessura;
                Vector3 it = q.Origin + x * (e - meia) + y * alt;
                Vector3 ot = q.Origin + x * (e + meia) + y * alt;
                Vector3 ob = q.Origin + x * (e + meia) - y * Espessura;
                // Faces com normal própria: interna, topo e externa.
                linhas[i, 0] = corpo.V(ib, -x); linhas[i, 1] = corpo.V(it, -x);
                linhas[i, 2] = corpo.V(it, y);  linhas[i, 3] = corpo.V(ot, y);
                linhas[i, 4] = corpo.V(ot, x);  linhas[i, 5] = corpo.V(ob, x);
                float u = i * 0.25f;
                luzes[i, 0] = luz.V(q.Origin + x * (e - 0.04f) + y * (alt + 0.005f), y, new Vector2(0f, u));
                luzes[i, 1] = luz.V(q.Origin + x * (e + 0.04f) + y * (alt + 0.005f), y, new Vector2(1f, u));
            }
            for (int i = 0; i < Segmentos; i++)
            {
                for (int f = 0; f < 6; f += 2)
                {
                    int a = linhas[i, f], b = linhas[i, f + 1], c = linhas[i + 1, f + 1], d = linhas[i + 1, f];
                    corpo.Quad(a, b, c, d, corpo.NormalDoVertice(a));
                }
                luz.Quad(luzes[i, 0], luzes[i + 1, 0], luzes[i + 1, 1], luzes[i, 1], luz.NormalDoVertice(luzes[i, 0]));
            }
        }

        AddChild(new MeshInstance3D { Mesh = corpo.Construir(), MaterialOverride = MetalEscuro() });
        AddChild(new MeshInstance3D
        {
            Mesh = luz.Construir(),
            MaterialOverride = Aceso(new Color(0.55f, 0.8f, 1f), 3.2f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // -- estrutura --------------------------------------------------------------

    private void ConstruirEstrutura()
    {
        // Espinha tubular correndo por baixo do leito.
        var caminho = new List<Vector3>();
        for (int i = 0; i < 600; i++)
        {
            var q = Tracado.Quadro(i / 600.0);
            caminho.Add(q.Origin - q.Basis.Y * 1.15f);
        }
        var espinha = new ConstrutorMalha();
        Geo.Tubo(espinha, caminho, 0.32f, 14, fechado: true);
        AddChild(new MeshInstance3D { Mesh = espinha.Construir(), MaterialOverride = MetalEscuro() });

        // Costelas a cada ~5 m, ligando o leito à espinha.
        int n = (int)(Tracado.Comprimento / 5f);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = new Vector3(Tracado.Largura * 0.92f, 0.12f, 0.32f) },
        };
        mm.InstanceCount = n * 2;
        for (int i = 0; i < n; i++)
        {
            var q = Tracado.Quadro((double)i / n);
            mm.SetInstanceTransform(i * 2, new Transform3D(q.Basis, q.Origin - q.Basis.Y * 0.5f));
            // Escala nos eixos LOCAIS da peça: Basis.Scaled escala nos eixos do
            // mundo, e nas curvas inclinadas isso fazia as pernas furarem o leito.
            var pe = new Transform3D(new Basis(q.Basis.X * 0.05f, q.Basis.Y * 6f, q.Basis.Z), q.Origin - q.Basis.Y * 0.85f);
            mm.SetInstanceTransform(i * 2 + 1, pe);
        }
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = MetalEscuro() });
    }

    // -- portais ------------------------------------------------------------------

    private StandardMaterial3D Portal(double t, float folga, float alturaRelativa, Color cor, float energia)
    {
        var q = Tracado.Quadro(t);
        float ra = Tracado.Largura * 0.5f + folga;
        var arco = new List<Vector3>();
        var arcoLuz = new List<Vector3>();
        for (int k = 0; k <= 32; k++)
        {
            float th = Mathf.Pi * k / 32f;
            Vector3 d = q.Basis.X * MathF.Cos(th) + q.Basis.Y * (MathF.Sin(th) * alturaRelativa);
            arco.Add(q.Origin + d * ra);
            arcoLuz.Add(q.Origin + d * (ra - 0.26f));
        }
        var estrutura = new ConstrutorMalha();
        Geo.Tubo(estrutura, arco, 0.22f, 12, fechado: false);
        var filete = new ConstrutorMalha();
        Geo.Tubo(filete, arcoLuz, 0.07f, 8, fechado: false);

        var matLuz = Aceso(cor, energia);
        var matEstrutura = MetalEscuro();
        Dissolver(matEstrutura);
        Dissolver(matLuz);
        AddChild(new MeshInstance3D { Mesh = estrutura.Construir(), MaterialOverride = matEstrutura });
        AddChild(new MeshInstance3D
        {
            Mesh = filete.Construir(),
            MaterialOverride = matLuz,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return matLuz;
    }

    /// <summary>
    /// De perto o material se dissolve (pontilhado) em vez de virar uma placa
    /// tapando a tela quando alguma câmera raspa nele.
    /// </summary>
    private static void Dissolver(StandardMaterial3D m)
    {
        m.DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither;
        m.DistanceFadeMinDistance = 1.5f;      // mais perto que isso: invisível
        m.DistanceFadeMaxDistance = 5f;        // a partir daqui: inteiro
    }

    private void ConstruirLargada()
    {
        Portal(0.0, 0.9f, 0.95f, new Color(1f, 1f, 1f), 2.4f);
        var q = Tracado.Quadro(0.0);
        float ra = Tracado.Largura * 0.5f + 0.9f;
        for (int k = 0; k < _luzesLargada.Length; k++)
        {
            float th = Mathf.Pi / 2f + (k - 2) * 0.16f;
            Vector3 d = q.Basis.X * MathF.Cos(th) + q.Basis.Y * (MathF.Sin(th) * 0.95f);
            _luzesLargada[k] = Aceso(new Color(0.4f, 0.05f, 0.04f), 0.25f);
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.26f, Height = 0.52f, RadialSegments = 16, Rings = 8 },
                MaterialOverride = _luzesLargada[k],
                Position = q.Origin + d * ra + q.Basis.Y * 0.4f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }
}
