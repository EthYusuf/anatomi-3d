using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anatomi3D.Core.Native;
using Anatomi3D.Core.Pack;

namespace Anatomi3D.AssetBuilder;

/// <summary>İşlenmiş, pakete yazılmaya hazır yapı.</summary>
public sealed class BuiltPart
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Name { get; init; }
    public required string En { get; init; }
    public string? La { get; init; }
    public string Side { get; init; } = "";
    public string Material { get; init; } = "";
    public int Group { get; init; }
    public string? ParentName { get; init; }
    public string? Parent { get; set; }
    public string? Desc { get; set; }
    public AttachmentInfo? Attachment { get; init; }

    // geometri (parça yerel köşe dizisi)
    public Vector3[] Pos = [];
    public Vector3[] Nrm = [];
    public Vector3[] Dir = [];
    public float[] Cavity = [];
    public int[][] Lods = new int[PackFormat.MaxLods][];
    public float[] LodError = new float[PackFormat.MaxLods];
    public PartFlags Flags;
    public int SourceTriangles;
}

public sealed partial class Builder(string rawDir, Action<string> log)
{
    /// <summary>Meshopt LOD'ları için mutlak hata eşikleri (metre)</summary>
    private static readonly float[] LodErrors = [0f, 0.00025f, 0.001f, 0.004f];

    /// <summary>Tampon ve çizim sırası: dıştan içe (ardışık çizim aralıkları birleşebilsin)</summary>
    public static readonly string[] CategoryOrder =
    [
        "skin", "hair", "fascia", "muscle", "vein", "artery", "nerve", "heart", "respiratory", "digestive", "urinary",
        "genital", "endocrine", "serosa", "cns", "meninges", "sense", "ligament", "cartilage", "teeth", "bone", "attachment",
    ];

    private static readonly HashSet<string> FiberCategories = ["muscle", "fascia", "artery", "vein", "nerve", "heart", "ligament"];

    private readonly Dictionary<string, string> latin = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> descFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string[]> groups = [];
    private readonly Dictionary<string, int> groupIndex = [];
    private readonly HashSet<string> usedIds = [];

    public int SkinLevels { get; init; } = 2;

    [GeneratedRegex(@"\.[lr]$")] private static partial Regex SideSuffixRx();
    [GeneratedRegex(@"^\((.*)\)$")] private static partial Regex ParenRx();
    [GeneratedRegex(@"\s+")] private static partial Regex SpacesRx();
    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonAlnumRx();

    public static string BaseName(string n) => SpacesRx().Replace(ParenRx().Replace(SideSuffixRx().Replace(n, ""), "$1"), " ").Trim();
    public static string SideOf(string n) => n.EndsWith(".l", StringComparison.Ordinal) ? "l" : n.EndsWith(".r", StringComparison.Ordinal) ? "r" : "";
    public static string Slug(string s) => NonAlnumRx().Replace(s.ToLowerInvariant(), "-").Trim('-');

    public void Run(string outPath)
    {
        var sw = Stopwatch.StartNew();
        LoadTranslations();

        var parts = new List<BuiltPart>();
        var rawSkin = new List<(RawPart raw, BuiltPart built)>();
        var attachments = new List<(RawPart raw, AttachmentInfo info)>();

        foreach (var file in Source.Files)
        {
            int before = parts.Count;
            foreach (var raw in Source.Read(Path.Combine(rawDir, "za", file + ".glb"), file))
            {
                var att = Source.ParseAttachment(raw.Name);
                if (att != null)
                {
                    if (raw.Indices.Length / 3 > 12) attachments.Add((raw, att));
                    continue;
                }
                if (Source.Skip(raw.Name)) continue;
                if (raw.Indices.Length / 3 <= 12) continue;
                var cat = Source.Classify(file, raw.Path, raw.Name, raw.Material);
                if (cat == null) continue;
                raw.Category = cat;
                var bp = MakePart(raw, cat, null);
                if (bp == null) continue;
                if (cat == "skin") rawSkin.Add((raw, bp));
                parts.Add(bp);
            }
            log($"{file,-18} {parts.Count - before,5} yapı  ({sw.Elapsed.TotalSeconds:F1} sn)");
        }
        log($"Kas yapışma işaretleri: {attachments.Count}");

        // ---------------------------------------------------------------- deri: birleşik Loop alt bölümlemesi
        BuildSkin(rawSkin);
        log($"Deri alt bölümlemesi tamam ({sw.Elapsed.TotalSeconds:F1} sn)");

        // ---------------------------------------------------------------- yapışma işaretlerini kaslarla eşle
        var attachByMuscle = new Dictionary<string, (List<Vector3> o, List<Vector3> e)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (raw, info) in attachments)
        {
            var key = info.Muscle.ToLowerInvariant() + "|" + info.Side;
            if (!attachByMuscle.TryGetValue(key, out var lists)) attachByMuscle[key] = lists = ([], []);
            (info.Kind == 'o' ? lists.o : lists.e).AddRange(raw.Positions);
        }

        // ---------------------------------------------------------------- diğer yapılar
        var others = parts.Where(p => p.Category != "skin").ToList();
        int matched = 0;
        // ham geometri MakePart içinde parçaya işlendi; burada LOD ve yön alanı
        Parallel.ForEach(others, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, p =>
        {
            (List<Vector3> o, List<Vector3> e) att = default;
            bool hasAtt = p.Category == "muscle" && attachByMuscle.TryGetValue(p.En.ToLowerInvariant() + "|" + p.Side, out att)
                          && att.o.Count > 0 && att.e.Count > 0;
            if (hasAtt) Interlocked.Increment(ref matched);
            FinishPart(p, hasAtt ? att.o!.ToArray() : null, hasAtt ? att.e!.ToArray() : null);
        });
        log($"Diğer yapılar tamam; origo/insersiyo eşleşen kas: {matched} ({sw.Elapsed.TotalSeconds:F1} sn)");

        // ---------------------------------------------------------------- yapışma alanları katmanı
        var attParts = new List<BuiltPart>();
        foreach (var (raw, info) in attachments)
        {
            var bp = MakePart(raw, "attachment", info);
            if (bp == null) continue;
            attParts.Add(bp);
        }
        Parallel.ForEach(attParts, p => FinishAttachment(p));
        parts.AddRange(attParts);

        // üst yapı adlarını id'ye çevir
        var byName = new Dictionary<string, string>();
        foreach (var p in parts) byName.TryAdd(p.Name, p.Id);
        foreach (var p in parts) p.Parent = p.ParentName != null && byName.TryGetValue(p.ParentName, out var pid) ? pid : null;

        // sıralama: kategori → grup → ad → taraf
        var catRank = CategoryOrder.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        parts = parts.OrderBy(p => catRank[p.Category]).ThenBy(p => p.Group).ThenBy(p => p.En, StringComparer.Ordinal).ThenBy(p => p.Side).ToList();

        WritePack(parts, outPath);
        log($"Toplam süre {sw.Elapsed.TotalSeconds:F1} sn");
    }

    // ------------------------------------------------------------------------------------------------ çeviriler
    private void LoadTranslations()
    {
        var tr = Path.Combine(rawDir, "za_repo", "Resources", "Translations0.txt");
        foreach (var line in File.ReadLines(tr).Skip(1))
        {
            var cols = line.Split(';');
            if (cols.Length >= 2 && cols[0].Trim().Length > 0 && cols[1].Trim().Length > 0) latin.TryAdd(cols[0].Trim(), cols[1].Trim());
        }
        var dd = Path.Combine(rawDir, "za_repo", "Resources", "Descriptions", "OriginalDescriptions");
        foreach (var f in Directory.EnumerateFiles(dd, "*.txt"))
            descFiles.TryAdd(BaseName(Path.GetFileNameWithoutExtension(f)), f);
        log($"Latince ad: {latin.Count}, açıklama dosyası: {descFiles.Count}");
    }

    // ------------------------------------------------------------------------------------------------ yapı oluşturma
    private BuiltPart? MakePart(RawPart raw, string cat, AttachmentInfo? att)
    {
        var (pos, idx, _) = MeshOps.Weld(raw.Positions, raw.Indices);
        if (idx.Length < 36) return null;
        (pos, idx, _) = MeshOps.Compact(pos, idx);
        MeshOps.OrientConsistently(pos, idx);

        var gPath = raw.Chain.Where(x => x.EndsWith(".g", StringComparison.Ordinal)).Select(x => x[..^2]).ToArray();
        int g;
        lock (groups)
        {
            var gKey = string.Join(" / ", gPath);
            if (!groupIndex.TryGetValue(gKey, out g))
            {
                g = groups.Count;
                groupIndex[gKey] = g;
                groups.Add(gPath);
            }
        }
        string? parentName = raw.Chain.Length > 0 && !raw.Chain[^1].EndsWith(".g", StringComparison.Ordinal) && raw.Chain[^1] != "RootNode" ? raw.Chain[^1] : null;
        string bn = att != null ? att.Muscle : BaseName(raw.Name);
        string id;
        lock (usedIds)
        {
            id = Slug(SideSuffixRx().Replace(raw.Name, m => "-" + m.Value[1..]));
            if (id.Length == 0) id = "part-" + usedIds.Count;
            if (att != null) id = "att-" + id;
            while (!usedIds.Add(id)) id += "_";
        }
        return new BuiltPart
        {
            Id = id, Category = cat, Name = raw.Name, En = bn,
            La = att == null && latin.TryGetValue(bn, out var la) ? la : null,
            Side = att != null ? att.Side : SideOf(raw.Name),
            Material = raw.Material, Group = g, ParentName = att == null ? parentName : null, Attachment = att,
            Desc = att == null && descFiles.ContainsKey(bn) ? Slug(bn) : null,
            Pos = pos, SourceTriangles = raw.Indices.Length / 3,
            Lods = [idx, idx, idx, idx],
        };
    }

    /// <summary>Normaller, yön alanı, kavite, LOD'lar ve önbellek sıralaması.</summary>
    private static void FinishPart(BuiltPart p, Vector3[]? origins, Vector3[]? insertions)
    {
        var pos = p.Pos;
        var idx = p.Lods[0];
        p.Nrm = MeshOps.Normals(pos, idx);
        if (MeshOps.IsClosed(idx)) p.Flags |= PartFlags.Closed;
        var (start, list) = MeshOps.Adjacency(pos.Length, idx);
        var (c, _, _, _) = MeshOps.Bounds(pos);
        var (axis, _) = MeshOps.PrincipalAxes(pos, c);

        if (origins != null && insertions != null)
        {
            p.Dir = Fibers.FromAttachments(pos, p.Nrm, origins, insertions);
            Fibers.SmoothAxial(p.Dir, p.Nrm, start, list, 4);
        }
        else if (FiberCategories.Contains(p.Category))
        {
            p.Dir = Fibers.MinCurvature(pos, p.Nrm, start, list, axis);
            Fibers.SmoothAxial(p.Dir, p.Nrm, start, list, 6);
        }
        else p.Dir = Fibers.ProjectedAxis(p.Nrm, axis);
        p.Cavity = MeshOps.Cavity(pos, p.Nrm, idx);

        BuildLods(p, pos, idx, LodErrors, prune: true);
    }

    private static void FinishAttachment(BuiltPart p)
    {
        var pos = p.Pos;
        var idx = p.Lods[0];
        p.Nrm = MeshOps.Normals(pos, idx);
        p.Dir = Fibers.ProjectedAxis(p.Nrm, Vector3.UnitY);
        p.Cavity = new float[pos.Length];
        p.Flags |= PartFlags.Attachment;
        // bindirme katmanı: tam çözünürlük gerekmez
        BuildLods(p, pos, idx, [0.00012f, 0.0005f, 0.002f, 0.006f], prune: false);
    }

    private static void BuildLods(BuiltPart p, Vector3[] pos, int[] idx0, float[] errors, bool prune)
    {
        var fpos = new float[pos.Length * 3];
        for (int i = 0; i < pos.Length; i++) { fpos[i * 3] = pos[i].X; fpos[i * 3 + 1] = pos[i].Y; fpos[i * 3 + 2] = pos[i].Z; }
        var baseIdx = Array.ConvertAll(idx0, x => (uint)x);
        var lods = new uint[PackFormat.MaxLods][];
        var errs = new float[PackFormat.MaxLods];
        for (int l = 0; l < PackFormat.MaxLods; l++)
        {
            if (errors[l] <= 0)
            {
                lods[l] = (uint[])baseIdx.Clone();
                errs[l] = 0;
            }
            else
            {
                int target = Math.Max(36, baseIdx.Length / 100 / 3 * 3);
                var opts = Meshopt.SimplifyOptions.ErrorAbsolute | (prune ? Meshopt.SimplifyOptions.Prune : 0);
                var s = Meshopt.Simplify(baseIdx, fpos, target, errors[l], opts, out float e);
                if (l == 0)
                {
                    // bindirme katmanları LOD0'da da hafifçe sadeleştirilir
                    lods[0] = s.Length >= 36 ? s : (uint[])baseIdx.Clone();
                    errs[0] = s.Length >= 36 ? e : 0;
                }
                // anlamlı bir kazanç yoksa ya da yapı yok olduysa bir önceki düzeyi kullan
                else if (s.Length < 36 || s.Length > lods[l - 1].Length * 0.85)
                {
                    lods[l] = lods[l - 1];
                    errs[l] = errs[l - 1];
                    continue;
                }
                else
                {
                    lods[l] = s;
                    errs[l] = Math.Max(e, errs[l - 1]);
                }
            }
            Meshopt.OptimizeVertexCache(lods[l], pos.Length);
        }
        // köşeleri LOD0 erişim sırasına göre diz (tüm LOD'lar aynı köşe dizisini kullanır)
        var remap = Meshopt.OptimizeVertexFetchRemap(lods[0], pos.Length, out int unique);
        var npos = new Vector3[unique];
        var nnrm = new Vector3[unique];
        var ndir = new Vector3[unique];
        var ncav = new float[unique];
        for (int i = 0; i < pos.Length; i++)
        {
            uint r = remap[i];
            if (r == uint.MaxValue) continue;
            npos[r] = pos[i]; nnrm[r] = p.Nrm[i]; ndir[r] = p.Dir[i]; ncav[r] = p.Cavity[i];
        }
        p.Pos = npos; p.Nrm = nnrm; p.Dir = ndir; p.Cavity = ncav;
        for (int l = 0; l < PackFormat.MaxLods; l++)
        {
            if (l > 0 && ReferenceEquals(lods[l], lods[l - 1]))
            {
                p.Lods[l] = p.Lods[l - 1];
                p.LodError[l] = p.LodError[l - 1];
                continue;
            }
            var src = lods[l];
            var dst = new int[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = (int)remap[src[i]];
            p.Lods[l] = dst;
            p.LodError[l] = errs[l];
        }
    }

    // ------------------------------------------------------------------------------------------------ deri
    /// <summary>
    /// Tüm deri bölgeleri tek ağda kaynaklanır; boşluk kalan sınırlar yakın köşelere yapıştırılır; birleşik ağa
    /// <see cref="SkinLevels"/> düzey Loop alt bölümlemesi uygulanır. Her düzey kendi köşeleriyle bir LOD olur ve yapılara
    /// geri bölünür: komşu bölgeler her düzeyde birebir aynı sınır köşelerini paylaştığından çatlak oluşmaz.
    /// </summary>
    private void BuildSkin(List<(RawPart raw, BuiltPart built)> skin)
    {
        var sw = Stopwatch.StartNew();
        void T(string what) => log($"    deri/{what}: {sw.Elapsed.TotalSeconds:F1} sn");
        // birleşik ağ
        var allPos = new List<Vector3>();
        var allIdx = new List<int>();
        var tags = new List<int>();
        for (int s = 0; s < skin.Count; s++)
        {
            var bp = skin[s].built;
            int b = allPos.Count;
            allPos.AddRange(bp.Pos);
            foreach (var i in bp.Lods[0]) allIdx.Add(b + i);
            for (int t = 0; t < bp.Lods[0].Length / 3; t++) tags.Add(s);
        }
        var (wpos, widx, faceMap) = MeshOps.Weld(allPos.ToArray(), allIdx.ToArray(), 1e5f);
        var wtags = faceMap.Select(f => tags[f]).ToArray();
        int boundaryBefore = CountBoundary(widx);
        // küçük aralıkları kapat: 0,6 mm'den yakın sınır köşelerini birleştir
        (wpos, widx, wtags) = SnapBoundaries(wpos, widx, wtags, 0.0006f);
        int boundaryAfter = CountBoundary(widx);
        (widx, wtags) = FillSmallHoles(wpos, widx, wtags, maxVerts: 16, maxPerimeter: 0.035f);
        log($"Deri: küçük delikler dolduruldu, sınır kenarı → {CountBoundary(widx)}");
        int flippedSkin = MeshOps.OrientConsistently(wpos, widx);
        log($"Deri: yönü düzeltilen üçgen {flippedSkin}");
        T("kaynak+yapıştırma");
        log($"Deri: {skin.Count} bölge, {widx.Length / 3} üçgen, sınır kenarı {boundaryBefore} → {boundaryAfter}");

        // Loop yaklaştırıcı bir şemadır: yüzey dışbükey yerlerde (parmak ucu, burun, kulak) içeri çekilir.
        // Hacmi korumak için kontrol ağı yinelemeli olarak şişirilir; en ince düzeyin köşeleri özgün
        // köşelerin tam üzerine gelir (interpolasyonlu Loop).
        var control = (Vector3[])wpos.Clone();
        List<(Vector3[] pos, int[] idx, int[] tags)> Levels(Vector3[] ctrl)
        {
            var lv = new List<(Vector3[] pos, int[] idx, int[] tags)> { (ctrl, widx, wtags) };
            for (int l = 0; l < SkinLevels; l++)
            {
                var (p, i, t, _, _) = LoopSubdivision.Subdivide(lv[^1].pos, lv[^1].idx, lv[^1].tags);
                lv.Add((p, i, t));
            }
            return lv;
        }
        float maxDev = 0;
        for (int it = 0; it < 12 && SkinLevels > 0; it++)
        {
            var fine = Levels(control)[^1].pos;
            maxDev = 0;
            for (int v = 0; v < wpos.Length; v++)
            {
                var d = wpos[v] - fine[v];
                maxDev = MathF.Max(maxDev, d.Length());
                control[v] += d * 0.9f;
            }
        }
        var levels = Levels(control);
        // en kaba düzey özgün ağın kendisidir (kontrol ağı şişirilmiş olduğundan çizilmez)
        levels[0] = (wpos, widx, wtags);
        T($"alt bölümleme (interpolasyon kalan sapma {maxDev * 1000:F2} mm)");
        // Düzey k'nın en ince düzeye göre hatası: düzey k'nın düz (orta nokta) bölmesiyle en ince yüzey arasındaki en büyük uzaklık
        var levelErr = new float[levels.Count];
        for (int k = 0; k < levels.Count - 1; k++)
        {
            var flat = levels[k];
            for (int l = k; l < levels.Count - 1; l++)
            {
                var (p, i, t, _, _) = LoopSubdivision.Subdivide(flat.pos, flat.idx, flat.tags, linear: true);
                flat = (p, i, t);
            }
            var finest = levels[^1].pos;
            float e = 0;
            for (int v = 0; v < finest.Length; v++) e = MathF.Max(e, Vector3.Distance(flat.pos[v], finest[v]));
            levelErr[k] = e;
        }
        log($"Deri düzeyleri: {string.Join(" → ", levels.Select(l => l.idx.Length / 3))} üçgen; hata {string.Join(" / ", levelErr.Select(e => (e * 1000).ToString("F2") + " mm"))}");

        // düzey başına normaller, kavite (birleşik ağda → sınırlarda sürekli)
        var levelNrm = new Vector3[levels.Count][];
        var levelCav = new float[levels.Count][];
        Parallel.For(0, levels.Count, l =>
        {
            levelNrm[l] = MeshOps.Normals(levels[l].pos, levels[l].idx);
            levelCav[l] = MeshOps.Cavity(levels[l].pos, levelNrm[l], levels[l].idx);
        });

        T("normal+kavite");
        // LOD eşlemesi: LOD0 = en ince düzey, sonra kabalaşan düzeyler
        int nl = levels.Count;
        var lodLevel = new int[PackFormat.MaxLods];
        for (int l = 0; l < PackFormat.MaxLods; l++) lodLevel[l] = Math.Max(0, nl - 1 - l);

        // yapılara böl
        Parallel.For(0, skin.Count, s =>
        {
            var bp = skin[s].built;
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var cav = new List<float>();
            var lodIdx = new int[PackFormat.MaxLods][];
            var levelLocal = new Dictionary<int, int[]>(); // düzey → yerel indeksler (aynı düzeyi paylaşan LOD'lar için)
            for (int l = 0; l < PackFormat.MaxLods; l++)
            {
                int lev = lodLevel[l];
                if (levelLocal.TryGetValue(lev, out var existing))
                {
                    lodIdx[l] = existing;
                    continue;
                }
                var (lp, li, lt) = levels[lev];
                var map = new Dictionary<int, int>();
                var outIdx = new List<int>();
                for (int f = 0; f < lt.Length; f++)
                {
                    if (lt[f] != s) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = li[f * 3 + k];
                        if (!map.TryGetValue(v, out int local))
                        {
                            local = pos.Count;
                            map[v] = local;
                            pos.Add(lp[v]);
                            nrm.Add(levelNrm[lev][v]);
                            cav.Add(levelCav[lev][v]);
                        }
                        outIdx.Add(local);
                    }
                }
                var arr = outIdx.ToArray();
                var u = Array.ConvertAll(arr, x => (uint)x);
                Meshopt.OptimizeVertexCache(u, pos.Count);
                arr = Array.ConvertAll(u, x => (int)x);
                levelLocal[lev] = arr;
                lodIdx[l] = arr;
            }
            bp.Pos = pos.ToArray();
            bp.Nrm = nrm.ToArray();
            bp.Cavity = cav.ToArray();
            var (c, _, _, _) = MeshOps.Bounds(bp.Pos);
            var (axis, _) = MeshOps.PrincipalAxes(bp.Pos, c);
            bp.Dir = Fibers.ProjectedAxis(bp.Nrm, axis);
            bp.Lods = lodIdx;
            for (int l = 0; l < PackFormat.MaxLods; l++) bp.LodError[l] = levelErr[lodLevel[l]];
            bp.Flags |= PartFlags.Subdivided;
        });
        T("bölme");
    }

    /// <summary>
    /// Kaynak modeldeki küçük delikleri (parmak uçları, dudak çevresi) yelpaze üçgenlemesiyle kapatır; büyük gerçek
    /// açıklıklar (dış kulak yolu) korunur. Yeni üçgenler komşu yüzün yapı etiketini alır.
    /// </summary>
    private static (int[] idx, int[] tags) FillSmallHoles(Vector3[] pos, int[] idx, int[] tags, int maxVerts, float maxPerimeter)
    {
        var count = new Dictionary<long, int>(idx.Length, LongKey.Comparer);
        var edgeFace = new Dictionary<long, int>(idx.Length, LongKey.Comparer);
        for (int t = 0; t < idx.Length; t += 3)
            for (int e = 0; e < 3; e++)
            {
                long k = MeshOps.EdgeKey(idx[t + e], idx[t + (e + 1) % 3]);
                count[k] = count.GetValueOrDefault(k) + 1;
                edgeFace[k] = t / 3;
            }
        // sınır kenarları: yönlü (yüzün dolaştığı yön), böylece döngü sırası çıkarılabilir
        var next = new Dictionary<int, int>();
        for (int t = 0; t < idx.Length; t += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = idx[t + e], b = idx[t + (e + 1) % 3];
                if (count[MeshOps.EdgeKey(a, b)] == 1) next.TryAdd(b, a); // delik, yüz yönünün tersine dolaşılır
            }
        var outIdx = new List<int>(idx);
        var outTags = new List<int>(tags);
        var used = new HashSet<int>();
        foreach (var start in next.Keys.ToList())
        {
            if (used.Contains(start)) continue;
            var loop = new List<int>();
            int v = start;
            bool ok = true;
            while (true)
            {
                if (!used.Add(v)) { ok = v == start; break; }
                loop.Add(v);
                if (!next.TryGetValue(v, out v)) { ok = false; break; }
                if (loop.Count > maxVerts) { ok = false; break; }
            }
            if (!ok || loop.Count < 3) continue;
            float per = 0;
            for (int i = 0; i < loop.Count; i++) per += Vector3.Distance(pos[loop[i]], pos[loop[(i + 1) % loop.Count]]);
            if (per > maxPerimeter) continue;
            int tag = tags[edgeFace[MeshOps.EdgeKey(loop[0], loop[1])]];
            for (int i = 1; i + 1 < loop.Count; i++)
            {
                outIdx.Add(loop[0]); outIdx.Add(loop[i]); outIdx.Add(loop[i + 1]);
                outTags.Add(tag);
            }
        }
        return (outIdx.ToArray(), outTags.ToArray());
    }

    /// <summary>Kalan sınır kenarlarını döngülere ayırıp konum ve uzunlukla raporlar (tanılama).</summary>
    private void LogBoundaryLoops(Vector3[] pos, int[] idx)
    {
        var count = new Dictionary<long, int>(idx.Length, LongKey.Comparer);
        for (int t = 0; t < idx.Length; t += 3)
            for (int e = 0; e < 3; e++)
            {
                long k = MeshOps.EdgeKey(idx[t + e], idx[t + (e + 1) % 3]);
                count[k] = count.GetValueOrDefault(k) + 1;
            }
        var adj = new Dictionary<int, List<int>>();
        foreach (var (k, c) in count)
        {
            if (c != 1) continue;
            int a = (int)(k >> 32), b = (int)(k & 0xffffffff);
            (adj.TryGetValue(a, out var la) ? la : adj[a] = []).Add(b);
            (adj.TryGetValue(b, out var lb) ? lb : adj[b] = []).Add(a);
        }
        var seen = new HashSet<int>();
        foreach (var start in adj.Keys)
        {
            if (!seen.Add(start)) continue;
            var stack = new Stack<int>([start]);
            var verts = new List<int> { start };
            float len = 0;
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                foreach (var u in adj[v])
                {
                    if (!seen.Add(u)) continue;
                    len += Vector3.Distance(pos[u], pos[v]);
                    verts.Add(u);
                    stack.Push(u);
                }
            }
            var c = verts.Aggregate(Vector3.Zero, (acc, v) => acc + pos[v]) / verts.Count;
            log($"    sınır döngüsü: {verts.Count} köşe, çevre {len * 1000:F1} mm, merkez ({c.X:F3}, {c.Y:F3}, {c.Z:F3})");
        }
    }

    private static int CountBoundary(int[] idx)
    {
        var count = new Dictionary<long, int>(idx.Length, LongKey.Comparer);
        for (int t = 0; t < idx.Length; t += 3)
            for (int e = 0; e < 3; e++)
            {
                long k = MeshOps.EdgeKey(idx[t + e], idx[t + (e + 1) % 3]);
                count[k] = count.GetValueOrDefault(k) + 1;
            }
        return count.Values.Count(c => c == 1);
    }

    /// <summary>Sınır köşelerini <paramref name="tol"/> mesafesindeki başka sınır köşelerine yapıştırır.</summary>
    private static (Vector3[] pos, int[] idx, int[] tags) SnapBoundaries(Vector3[] pos, int[] idx, int[] tags, float tol)
    {
        var count = new Dictionary<long, int>(idx.Length, LongKey.Comparer);
        for (int t = 0; t < idx.Length; t += 3)
            for (int e = 0; e < 3; e++)
            {
                long k = MeshOps.EdgeKey(idx[t + e], idx[t + (e + 1) % 3]);
                count[k] = count.GetValueOrDefault(k) + 1;
            }
        var isB = new bool[pos.Length];
        foreach (var (k, c) in count)
            if (c == 1) { isB[(int)(k >> 32)] = true; isB[(int)(k & 0xffffffff)] = true; }
        var bverts = Enumerable.Range(0, pos.Length).Where(i => isB[i]).ToArray();
        // ızgara
        float cell = tol * 2;
        var grid = new Dictionary<(int, int, int), List<int>>();
        (int, int, int) Cell(Vector3 p) => ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
        foreach (var v in bverts)
        {
            var c = Cell(pos[v]);
            if (!grid.TryGetValue(c, out var l)) grid[c] = l = [];
            l.Add(v);
        }
        var target = Enumerable.Range(0, pos.Length).ToArray();
        int Find(int x) { while (target[x] != x) x = target[x] = target[target[x]]; return x; }
        foreach (var v in bverts)
        {
            var (cx, cy, cz) = Cell(pos[v]);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var l)) continue;
                        foreach (var u in l)
                        {
                            if (u == v) continue;
                            if (Vector3.DistanceSquared(pos[u], pos[v]) > tol * tol) continue;
                            int a = Find(u), b = Find(v);
                            if (a != b) target[Math.Max(a, b)] = Math.Min(a, b);
                        }
                    }
        }
        // birleşen kümelerin ortalama konumu
        var sum = new Dictionary<int, (Vector3 s, int n)>();
        for (int i = 0; i < pos.Length; i++)
        {
            int r = Find(i);
            if (r == i && !isB[i]) continue;
            var cur = sum.GetValueOrDefault(r);
            sum[r] = (cur.s + pos[i], cur.n + 1);
        }
        var npos = (Vector3[])pos.Clone();
        foreach (var (r, (s, n)) in sum) npos[r] = s / n;
        var nidx = new List<int>(idx.Length);
        var ntags = new List<int>(tags.Length);
        var seen = new HashSet<(int, int, int)>();
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = Find(idx[t]), b = Find(idx[t + 1]), c = Find(idx[t + 2]);
            if (a == b || b == c || a == c) continue;
            var key = (Math.Min(a, Math.Min(b, c)), a + b + c - Math.Min(a, Math.Min(b, c)) - Math.Max(a, Math.Max(b, c)), Math.Max(a, Math.Max(b, c)));
            if (!seen.Add(key)) continue;
            nidx.Add(a); nidx.Add(b); nidx.Add(c);
            ntags.Add(tags[t / 3]);
        }
        var (cp, ci, _) = MeshOps.Compact(npos, nidx.ToArray());
        return (cp, ci, ntags.ToArray());
    }

    // ------------------------------------------------------------------------------------------------ paket
    private void WritePack(List<BuiltPart> parts, string outPath)
    {
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        long totalV = 0;
        foreach (var p in parts)
        {
            foreach (var v in p.Pos) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            totalV += p.Pos.Length;
        }
        mn -= new Vector3(0.001f);
        mx += new Vector3(0.001f);
        var scale = (mx - mn) / 65535f;
        var vertices = new PackVertex[totalV];
        var records = new PartRecord[parts.Count];
        var lodLists = new List<uint>[PackFormat.MaxLods];
        for (int l = 0; l < PackFormat.MaxLods; l++) lodLists[l] = new List<uint>(1 << 22);

        uint vOff = 0;
        for (int pi = 0; pi < parts.Count; pi++)
        {
            var p = parts[pi];
            var rec = new PartRecord { VertexOffset = vOff, VertexCount = (uint)p.Pos.Length, Flags = (uint)p.Flags };
            for (int i = 0; i < p.Pos.Length; i++)
            {
                var q = (p.Pos[i] - mn) / scale;
                var (nx, ny) = Octahedral.EncodeSnorm16(p.Nrm[i]);
                var (dx, dy) = Octahedral.EncodeSnorm8(p.Dir[i]);
                vertices[vOff + i] = new PackVertex
                {
                    X = (ushort)Math.Clamp(MathF.Round(q.X), 0, 65535),
                    Y = (ushort)Math.Clamp(MathF.Round(q.Y), 0, 65535),
                    Z = (ushort)Math.Clamp(MathF.Round(q.Z), 0, 65535),
                    Part = (ushort)pi,
                    NormalX = nx, NormalY = ny, DirX = dx, DirY = dy,
                    Occlusion = 255,
                    Cavity = (byte)Math.Clamp(MathF.Round(128 + p.Cavity[i] * 127), 0, 255),
                };
            }
            for (int l = 0; l < PackFormat.MaxLods; l++)
            {
                var idx = p.Lods[l];
                uint off = (uint)lodLists[l].Count;
                foreach (var i in idx) lodLists[l].Add(vOff + (uint)i);
                rec.SetLod(l, new LodRange { IndexOffset = off, IndexCount = (uint)idx.Length, Error = p.LodError[l] });
            }
            var (c, r, bmn, bmx) = MeshOps.Bounds(p.Pos);
            var (axis, minor) = MeshOps.PrincipalAxes(p.Pos, c);
            rec.Center = c; rec.Radius = r; rec.Min = bmn; rec.Max = bmx; rec.Axis = axis; rec.Minor = minor;
            records[pi] = rec;
            vOff += (uint)p.Pos.Length;
        }

        // üst veri
        var meta = new
        {
            format = "Anatomi 3D model paketi",
            source = "Z-Anatomy (Lluís Vinent Juanico ve katkıda bulunanlar), CC BY-SA 4.0 — https://www.z-anatomy.com",
            license = "CC BY-SA 4.0",
            built = DateTime.UtcNow.ToString("u"),
            skinLevels = SkinLevels,
            groups = groups.Select(path => path.Select(g => latin.TryGetValue(g, out var la) ? new[] { g, la } : new[] { g }).ToArray()).ToArray(),
            parts = parts.Select(p => new MetaPart
            {
                id = p.Id, cat = p.Category, en = p.En, la = p.La, side = p.Side.Length > 0 ? p.Side : null,
                mat = p.Material.Length > 0 ? p.Material : null, g = p.Group, parent = p.Parent, desc = p.Desc,
                att = p.Attachment == null ? null : new MetaAttachment { m = p.Attachment.Muscle, k = p.Attachment.Kind.ToString(), n = p.Attachment.Number },
            }).ToArray(),
        };
        var jsonOpts = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        string metaJson = JsonSerializer.Serialize(meta, jsonOpts);

        // açıklamalar
        var descs = new SortedDictionary<string, string>();
        foreach (var p in parts)
        {
            if (p.Desc == null || descs.ContainsKey(p.Desc)) continue;
            if (descFiles.TryGetValue(p.En, out var f)) descs[p.Desc] = File.ReadAllText(f);
        }
        string descJson = JsonSerializer.Serialize(descs);

        var header = new PackHeader { PartCount = (uint)parts.Count, VertexCount = (uint)totalV, QuantMin = mn, QuantMax = mx };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        log("Paket yazılıyor:");
        PackWriter.Write(outPath, header, records, metaJson, descJson, vertices, lodLists.Select(l => l.ToArray()).ToArray(), log);

        // istatistik
        log("");
        log($"{"kategori",-12} {"yapı",5} {"kapalı",6} {"kaynak",10} {"LOD0",10} {"LOD1",10} {"LOD2",10} {"LOD3",10}");
        foreach (var cat in CategoryOrder)
        {
            var list = parts.Where(p => p.Category == cat).ToList();
            if (list.Count == 0) continue;
            long src = list.Sum(p => (long)p.SourceTriangles);
            var l = Enumerable.Range(0, PackFormat.MaxLods).Select(k => list.Sum(p => (long)p.Lods[k].Length / 3)).ToArray();
            int closed = list.Count(p => (p.Flags & PartFlags.Closed) != 0);
            log($"{cat,-12} {list.Count,5} {closed,6} {src,10:N0} {l[0],10:N0} {l[1],10:N0} {l[2],10:N0} {l[3],10:N0}");
        }
        long t0 = parts.Sum(p => (long)p.Lods[0].Length / 3);
        log($"TOPLAM {parts.Count} yapı, {totalV:N0} köşe, LOD0 {t0:N0} üçgen, dosya {new FileInfo(outPath).Length / 1e6:F1} MB");
    }

    private sealed class MetaPart
    {
        public string id { get; set; } = "";
        public string cat { get; set; } = "";
        public string en { get; set; } = "";
        public string? la { get; set; }
        public string? side { get; set; }
        public string? mat { get; set; }
        public int g { get; set; }
        public string? parent { get; set; }
        public string? desc { get; set; }
        public MetaAttachment? att { get; set; }
    }

    private sealed class MetaAttachment
    {
        public string m { get; set; } = "";
        public string k { get; set; } = "";
        public int n { get; set; }
    }
}
