// Montagem de malhas à mão, para tudo que é gerado por código (naves, estação,
// pista, asteroides).

using System;
using System.Collections.Generic;
using Godot;

namespace OrbitalDerby.Mundo;

public sealed class ConstrutorMalha
{
    // O Godot trata como FRENTE o triângulo em sentido horário visto de fora.
    // Em vez de acertar a ordem na mão em cada peça — fonte clássica de malha
    // "do avesso" —, cada triângulo recebe o lado para onde deve olhar e a ordem
    // é corrigida aqui, num lugar só.
    private const bool FrenteHoraria = true;

    private readonly List<Vector3> _v = new();
    private readonly List<Vector3> _n = new();
    private readonly List<Vector2> _uv = new();
    private readonly List<Vector2> _uv2 = new();
    private readonly List<Color> _c = new();
    private readonly List<int> _i = new();
    private bool _temCor;
    private bool _temUv2;

    public int Contagem => _v.Count;
    public Vector3 NormalDoVertice(int i) => _n[i];

    /// <summary>Índice a partir do qual o próximo <see cref="DeslocarDesde"/> vai mover.</summary>
    public int MarcaDeslocamento { get; set; }

    /// <summary>Translada os vértices criados desde <paramref name="inicio"/> (peças fora do eixo).</summary>
    public void DeslocarDesde(int inicio, Vector3 delta)
    {
        for (int k = inicio; k < _v.Count; k++)
            _v[k] += delta;
    }

    /// <summary>
    /// Novo vértice. <paramref name="uv2"/> é o canal para dados por vértice que
    /// não são textura — a inclinação do leito, por exemplo. Vai em UV2 e não em
    /// COLOR de propósito: UV2 chega ao shader como foi escrito, enquanto a cor
    /// de vértice passa por conversão de espaço de cor conforme o modo da malha.
    /// </summary>
    public int V(Vector3 p, Vector3 n, Vector2 uv = default, Color? cor = null, Vector2? uv2 = null)
    {
        _v.Add(p);
        _n.Add(n);
        _uv.Add(uv);
        _uv2.Add(uv2 ?? Vector2.Zero);
        _c.Add(cor ?? Colors.White);
        _temCor |= cor.HasValue;
        _temUv2 |= uv2.HasValue;
        return _v.Count - 1;
    }

    /// <summary>Triângulo que olha para o lado de <paramref name="fora"/>.</summary>
    public void Tri(int a, int b, int c, Vector3 fora)
    {
        Vector3 g = (_v[b] - _v[a]).Cross(_v[c] - _v[a]);
        bool aponta = g.Dot(fora) > 0;
        if (aponta == FrenteHoraria)
            (b, c) = (c, b);
        _i.Add(a);
        _i.Add(b);
        _i.Add(c);
    }

    public void Tri(int a, int b, int c) => Tri(a, b, c, _n[a] + _n[b] + _n[c]);

    public void Quad(int a, int b, int c, int d, Vector3 fora)
    {
        Tri(a, b, c, fora);
        Tri(a, c, d, fora);
    }

    /// <summary>Face plana com normal própria: vértices duplicados, aresta dura.</summary>
    public void TriPlano(Vector3 a, Vector3 b, Vector3 c, Vector3 fora, Color? cor = null)
    {
        Vector3 n = (b - a).Cross(c - a).Normalized();
        if (n.Dot(fora) < 0) n = -n;
        int i0 = V(a, n, default, cor), i1 = V(b, n, default, cor), i2 = V(c, n, default, cor);
        Tri(i0, i1, i2, n);
    }

    public void QuadPlano(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 fora, Color? cor = null)
    {
        Vector3 n = ((b - a).Cross(c - a) + (c - a).Cross(d - a)).Normalized();
        if (n.Dot(fora) < 0) n = -n;
        int i0 = V(a, n, default, cor), i1 = V(b, n, default, cor);
        int i2 = V(c, n, default, cor), i3 = V(d, n, default, cor);
        Quad(i0, i1, i2, i3, n);
    }

    /// <summary>Normais suaves: média das faces que tocam cada vértice.</summary>
    public void SuavizarNormais()
    {
        var acc = new Vector3[_v.Count];
        for (int k = 0; k < _i.Count; k += 3)
        {
            int a = _i[k], b = _i[k + 1], c = _i[k + 2];
            Vector3 g = (_v[b] - _v[a]).Cross(_v[c] - _v[a]);
            if (FrenteHoraria) g = -g;          // a ordem já está no padrão do Godot
            acc[a] += g;
            acc[b] += g;
            acc[c] += g;
        }
        for (int k = 0; k < acc.Length; k++)
            if (acc[k].LengthSquared() > 1e-12f)
                _n[k] = acc[k].Normalized();
    }

    public ArrayMesh Construir(ArrayMesh? destino = null)
    {
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = _n.ToArray();
        arr[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
        if (_temUv2)
            arr[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
        if (_temCor)
            arr[(int)Mesh.ArrayType.Color] = _c.ToArray();
        arr[(int)Mesh.ArrayType.Index] = _i.ToArray();
        var malha = destino ?? new ArrayMesh();
        malha.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return malha;
    }
}

/// <summary>Seção transversal de um casco em "loft".</summary>
public readonly record struct Secao(float Z, float Largura, float Altura, float Y = 0f);

/// <summary>Primitivas geométricas reaproveitadas pelo mundo inteiro.</summary>
public static class Geo
{
    /// <summary>Icosfera unitária subdividida <paramref name="nivel"/> vezes.</summary>
    public static (List<Vector3> vertices, List<int[]> faces) Icosfera(int nivel)
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int k = 0; k < v.Count; k++) v[k] = v[k].Normalized();

        var f = new List<int[]>
        {
            new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 },
            new[] { 1, 5, 9 }, new[] { 5, 11, 4 }, new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 },
            new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 }, new[] { 3, 8, 9 },
            new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 },
        };

        for (int n = 0; n < nivel; n++)
        {
            var meio = new Dictionary<long, int>();
            int Meio(int a, int b)
            {
                long chave = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (meio.TryGetValue(chave, out int idx)) return idx;
                v.Add(((v[a] + v[b]) * 0.5f).Normalized());
                meio[chave] = v.Count - 1;
                return v.Count - 1;
            }
            var novas = new List<int[]>(f.Count * 4);
            foreach (var tri in f)
            {
                int ab = Meio(tri[0], tri[1]), bc = Meio(tri[1], tri[2]), ca = Meio(tri[2], tri[0]);
                novas.Add(new[] { tri[0], ab, ca });
                novas.Add(new[] { tri[1], bc, ab });
                novas.Add(new[] { tri[2], ca, bc });
                novas.Add(new[] { ab, bc, ca });
            }
            f = novas;
        }
        return (v, f);
    }

    /// <summary>Tubo varrido ao longo de um caminho (transporte paralelo, sem torção).</summary>
    public static void Tubo(ConstrutorMalha m, IReadOnlyList<Vector3> caminho, float raio, int lados,
                            bool fechado, Color? cor = null)
    {
        int n = caminho.Count;
        if (n < 2) return;
        var tan = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 ant = caminho[fechado ? (i - 1 + n) % n : Math.Max(i - 1, 0)];
            Vector3 prox = caminho[fechado ? (i + 1) % n : Math.Min(i + 1, n - 1)];
            tan[i] = (prox - ant).Normalized();
        }

        Vector3 normal = tan[0].Cross(MathF.Abs(tan[0].Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        int aneis = fechado ? n + 1 : n;
        var idx = new int[aneis, lados + 1];
        float dist = 0f;
        for (int i = 0; i < aneis; i++)
        {
            int k = i % n;
            if (i > 0)
            {
                normal = (normal - tan[k] * normal.Dot(tan[k])).Normalized();
                dist += caminho[k].DistanceTo(caminho[(i - 1) % n]);
            }
            Vector3 binormal = tan[k].Cross(normal);
            for (int j = 0; j <= lados; j++)
            {
                float a = Mathf.Tau * j / lados;
                Vector3 dir = normal * MathF.Cos(a) + binormal * MathF.Sin(a);
                idx[i, j] = m.V(caminho[k] + dir * raio, dir, new Vector2((float)j / lados, dist), cor);
            }
        }
        for (int i = 0; i < aneis - 1; i++)
            for (int j = 0; j < lados; j++)
            {
                int a = idx[i, j], b = idx[i + 1, j], c = idx[i + 1, j + 1], d = idx[i, j + 1];
                // A normal gravada em cada vértice já é a direção radial: é o "fora" do quad.
                m.Quad(a, b, c, d, m.NormalDoVertice(a) + m.NormalDoVertice(c));
            }
    }

    /// <summary>
    /// Casco em "loft": anéis elípticos ao longo de Z. A primeira seção com
    /// largura zero vira ponta; a última recebe tampa plana.
    /// </summary>
    public static void Loft(ConstrutorMalha m, IReadOnlyList<Secao> secoes, int lados, bool plano, Color? cor = null)
    {
        int ns = secoes.Count;
        var anel = new Vector3[ns][];
        for (int s = 0; s < ns; s++)
        {
            anel[s] = new Vector3[lados];
            var sec = secoes[s];
            for (int j = 0; j < lados; j++)
            {
                // Começa em cima (+Y) para a quina do casco facetado ficar na espinha.
                float a = Mathf.Tau * j / lados + Mathf.Pi / 2f;
                anel[s][j] = new Vector3(sec.Largura * MathF.Cos(a), sec.Y + sec.Altura * MathF.Sin(a), sec.Z);
            }
        }

        Vector3 Centro(int s) => new(0, secoes[s].Y, secoes[s].Z);

        for (int s = 0; s < ns - 1; s++)
        {
            for (int j = 0; j < lados; j++)
            {
                int j2 = (j + 1) % lados;
                Vector3 a = anel[s][j], b = anel[s + 1][j], c = anel[s + 1][j2], d = anel[s][j2];
                Vector3 meio = (a + b + c + d) * 0.25f;
                Vector3 fora = meio - (Centro(s) + Centro(s + 1)) * 0.5f;
                if (plano)
                {
                    m.QuadPlano(a, b, c, d, fora, cor);
                }
                else
                {
                    int ia = m.V(a, (a - Centro(s)).Normalized(), default, cor);
                    int ib = m.V(b, (b - Centro(s + 1)).Normalized(), default, cor);
                    int ic = m.V(c, (c - Centro(s + 1)).Normalized(), default, cor);
                    int id = m.V(d, (d - Centro(s)).Normalized(), default, cor);
                    m.Quad(ia, ib, ic, id, fora);
                }
            }
        }

        // Tampa traseira plana.
        Vector3 fim = Centro(ns - 1);
        Vector3 foraFim = secoes[ns - 1].Z >= secoes[0].Z ? Vector3.Back : Vector3.Forward;
        for (int j = 0; j < lados; j++)
            m.TriPlano(fim, anel[ns - 1][j], anel[ns - 1][(j + 1) % lados], foraFim, cor);
    }

    /// <summary>
    /// Placa: contorno 2D (X, Z) extrudado em Y com <paramref name="espessura"/>,
    /// depois posicionado por <paramref name="xf"/>. Asas, aletas, lâminas.
    /// </summary>
    public static void Placa(ConstrutorMalha m, Vector2[] contorno, float espessura, Transform3D xf, Color? cor = null)
    {
        int[] tris = Geometry2D.TriangulatePolygon(contorno);
        float h = espessura * 0.5f;
        Vector3 cima = xf.Basis * Vector3.Up;
        Vector3 P(Vector2 p, float y) => xf * new Vector3(p.X, y, p.Y);

        for (int k = 0; k + 2 < tris.Length; k += 3)
        {
            Vector2 a = contorno[tris[k]], b = contorno[tris[k + 1]], c = contorno[tris[k + 2]];
            m.TriPlano(P(a, h), P(b, h), P(c, h), cima, cor);
            m.TriPlano(P(a, -h), P(b, -h), P(c, -h), -cima, cor);
        }

        Vector2 centro = Vector2.Zero;
        foreach (var p in contorno) centro += p;
        centro /= contorno.Length;
        for (int k = 0; k < contorno.Length; k++)
        {
            Vector2 a = contorno[k], b = contorno[(k + 1) % contorno.Length];
            Vector2 meio = (a + b) * 0.5f;
            Vector3 fora = xf.Basis * new Vector3(meio.X - centro.X, 0, meio.Y - centro.Y);
            m.QuadPlano(P(a, h), P(b, h), P(b, -h), P(a, -h), fora, cor);
        }
    }
}
