using System.Numerics;
using System.Runtime.InteropServices;

namespace Anatomi3D.AssetBuilder;

/// <summary>Üçgen ağı üzerinde temel geometri işlemleri.</summary>
public static class MeshOps
{
    private static long Key(Vector3 p, float q)
    {
        long x = (long)MathF.Round(p.X * q) + (1L << 20);
        long y = (long)MathF.Round(p.Y * q) + (1L << 20);
        long z = (long)MathF.Round(p.Z * q) + (1L << 20);
        return (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
    }

    /// <summary>
    /// Aynı konumdaki köşeleri birleştirir (FBX dikişlerindeki çift köşeler), dejenere ve tekrarlanan üçgenleri atar.
    /// <paramref name="quant"/>: metre başına nicemleme adımı (1e5 = 0,01 mm).
    /// </summary>
    public static (Vector3[] pos, int[] idx, int[] faceMap) Weld(ReadOnlySpan<Vector3> pos, ReadOnlySpan<int> idx, float quant = 1e5f)
    {
        var map = new Dictionary<long, int>(pos.Length, LongKey.Comparer);
        var remap = new int[pos.Length];
        var outPos = new List<Vector3>(pos.Length);
        for (int i = 0; i < pos.Length; i++)
        {
            long k = Key(pos[i], quant);
            if (!map.TryGetValue(k, out int j))
            {
                j = outPos.Count;
                map[k] = j;
                outPos.Add(pos[i]);
            }
            remap[i] = j;
        }
        var outIdx = new List<int>(idx.Length);
        var faceMap = new List<int>(idx.Length / 3);
        var seen = new HashSet<(int, int, int)>();
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = remap[idx[t]], b = remap[idx[t + 1]], c = remap[idx[t + 2]];
            if (a == b || b == c || a == c) continue;
            // aynı üçgenin (herhangi bir dönüşümüyle) tekrarını at
            var key = Sorted(a, b, c);
            if (!seen.Add(key)) continue;
            outIdx.Add(a); outIdx.Add(b); outIdx.Add(c);
            faceMap.Add(t / 3);
        }
        return (outPos.ToArray(), outIdx.ToArray(), faceMap.ToArray());
    }

    private static (int, int, int) Sorted(int a, int b, int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
    }

    /// <summary>
    /// Üçgen yönlerini tutarlı hale getirir: kenar paylaşan üçgenler kenarı zıt yönde dolaşmalıdır. Her bağlı bileşen
    /// genişlik öncelikli taramayla hizalanır, ardından işaretli hacmi negatifse (içe bakıyorsa) bütünüyle çevrilir.
    /// Döndürülen değer: çevrilen üçgen sayısı.
    /// </summary>
    public static int OrientConsistently(Vector3[] pos, int[] idx)
    {
        int nf = idx.Length / 3;
        if (nf == 0) return 0;
        // yönsüz kenar -> (yüz, kenarı a->b yönünde mi dolaşıyor)
        var edgeFaces = new Dictionary<long, (int f0, bool d0, int f1, bool d1, int n)>(nf * 2, LongKey.Comparer);
        for (int f = 0; f < nf; f++)
            for (int e = 0; e < 3; e++)
            {
                int a = idx[f * 3 + e], b = idx[f * 3 + (e + 1) % 3];
                long k = EdgeKey(a, b);
                bool dir = a < b;
                if (edgeFaces.TryGetValue(k, out var v))
                {
                    if (v.n == 1) edgeFaces[k] = (v.f0, v.d0, f, dir, 2);
                    else edgeFaces[k] = (v.f0, v.d0, v.f1, v.d1, v.n + 1);
                }
                else edgeFaces[k] = (f, dir, -1, false, 1);
            }
        var flip = new sbyte[nf]; // 0 = ziyaret edilmedi, 1 = olduğu gibi, 2 = çevrilecek
        var comp = new int[nf];
        int compCount = 0;
        var queue = new Queue<int>();
        for (int s0 = 0; s0 < nf; s0++)
        {
            if (flip[s0] != 0) continue;
            flip[s0] = 1;
            comp[s0] = compCount;
            queue.Enqueue(s0);
            while (queue.Count > 0)
            {
                int f = queue.Dequeue();
                for (int e = 0; e < 3; e++)
                {
                    int a = idx[f * 3 + e], b = idx[f * 3 + (e + 1) % 3];
                    var v = edgeFaces[EdgeKey(a, b)];
                    if (v.n != 2) continue; // sınır ya da çok-manifold olmayan kenar
                    int g = v.f0 == f ? v.f1 : v.f0;
                    bool df = v.f0 == f ? v.d0 : v.d1;
                    bool dg = v.f0 == f ? v.d1 : v.d0;
                    if (flip[g] != 0) continue;
                    // tutarlı: iki yüz kenarı zıt yönde dolaşır; f çevrilecekse g için beklenti de tersine döner
                    bool sameDir = df == dg;
                    bool fFlipped = flip[f] == 2;
                    flip[g] = (sbyte)((sameDir ^ fFlipped) ? 2 : 1);
                    comp[g] = compCount;
                    queue.Enqueue(g);
                }
            }
            compCount++;
        }
        // bileşen başına işaretli hacim (bileşen ağırlık merkezine göre)
        var vol = new double[compCount];
        var cen = new Vector3[compCount];
        var cnt = new int[compCount];
        for (int f = 0; f < nf; f++)
        {
            cen[comp[f]] += (pos[idx[f * 3]] + pos[idx[f * 3 + 1]] + pos[idx[f * 3 + 2]]) / 3f;
            cnt[comp[f]]++;
        }
        for (int c = 0; c < compCount; c++) cen[c] /= Math.Max(1, cnt[c]);
        for (int f = 0; f < nf; f++)
        {
            int c = comp[f];
            var a = pos[idx[f * 3]] - cen[c];
            var b = pos[idx[f * 3 + 1]] - cen[c];
            var d = pos[idx[f * 3 + 2]] - cen[c];
            double v = Vector3.Dot(a, Vector3.Cross(b, d));
            vol[c] += flip[f] == 2 ? -v : v;
        }
        int flipped = 0;
        for (int f = 0; f < nf; f++)
        {
            bool doFlip = (flip[f] == 2) ^ (vol[comp[f]] < 0);
            if (!doFlip) continue;
            (idx[f * 3 + 1], idx[f * 3 + 2]) = (idx[f * 3 + 2], idx[f * 3 + 1]);
            flipped++;
        }
        return flipped;
    }

    /// <summary>Yalnız kullanılan köşeleri tutar.</summary>
    public static (Vector3[] pos, int[] idx, int[] oldToNew) Compact(Vector3[] pos, int[] idx)
    {
        var remap = new int[pos.Length];
        Array.Fill(remap, -1);
        var outPos = new List<Vector3>();
        var outIdx = new int[idx.Length];
        for (int i = 0; i < idx.Length; i++)
        {
            int v = idx[i];
            if (remap[v] < 0)
            {
                remap[v] = outPos.Count;
                outPos.Add(pos[v]);
            }
            outIdx[i] = remap[v];
        }
        return (outPos.ToArray(), outIdx, remap);
    }

    /// <summary>Açı ağırlıklı köşe normalleri.</summary>
    public static Vector3[] Normals(Vector3[] pos, int[] idx)
    {
        var n = new Vector3[pos.Length];
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            Vector3 pa = pos[a], pb = pos[b], pc = pos[c];
            var fn = Vector3.Cross(pb - pa, pc - pa);
            float len = fn.Length();
            if (len < 1e-20f) continue;
            fn /= len;
            n[a] += fn * Angle(pb - pa, pc - pa);
            n[b] += fn * Angle(pc - pb, pa - pb);
            n[c] += fn * Angle(pa - pc, pb - pc);
        }
        for (int i = 0; i < n.Length; i++)
        {
            float l = n[i].Length();
            n[i] = l > 1e-20f ? n[i] / l : Vector3.UnitY;
        }
        return n;
    }

    private static float Angle(Vector3 u, Vector3 v)
    {
        float d = Vector3.Dot(Vector3.Normalize(u), Vector3.Normalize(v));
        return MathF.Acos(Math.Clamp(d, -1f, 1f));
    }

    public static (Vector3 center, float radius, Vector3 min, Vector3 max) Bounds(ReadOnlySpan<Vector3> pos)
    {
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        var c = Vector3.Zero;
        foreach (var p in pos)
        {
            mn = Vector3.Min(mn, p);
            mx = Vector3.Max(mx, p);
            c += p;
        }
        c /= Math.Max(1, pos.Length);
        float r = 0;
        foreach (var p in pos) r = MathF.Max(r, Vector3.Distance(p, c));
        return (c, r, mn, mx);
    }

    /// <summary>Köşe dağılımının kovaryansından ana ve küçük eksenler (Jacobi öz ayrışımı).</summary>
    public static (Vector3 axis, Vector3 minor) PrincipalAxes(ReadOnlySpan<Vector3> pos, Vector3 c)
    {
        var (vals, vecs) = Covariance(pos, c);
        int[] order = [0, 1, 2];
        Array.Sort(order, (x, y) => vals[y].CompareTo(vals[x]));
        var minor = vecs[order[2]];
        if (minor.Z < 0) minor = -minor; // disk normalleri öne baksın
        return (vecs[order[0]], minor);
    }

    public static (float[] values, Vector3[] vectors) Covariance(ReadOnlySpan<Vector3> pos, Vector3 c)
    {
        double xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
        foreach (var p in pos)
        {
            double x = p.X - c.X, y = p.Y - c.Y, z = p.Z - c.Z;
            xx += x * x; yy += y * y; zz += z * z; xy += x * y; xz += x * z; yz += y * z;
        }
        double n = Math.Max(1, pos.Length);
        var m = new double[3, 3] { { xx / n, xy / n, xz / n }, { xy / n, yy / n, yz / n }, { xz / n, yz / n, zz / n } };
        return Jacobi(m);
    }

    public static (float[] values, Vector3[] vectors) Jacobi(double[,] m)
    {
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 16; sweep++)
        {
            foreach (var (i, j) in new[] { (0, 1), (0, 2), (1, 2) })
            {
                if (Math.Abs(m[i, j]) < 1e-18) continue;
                double th = 0.5 * Math.Atan2(2 * m[i, j], m[j, j] - m[i, i]);
                double cs = Math.Cos(th), sn = Math.Sin(th);
                for (int k = 0; k < 3; k++)
                {
                    double mik = m[i, k], mjk = m[j, k];
                    m[i, k] = cs * mik - sn * mjk;
                    m[j, k] = sn * mik + cs * mjk;
                }
                for (int k = 0; k < 3; k++)
                {
                    double mki = m[k, i], mkj = m[k, j];
                    m[k, i] = cs * mki - sn * mkj;
                    m[k, j] = sn * mki + cs * mkj;
                }
                for (int k = 0; k < 3; k++)
                {
                    double vki = v[k, i], vkj = v[k, j];
                    v[k, i] = cs * vki - sn * vkj;
                    v[k, j] = sn * vki + cs * vkj;
                }
            }
        }
        var vals = new float[] { (float)m[0, 0], (float)m[1, 1], (float)m[2, 2] };
        var vecs = new Vector3[3];
        for (int k = 0; k < 3; k++)
        {
            var col = new Vector3((float)v[0, k], (float)v[1, k], (float)v[2, k]);
            vecs[k] = col.LengthSquared() > 0 ? Vector3.Normalize(col) : Vector3.UnitX;
        }
        return (vals, vecs);
    }

    /// <summary>Her kenarın tam iki üçgene ait olduğu (su geçirmez) ağ mı?</summary>
    public static bool IsClosed(int[] idx)
    {
        var count = new Dictionary<long, int>(idx.Length, LongKey.Comparer);
        for (int t = 0; t < idx.Length; t += 3)
            for (int e = 0; e < 3; e++)
            {
                long k = EdgeKey(idx[t + e], idx[t + (e + 1) % 3]);
                count[k] = count.GetValueOrDefault(k) + 1;
            }
        foreach (var c in count.Values) if (c != 2) return false;
        return true;
    }

    public static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>Köşe komşuluk listesi (CSR biçiminde). Yönlü kenarlar sıralanıp tekilleştirilir; köşe başına nesne ayırmaz.</summary>
    public static (int[] start, int[] list) Adjacency(int vertexCount, int[] idx)
    {
        var keys = new long[idx.Length * 2];
        int n = 0;
        for (int t = 0; t < idx.Length; t += 3)
        {
            for (int e = 0; e < 3; e++)
            {
                long a = idx[t + e], b = idx[t + (e + 1) % 3];
                keys[n++] = (a << 32) | b;
                keys[n++] = (b << 32) | a;
            }
        }
        Array.Sort(keys);
        var start = new int[vertexCount + 1];
        var list = new List<int>(n / 2);
        long prev = -1;
        for (int i = 0; i < n; i++)
        {
            long k = keys[i];
            if (k == prev) continue;
            prev = k;
            int a = (int)(k >> 32);
            start[a + 1]++;
            list.Add((int)(k & 0xffffffff));
        }
        for (int v = 0; v < vertexCount; v++) start[v + 1] += start[v];
        return (start, list.ToArray());
    }

    /// <summary>
    /// Girinti / çıkıntı değeri: köşenin komşu ortalamasına göre normal yönündeki sapması (ortalama kenar boyuna bölünmüş).
    /// Pozitif = çukur (kavite), negatif = sırt. Yumuşatılmış, [-1, 1] aralığına sıkıştırılmış döner.
    /// </summary>
    public static float[] Cavity(Vector3[] pos, Vector3[] nrm, int[] idx)
    {
        var (start, list) = Adjacency(pos.Length, idx);
        var cav = new float[pos.Length];
        Parallel.For(0, pos.Length, i =>
        {
            int s = start[i], e = start[i + 1];
            if (e == s) return;
            var avg = Vector3.Zero;
            float len = 0;
            for (int k = s; k < e; k++)
            {
                avg += pos[list[k]];
                len += Vector3.Distance(pos[list[k]], pos[i]);
            }
            avg /= e - s;
            len /= e - s;
            cav[i] = len > 1e-9f ? Vector3.Dot(nrm[i], avg - pos[i]) / len : 0;
        });
        // iki tur Laplace yumuşatma
        for (int it = 0; it < 2; it++)
        {
            var nxt = new float[cav.Length];
            Parallel.For(0, pos.Length, i =>
            {
                int s = start[i], e = start[i + 1];
                float sum = cav[i];
                for (int k = s; k < e; k++) sum += cav[list[k]];
                nxt[i] = sum / (1 + e - s);
            });
            cav = nxt;
        }
        for (int i = 0; i < cav.Length; i++) cav[i] = MathF.Tanh(cav[i] * 3f);
        return cav;
    }
}

/// <summary>
/// Loop alt bölümlemesi (Charles Loop, 1987): her üçgen dörde bölünür, köşeler komşularının ağırlıklı
/// ortalamasına çekilir; sonuç C2-pürüzsüz bir limit yüzeye yaklaşır. Sınır ve çok-manifold olmayan kenarlarda
/// eğri kuralları (1/2, 3/4-1/8) kullanılır, böylece açıklıklar (göz, ağız) büzülmez.
/// Her yüz etiketi (yapı indeksi) alt yüzlerine aktarılır.
/// </summary>
public static class LoopSubdivision
{
    /// <param name="linear">true: yalnız orta nokta bölmesi (düz yüzey; hata ölçümü için)</param>
    public static (Vector3[] pos, int[] idx, int[] tags, int[] parentVertex, (int a, int b)[] edgeVertex) Subdivide(
        Vector3[] pos, int[] idx, int[] tags, bool linear = false)
    {
        int nv = pos.Length;
        int nf = idx.Length / 3;
        // kenar tablosu: anahtar -> kenar indeksi
        var edgeIndex = new Dictionary<long, int>(nf * 2, LongKey.Comparer);
        var edgeA = new List<int>(nf * 2);
        var edgeB = new List<int>(nf * 2);
        var edgeFaces = new List<int>(nf * 2);  // kaç yüz
        var edgeOpp0 = new List<int>(nf * 2);   // karşı köşeler
        var edgeOpp1 = new List<int>(nf * 2);
        var faceEdges = new int[nf * 3];
        for (int f = 0; f < nf; f++)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = idx[f * 3 + e], b = idx[f * 3 + (e + 1) % 3], opp = idx[f * 3 + (e + 2) % 3];
                long k = MeshOps.EdgeKey(a, b);
                if (!edgeIndex.TryGetValue(k, out int ei))
                {
                    ei = edgeA.Count;
                    edgeIndex[k] = ei;
                    edgeA.Add(Math.Min(a, b));
                    edgeB.Add(Math.Max(a, b));
                    edgeFaces.Add(0);
                    edgeOpp0.Add(opp);
                    edgeOpp1.Add(-1);
                }
                else if (edgeFaces[ei] == 1) edgeOpp1[ei] = opp;
                edgeFaces[ei]++;
                faceEdges[f * 3 + e] = ei;
            }
        }
        int ne = edgeA.Count;

        // komşuluk ve sınır komşuları
        var neighbors = new List<int>[nv];
        var boundaryNb = new List<int>[nv];
        for (int i = 0; i < nv; i++) neighbors[i] = new List<int>(6);
        for (int e = 0; e < ne; e++)
        {
            int a = edgeA[e], b = edgeB[e];
            neighbors[a].Add(b);
            neighbors[b].Add(a);
            if (edgeFaces[e] != 2)
            {
                (boundaryNb[a] ??= new List<int>(2)).Add(b);
                (boundaryNb[b] ??= new List<int>(2)).Add(a);
            }
        }

        var newPos = new Vector3[nv + ne];
        // eski (çift) köşeler
        Parallel.For(0, nv, v =>
        {
            if (linear)
            {
                newPos[v] = pos[v];
                return;
            }
            var bn = boundaryNb[v];
            if (bn != null)
            {
                newPos[v] = bn.Count == 2 ? pos[v] * 0.75f + (pos[bn[0]] + pos[bn[1]]) * 0.125f : pos[v];
                return;
            }
            var nb = neighbors[v];
            int n = nb.Count;
            if (n < 3)
            {
                newPos[v] = pos[v];
                return;
            }
            float beta = n == 3 ? 3f / 16f : 3f / (8f * n);
            var sum = Vector3.Zero;
            foreach (var j in nb) sum += pos[j];
            newPos[v] = pos[v] * (1 - n * beta) + sum * beta;
        });
        // yeni (tek) kenar köşeleri
        var edgeVertex = new (int a, int b)[ne];
        Parallel.For(0, ne, e =>
        {
            int a = edgeA[e], b = edgeB[e];
            edgeVertex[e] = (a, b);
            if (!linear && edgeFaces[e] == 2 && edgeOpp1[e] >= 0)
                newPos[nv + e] = (pos[a] + pos[b]) * 0.375f + (pos[edgeOpp0[e]] + pos[edgeOpp1[e]]) * 0.125f;
            else
                newPos[nv + e] = (pos[a] + pos[b]) * 0.5f;
        });

        var newIdx = new int[nf * 12];
        var newTags = new int[nf * 4];
        for (int f = 0; f < nf; f++)
        {
            int a = idx[f * 3], b = idx[f * 3 + 1], c = idx[f * 3 + 2];
            int ab = nv + faceEdges[f * 3], bc = nv + faceEdges[f * 3 + 1], ca = nv + faceEdges[f * 3 + 2];
            int o = f * 12;
            newIdx[o + 0] = a; newIdx[o + 1] = ab; newIdx[o + 2] = ca;
            newIdx[o + 3] = b; newIdx[o + 4] = bc; newIdx[o + 5] = ab;
            newIdx[o + 6] = c; newIdx[o + 7] = ca; newIdx[o + 8] = bc;
            newIdx[o + 9] = ab; newIdx[o + 10] = bc; newIdx[o + 11] = ca;
            for (int k = 0; k < 4; k++) newTags[f * 4 + k] = tags[f];
        }
        var parent = new int[nv + ne];
        for (int i = 0; i < nv; i++) parent[i] = i;
        for (int e = 0; e < ne; e++) parent[nv + e] = -1;
        return (newPos, newIdx, newTags, parent, edgeVertex);
    }

    /// <summary>
    /// Bir alt bölümleme adımının geometrik sapması: ince ağdaki her köşenin, kaba ağın düz (doğrusal)
    /// alt bölümlemesindeki karşılığına uzaklığının en büyüğü — kaba LOD'un gerçek hatası.
    /// </summary>
    public static float StepError(Vector3[] coarse, Vector3[] fine, (int a, int b)[] edgeVertex)
    {
        int nv = coarse.Length;
        float err = 0;
        for (int i = 0; i < nv; i++) err = MathF.Max(err, Vector3.Distance(coarse[i], fine[i]));
        for (int e = 0; e < edgeVertex.Length; e++)
        {
            var (a, b) = edgeVertex[e];
            err = MathF.Max(err, Vector3.Distance((coarse[a] + coarse[b]) * 0.5f, fine[nv + e]));
        }
        return err;
    }
}

/// <summary>
/// 64 bit anahtarlar için iyi dağılımlı karşılaştırıcı. Varsayılan long.GetHashCode (üst ^ alt 32 bit), kenar anahtarlarında
/// (a &lt;&lt; 32 | b, a ≈ b) neredeyse hep çakışır ve sözlüğü karesel yavaşlatır; burada splitmix64 son karıştırıcısı kullanılır.
/// </summary>
public sealed class LongKey : IEqualityComparer<long>
{
    public static readonly LongKey Comparer = new();

    public bool Equals(long x, long y) => x == y;

    public int GetHashCode(long v)
    {
        ulong x = (ulong)v;
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebUL;
        x ^= x >> 31;
        return (int)x;
    }
}
