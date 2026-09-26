using System.Text;
using Anatomi3D.Core.Model;

namespace Anatomi3D.Core.Content;

/// <summary>
/// İçerik kapsama raporu (içerik ekibi için): her yapının Türkçe adı ve bilgi girdisi var mı, girdi kendisine mi ait
/// yoksa üst yapıdan / gruptan mı kalıtılıyor; hiçbir yapıyla eşleşmeyen (büyük olasılıkla yanlış yazılmış) anahtarlar.
/// </summary>
public static class ContentReport
{
    public static string Build(AnatomyModel model)
    {
        var parts = model.Parts.Where(p => !p.IsAttachment).ToArray();
        // sağ/sol kopyaları tek yapı say
        var unique = parts.GroupBy(p => (p.En, p.Category)).Select(g => g.First()).ToArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<(Category cat, string en, string? tr, string status, string key, string path, string id)>();
        foreach (var p in unique)
        {
            var (e, inherited) = ContentLibrary.For(p, model.Groups[p.Group]);
            if (e != null) used.Add(e.Key);
            rows.Add((p.Category, p.En, p.Tr, e == null ? "yok" : inherited ? "kalıtım" : "kendi", e?.Key ?? "",
                string.Join(" / ", model.Groups[p.Group].Select(g => g[0])) + (p.Parent != null ? " > " + p.Parent.En : ""), p.Id));
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Anatomi 3D — içerik kapsama raporu");
        sb.AppendLine($"# Yapı (tekil, taraf birleşik): {unique.Length}   Bilgi girdisi: {ContentLibrary.Count}");
        sb.AppendLine($"# Türkçe adı olan: {unique.Count(p => p.Tr != null)} ({Pct(unique.Count(p => p.Tr != null), unique.Length)})");
        sb.AppendLine($"# Kendi girdisi: {rows.Count(r => r.status == "kendi")}   Kalıtımla: {rows.Count(r => r.status == "kalıtım")}   Girdisiz: {rows.Count(r => r.status == "yok")}");
        sb.AppendLine("#");
        sb.AppendLine("# Kategori            yapı   TR ad   kendi  kalıtım  yok");
        foreach (var g in rows.GroupBy(r => r.cat).OrderBy(g => g.Key))
        {
            var info = Categories.Get(g.Key);
            sb.AppendLine($"# {info.Tr,-18} {g.Count(),5}  {g.Count(r => r.tr != null),6}  {g.Count(r => r.status == "kendi"),5}  {g.Count(r => r.status == "kalıtım"),7}  {g.Count(r => r.status == "yok"),4}");
        }

        var orphans = ContentLibrary.All.Keys.Where(k => !used.Contains(k)).OrderBy(k => k).ToList();
        sb.AppendLine("#");
        sb.AppendLine($"# Hiçbir yapıyla eşleşmeyen girdi anahtarları: {orphans.Count}");
        foreach (var k in orphans)
        {
            var e = ContentLibrary.All[k];
            // gruplar yalnız grup yolundan kalıtılır; grup yolunda böyle bir ad yoksa anahtar büyük olasılıkla yanlıştır
            bool inGroups = model.Groups.Any(path => path.Any(g => string.Equals(g[0], k, StringComparison.OrdinalIgnoreCase)));
            sb.AppendLine($"#   {k}{(e.Group ? inGroups ? "  (grup; yapılar kendi girdisine sahip)" : "  (grup — grup yolunda bulunamadı!)" : "")}");
        }
        sb.AppendLine();
        sb.AppendLine("kategori\tİngilizce ad\tTürkçe ad\tdurum\tgirdi\tgrup yolu > üst yapı\tkimlik");
        foreach (var r in rows.OrderBy(r => r.cat).ThenBy(r => r.status).ThenBy(r => r.en))
            sb.AppendLine($"{Categories.Get(r.cat).Key}\t{r.en}\t{r.tr ?? "-"}\t{r.status}\t{r.key}\t{r.path}\t{r.id}");
        return sb.ToString();
    }

    private static string Pct(int a, int b) => b == 0 ? "0%" : $"{100.0 * a / b:0.0}%";
}
