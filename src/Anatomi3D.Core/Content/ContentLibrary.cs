using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Anatomi3D.Core.Model;

namespace Anatomi3D.Core.Content;

/// <summary>Türkçe anatomi bilgi girdisi (data/content/tr/*.json).</summary>
public sealed class ContentEntry
{
    /// <summary>Türkçe ad (varsa sözlüğü geçersiz kılar)</summary>
    [JsonPropertyName("tr")] public string? Tr { get; set; }
    /// <summary>Kısa tanım (1-3 cümle)</summary>
    [JsonPropertyName("ozet")] public string? Summary { get; set; }
    /// <summary>Anahtar bilgiler: [başlık, değer] — kaslarda origo, insersiyo, innervasyon, kanlanma, fonksiyon</summary>
    [JsonPropertyName("bilgi")] public List<string[]>? Facts { get; set; }
    /// <summary>Başlıklı bölümler</summary>
    [JsonPropertyName("bolumler")] public List<ContentSection>? Sections { get; set; }
    /// <summary>Klinik not</summary>
    [JsonPropertyName("klinik")] public string? Clinical { get; set; }
    /// <summary>İçerik gözden geçirme durumu: taslak | inceleniyor | onaylı</summary>
    [JsonPropertyName("durum")] public string? Status { get; set; }
    /// <summary>true: bir grubu anlatır; gruptaki kendi girdisi olmayan yapılara kalıtılır</summary>
    [JsonPropertyName("grup")] public bool Group { get; set; }
    [JsonIgnore] public string Key { get; set; } = "";
}

public sealed class ContentSection
{
    [JsonPropertyName("baslik")] public string Title { get; set; } = "";
    [JsonPropertyName("metin")] public string Text { get; set; } = "";
}

/// <summary>
/// Türkçe içerik kütüphanesi. Girdiler İngilizce yapı adıyla anahtarlanır (Z-Anatomy adları); bir yapının kendi girdisi
/// yoksa üst yapısının ya da taban kas adının girdisi kullanılır (ör. "Long head of biceps brachii" → "Biceps brachii").
/// İçerik ekibi JSON dosyalarını kod değiştirmeden düzenleyebilir.
/// </summary>
public static partial class ContentLibrary
{
    private static readonly Dictionary<string, ContentEntry> Entries = new(StringComparer.OrdinalIgnoreCase);
    public static int Count => Entries.Count;
    public static IReadOnlyDictionary<string, ContentEntry> All => Entries;

    private static readonly JsonSerializerOptions Opts = new() { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static void Load(string contentDir)
    {
        Entries.Clear();
        var dir = Path.Combine(contentDir, "tr");
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json").OrderBy(x => x))
        {
            string current = "";
            try
            {
                using var fs = File.OpenRead(f);
                using var doc = JsonDocument.Parse(fs, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    // "_" ile başlayan anahtarlar dosya açıklamasıdır
                    if (prop.Name.StartsWith('_')) continue;
                    current = prop.Name;
                    var v = prop.Value.Deserialize<ContentEntry>(Opts);
                    if (v == null) continue;
                    var key = prop.Name.Trim();
                    v.Key = key;
                    Entries[key] = v;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"İçerik dosyası hatalı: {Path.GetFileName(f)} [{current}] — {ex.Message}", ex);
            }
        }
    }

    [GeneratedRegex(@"^(long|short|lateral|medial|superficial|deep|clavicular|sternocostal|abdominal|oblique|transverse|posterior|anterior|inferior|superior|middle|ascending|descending|acromial|spinal|scapular spinal|humeral|ulnar|radial|humero-ulnar|straight|orbital|palpebral|external|internal|ary-epiglottic|thyro-epiglottic)\s+(head|part|belly|portion|fibres|fibers)\s+of\s+(the\s+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadRx();

    /// <summary>Sistematik adlar için ortak girdi anahtarları (ör. "Vertebra C4" → "Cervical vertebrae").</summary>
    private static readonly (Regex rx, string key)[] Generic =
    [
        (new Regex(@"^Vertebra C[3-7]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Cervical vertebrae"),
        (new Regex(@"^Vertebra T\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Thoracic vertebrae"),
        (new Regex(@"^Vertebra L\d$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Lumbar vertebrae"),
        (new Regex(@"^(first|second|third|fourth|fifth|sixth|seventh) rib$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "True ribs"),
        (new Regex(@"^(eighth|ninth|tenth) rib$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "False ribs"),
        (new Regex(@"^(eleventh|twelfth) rib$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Floating ribs"),
        (new Regex(@"^costal cartilage of", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Costal cartilages"),
        (new Regex(@"phalanx of .* of hand$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Phalanges of hand"),
        (new Regex(@"phalanx of .* of foot$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Phalanges of foot"),
        (new Regex(@"metacarpal bone$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Metacarpal bones"),
        (new Regex(@"metatarsal bone$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Metatarsal bones"),
        (new Regex(@"incisor", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Incisors"),
        (new Regex(@"canine", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Canines"),
        (new Regex(@"premolar", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Premolars"),
        (new Regex(@"molar tooth", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Molars"),
        (new Regex(@"^intervertebral dis", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Intervertebral disc"),
        (new Regex(@"^nucleus pulposus", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Nucleus pulposus"),
        (new Regex(@"^(superior|middle|inferior) lobe of (right|left) lung$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Lungs"),
        (new Regex(@"intercostal muscles$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Intercostal muscles"),
        (new Regex(@"intertransversarii", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Intertransversarii muscles"),
        (new Regex(@"papillary muscle of (right|left) ventricle$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Papillary muscles"),
        (new Regex(@"segmental bronchus", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Segmental bronchi"),
        (new Regex(@"lobar bronchus$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Lobar bronchi"),
    ];

    /// <summary>
    /// Yapı için en uygun içerik girdisi ve bu girdinin yapının kendisine mi (false) yoksa üst yapısına / grubuna mı (true) ait olduğu.
    /// Arama sırası: kendi adı → kas başı/bölümünden taban kas → sistematik ad kuralları → üst yapılar → grup yolu (içten dışa).
    /// </summary>
    public static (ContentEntry? entry, bool inherited) For(Part p, string[][]? groupPath = null)
    {
        if (Entries.TryGetValue(p.En, out var e)) return (e, false);
        // "Long head of biceps brachii" → "Biceps brachii"
        var m = HeadRx().Match(p.En);
        if (m.Success)
        {
            var baseName = p.En[m.Length..];
            if (Entries.TryGetValue(baseName, out e)) return (e, true);
            if (Entries.TryGetValue(baseName + " muscle", out e)) return (e, true);
            if (baseName.EndsWith(" muscle", StringComparison.Ordinal) && Entries.TryGetValue(baseName[..^7], out e)) return (e, true);
        }
        foreach (var (rx, key) in Generic)
            if (rx.IsMatch(p.En) && Entries.TryGetValue(key, out e)) return (e, true);
        for (var q = p.Parent; q != null; q = q.Parent)
            if (Entries.TryGetValue(q.En, out e)) return (e, true);
        if (groupPath != null)
            for (int i = groupPath.Length - 1; i >= 0; i--)
                if (Entries.TryGetValue(groupPath[i][0], out e) && e.Group) return (e, true);
        return (null, false);
    }

    public static string? TurkishName(string en) => Entries.TryGetValue(en, out var e) ? e.Tr : null;
}

/// <summary>Z-Anatomy / Wikipedia kaynaklı İngilizce açıklama metnini bölümlere ayırır.</summary>
public static partial class DescriptionParser
{
    public sealed record Section(int Level, string Title, List<string> Paragraphs);

    [GeneratedRegex("^(=+)\\s*(.*?)\\s*=+$")] private static partial Regex HeadingRx();
    [GeneratedRegex("animals|invertebrate|other species|in other|society|culture|etymology|see also|gallery|additional images|references|history", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkipRx();
    [GeneratedRegex(@"\(\s*[,;]?\s*\)")] private static partial Regex EmptyParenRx();
    [GeneratedRegex(@"\s+([,.;])")] private static partial Regex SpaceBeforePunctRx();

    public static (List<Section> sections, string? url) Parse(string text)
    {
        var sections = new List<Section> { new(1, "", []) };
        string? url = null;
        int skipLevel = 0;
        bool titleSeen = false;
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0) continue;
            if (l.StartsWith("http://", StringComparison.Ordinal) || l.StartsWith("https://", StringComparison.Ordinal)) { url = l; continue; }
            if (!titleSeen)
            {
                titleSeen = true;
                // ilk satır BÜYÜK HARFLİ başlıktır; aynı satırda cümle başlıyorsa ayır
                var words = l.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int k = 0;
                while (k < words.Length && words[k] == words[k].ToUpperInvariant() && !words[k].StartsWith('"') && !words[k].StartsWith('“')) k++;
                if (k > 0)
                {
                    var rest = string.Join(' ', words.Skip(k));
                    if (rest.Length > 0) sections[0].Paragraphs.Add(rest);
                    continue;
                }
            }
            var m = HeadingRx().Match(l);
            if (m.Success)
            {
                int level = m.Groups[1].Value.Length;
                if (skipLevel > 0 && level > skipLevel) continue;
                skipLevel = SkipRx().IsMatch(m.Groups[2].Value) ? level : 0;
                if (skipLevel == 0) sections.Add(new Section(level, m.Groups[2].Value, []));
                continue;
            }
            if (skipLevel > 0) continue;
            var clean = SpaceBeforePunctRx().Replace(EmptyParenRx().Replace(l, ""), "$1");
            var cur = sections[^1];
            if (cur.Paragraphs.Count > 0 && cur.Paragraphs[^1].Length < 380) cur.Paragraphs[^1] += " " + clean;
            else cur.Paragraphs.Add(clean);
        }
        return (sections.Where(s => s.Paragraphs.Count > 0).ToList(), url);
    }
}
