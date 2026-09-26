using System.Numerics;

namespace Anatomi3D.AssetBuilder;

/// <summary>
/// Köşe başına yüzey yön alanı (kas lifi, tendon, damar ve sinir boyunca uzanan yön).
///
/// - Kaslar: Z-Anatomy'deki origo (.o) ve insersiyo (.e) işaret alanlarından; her köşe için en yakın origo
///   noktasından en yakın insersiyo noktasına giden yön yüzeye izdüşürülür. Böylece yelpaze biçimli kaslarda
///   (ör. pectoralis major) lifler gerçekteki gibi yelpaze şeklinde açılır.
/// - Tüp biçimli yapılar (damar, sinir, tendon) ve işaretsiz kaslar: en küçük asal eğrilik yönü
///   (silindirde eksen boyunca), belirsiz bölgelerde yapının ana eksenine karışır.
/// Tüm alanlar yönsüz (±) kabul edilip komşuluk üzerinde yumuşatılır.
/// </summary>
public static class Fibers
{
    public static Vector3[] ProjectedAxis(Vector3[] nrm, Vector3 axis)
    {
        var d = new Vector3[nrm.Length];
        for (int i = 0; i < nrm.Length; i++) d[i] = Tangent(axis, nrm[i]);
        return d;
    }

    private static Vector3 Tangent(Vector3 v, Vector3 n)
    {
        var t = v - n * Vector3.Dot(n, v);
        float l = t.Length();
        if (l > 1e-6f) return t / l;
        // eksen normale paralel: herhangi bir teğet seç
        var alt = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(n, alt));
    }

    /// <summary>En küçük asal eğrilik yönü; belirsiz yerlerde <paramref name="axis"/> yönüne karışır.</summary>
    public static Vector3[] MinCurvature(Vector3[] pos, Vector3[] nrm, int[] adjStart, int[] adjList, Vector3 axis)
    {
        var dirs = new Vector3[pos.Length];
        Parallel.For(0, pos.Length, i =>
        {
            var n = nrm[i];
            var t1 = Tangent(axis, n);
            var t2 = Vector3.Cross(n, t1);
            // [a b; b c] ikinci temel formu en küçük kareler ile
            double s11 = 0, s12 = 0, s13 = 0, s22 = 0, s23 = 0, s33 = 0, r1 = 0, r2 = 0, r3 = 0;
            for (int k = adjStart[i]; k < adjStart[i + 1]; k++)
            {
                int j = adjList[k];
                var d = pos[j] - pos[i];
                float len2 = d.LengthSquared();
                if (len2 < 1e-16f) continue;
                float kappa = Vector3.Dot(nrm[j] - n, d) / len2;
                float u1 = Vector3.Dot(d, t1), u2 = Vector3.Dot(d, t2);
                float ul = MathF.Sqrt(u1 * u1 + u2 * u2);
                if (ul < 1e-9f) continue;
                u1 /= ul; u2 /= ul;
                double x1 = u1 * u1, x2 = 2 * u1 * u2, x3 = u2 * u2;
                s11 += x1 * x1; s12 += x1 * x2; s13 += x1 * x3; s22 += x2 * x2; s23 += x2 * x3; s33 += x3 * x3;
                r1 += x1 * kappa; r2 += x2 * kappa; r3 += x3 * kappa;
            }
            // 3x3 simetrik sistemi Cramer ile çöz (küçük sönümleme)
            s11 += 1e-6; s22 += 1e-6; s33 += 1e-6;
            double det = s11 * (s22 * s33 - s23 * s23) - s12 * (s12 * s33 - s23 * s13) + s13 * (s12 * s23 - s22 * s13);
            if (Math.Abs(det) < 1e-18)
            {
                dirs[i] = t1;
                return;
            }
            double a = (r1 * (s22 * s33 - s23 * s23) - s12 * (r2 * s33 - s23 * r3) + s13 * (r2 * s23 - s22 * r3)) / det;
            double b = (s11 * (r2 * s33 - s23 * r3) - r1 * (s12 * s33 - s23 * s13) + s13 * (s12 * r3 - r2 * s13)) / det;
            double c = (s11 * (s22 * r3 - r2 * s23) - s12 * (s12 * r3 - r2 * s13) + r1 * (s12 * s23 - s22 * s13)) / det;
            // öz ayrışım
            double tr = a + c, dd = Math.Sqrt(Math.Max(0, (a - c) * (a - c) / 4 + b * b));
            double l1 = tr / 2 + dd, l2 = tr / 2 - dd;
            // küçük mutlak eğriliğe ait öz vektör
            double lam = Math.Abs(l1) < Math.Abs(l2) ? l1 : l2;
            double ex = b, ey = lam - a;
            if (Math.Abs(ex) + Math.Abs(ey) < 1e-12) { ex = lam - c; ey = b; }
            if (Math.Abs(ex) + Math.Abs(ey) < 1e-12) { ex = 1; ey = 0; }
            var dir = Vector3.Normalize(t1 * (float)ex + t2 * (float)ey);
            // anizotropi zayıfsa (küresel bölge) ana eksene karış
            double big = Math.Max(Math.Abs(l1), Math.Abs(l2)), small = Math.Min(Math.Abs(l1), Math.Abs(l2));
            float aniso = big > 1e-9 ? (float)Math.Clamp((big - small) / big, 0, 1) : 0;
            if (Vector3.Dot(dir, t1) < 0) dir = -dir;
            dirs[i] = Tangent(Vector3.Lerp(t1, dir, MathF.Pow(aniso, 0.5f)), n);
        });
        return dirs;
    }

    /// <summary>Origo ve insersiyo noktalarından kas lifi yönü.</summary>
    public static Vector3[] FromAttachments(Vector3[] pos, Vector3[] nrm, Vector3[] origins, Vector3[] insertions)
    {
        var o = Subsample(origins, 384);
        var e = Subsample(insertions, 384);
        var dirs = new Vector3[pos.Length];
        Parallel.For(0, pos.Length, i =>
        {
            var p = pos[i];
            var no = Nearest(o, p);
            var ni = Nearest(e, p);
            dirs[i] = Tangent(ni - no, nrm[i]);
        });
        return dirs;
    }

    private static Vector3 Nearest(Vector3[] pts, Vector3 p)
    {
        float best = float.MaxValue;
        var bp = pts[0];
        foreach (var q in pts)
        {
            float d = Vector3.DistanceSquared(p, q);
            if (d < best) { best = d; bp = q; }
        }
        return bp;
    }

    private static Vector3[] Subsample(Vector3[] pts, int max)
    {
        if (pts.Length <= max) return pts;
        var r = new Vector3[max];
        double step = pts.Length / (double)max;
        for (int i = 0; i < max; i++) r[i] = pts[(int)(i * step)];
        return r;
    }

    /// <summary>Yönsüz yön alanını komşuluk üzerinde yumuşatır (işaretler hizalanarak).</summary>
    public static void SmoothAxial(Vector3[] dirs, Vector3[] nrm, int[] adjStart, int[] adjList, int iterations)
    {
        var cur = dirs;
        for (int it = 0; it < iterations; it++)
        {
            var nxt = new Vector3[cur.Length];
            var src = cur;
            Parallel.For(0, src.Length, i =>
            {
                var d = src[i] * 2f;
                for (int k = adjStart[i]; k < adjStart[i + 1]; k++)
                {
                    var q = src[adjList[k]];
                    d += Vector3.Dot(q, src[i]) < 0 ? -q : q;
                }
                nxt[i] = Tangent(d, nrm[i]);
            });
            cur = nxt;
        }
        Array.Copy(cur, dirs, dirs.Length);
    }
}
