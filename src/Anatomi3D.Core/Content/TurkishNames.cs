using System.Text.Json;
using System.Text.RegularExpressions;

namespace Anatomi3D.Core.Content;

public enum Lang { Tr, En }

/// <summary>
/// Yapıların Türkçe adları. Sözlük "data/content/names.tr.json" dosyasından okunur (içerik ekibi kod
/// değiştirmeden düzenleyebilir); sistematik adlar (omurlar, kaburgalar, falankslar, dişler, taraf ve yön
/// ekleri) kurallarla üretilir. Karşılığı olmayan yapılarda Latince (Terminologia Anatomica) ad gösterilir.
/// </summary>
public sealed partial class TurkishNames
{
    private readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> Ord = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["sixth"] = 6, ["seventh"] = 7,
        ["eighth"] = 8, ["ninth"] = 9, ["tenth"] = 10, ["eleventh"] = 11, ["twelfth"] = 12,
    };

    public int Count => names.Count;

    /// <summary>Verilen dosyayı ve aynı klasördeki diğer names*.tr.json dosyalarını (alfabetik sırayla) yükler.</summary>
    public static TurkishNames Load(string? jsonPath)
    {
        var t = new TurkishNames();
        if (jsonPath == null) return t;
        var dir = Path.GetDirectoryName(jsonPath);
        var files = dir != null && Directory.Exists(dir)
            ? Directory.GetFiles(dir, "names*.tr.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
            : File.Exists(jsonPath) ? [jsonPath] : [];
        var opts = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        foreach (var f in files)
        {
            using var fs = File.OpenRead(f);
            var doc = JsonSerializer.Deserialize<Dictionary<string, string>>(fs, opts);
            if (doc == null) continue;
            foreach (var (k, v) in doc)
                if (!k.StartsWith('_')) t.names[k.Trim()] = v.Trim();
        }
        return t;
    }

    public void Add(string en, string tr) => names[en] = tr;

    [GeneratedRegex(@"^vertebra ([ctl])(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex VertebraRx();
    [GeneratedRegex(@"^(\w+) rib$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex RibRx();
    [GeneratedRegex(@"^costal cartilage of (\w+) rib$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex CostalRx();
    [GeneratedRegex(@"^(\w+) metacarpal bone$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex MetacarpalRx();
    [GeneratedRegex(@"^(\w+) metatarsal bone$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex MetatarsalRx();
    [GeneratedRegex(@"^(distal|middle|proximal) phalanx of (\w+) (finger|toe)(?: of (hand|foot))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex PhalanxRx();
    [GeneratedRegex(@"^(upper|lower) (medial|lateral|central) incisor(?: tooth)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex IncisorRx();
    [GeneratedRegex(@"^(upper|lower) canine(?: tooth)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex CanineRx();
    [GeneratedRegex(@"^(upper|lower) (first|second) premolar(?: tooth)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex PremolarRx();
    [GeneratedRegex(@"^(upper|lower) (first|second|third) molar(?: tooth)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex MolarRx();
    [GeneratedRegex(@"^(\w+) (thoracic|lumbar|sacral|cervical) (spinal )?nerve$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex SpinalNerveRx();
    [GeneratedRegex(@"^(\w+) (posterior )?intercostal (artery|vein|nerve)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex IntercostalRx();
    [GeneratedRegex(@"^intervertebral dis[ck] ([ctl]\d+)\s*-\s*([ctls]\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DiscRx();
    [GeneratedRegex(@"^nucleus pulposus ([ctl]\d+)\s*-\s*([ctls]\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex NucleusRx();

    public string? Lookup(string en)
    {
        if (names.TryGetValue(en, out var n)) return n;
        Match m;
        if ((m = VertebraRx().Match(en)).Success)
        {
            var k = m.Groups[1].Value.ToUpperInvariant();
            string kind = k switch { "C" => "boyun", "T" => "göğüs", _ => "bel" };
            return $"{k}{m.Groups[2].Value} omuru ({kind} omuru)";
        }
        if ((m = RibRx().Match(en)).Success && Ord.TryGetValue(m.Groups[1].Value, out int r)) return $"{r}. kaburga";
        if ((m = CostalRx().Match(en)).Success && Ord.TryGetValue(m.Groups[1].Value, out r)) return $"{r}. kaburga kıkırdağı";
        if ((m = MetacarpalRx().Match(en)).Success && Ord.TryGetValue(m.Groups[1].Value, out r)) return $"{r}. tarak kemiği (metakarp)";
        if ((m = MetatarsalRx().Match(en)).Success && Ord.TryGetValue(m.Groups[1].Value, out r)) return $"{r}. ayak tarak kemiği (metatars)";
        if ((m = PhalanxRx().Match(en)).Success && Ord.TryGetValue(m.Groups[2].Value, out r))
        {
            bool hand = m.Groups[3].Value.Equals("finger", StringComparison.OrdinalIgnoreCase) && !m.Groups[4].Value.Equals("foot", StringComparison.OrdinalIgnoreCase);
            string which = m.Groups[1].Value.ToLowerInvariant() switch { "distal" => "uç (distal)", "middle" => "orta", _ => "ilk (proksimal)" };
            return $"{(hand ? "El" : "Ayak")} {r}. parmağın {which} falanksı";
        }
        if ((m = IncisorRx().Match(en)).Success)
            return $"{UpLow(m.Groups[1].Value)} {(m.Groups[2].Value.Equals("lateral", StringComparison.OrdinalIgnoreCase) ? "yan" : "orta")} kesici diş";
        if ((m = CanineRx().Match(en)).Success) return $"{UpLow(m.Groups[1].Value)} köpek dişi";
        if ((m = PremolarRx().Match(en)).Success && Ord.TryGetValue(m.Groups[2].Value, out r)) return $"{UpLow(m.Groups[1].Value)} {r}. küçük azı dişi";
        if ((m = MolarRx().Match(en)).Success && Ord.TryGetValue(m.Groups[2].Value, out r))
            return $"{UpLow(m.Groups[1].Value)} {r}. büyük azı dişi{(r == 3 ? " (yirmilik)" : "")}";
        if ((m = SpinalNerveRx().Match(en)).Success && Ord.TryGetValue(m.Groups[1].Value, out r))
        {
            string reg = m.Groups[2].Value.ToLowerInvariant() switch { "thoracic" => "göğüs (torakal)", "lumbar" => "bel (lomber)", "sacral" => "sakral", _ => "boyun (servikal)" };
            return $"{r}. {reg} spinal sinir";
        }
        if ((m = IntercostalRx().Match(en)).Success && Ord.TryGetValue(m.Groups[1].Value, out r))
        {
            string kind = m.Groups[3].Value.ToLowerInvariant() switch { "artery" => "arteri", "vein" => "veni", _ => "siniri" };
            return $"{r}. {(m.Groups[2].Success ? "arka " : "")}interkostal {kind}";
        }
        if ((m = DiscRx().Match(en)).Success) return $"Omurlar arası disk {m.Groups[1].Value.ToUpperInvariant()}–{m.Groups[2].Value.ToUpperInvariant()}";
        if ((m = NucleusRx().Match(en)).Success) return $"Nukleus pulpozus {m.Groups[1].Value.ToUpperInvariant()}–{m.Groups[2].Value.ToUpperInvariant()}";
        return null;
    }

    private static string UpLow(string s) => s.Equals("upper", StringComparison.OrdinalIgnoreCase) ? "Üst" : "Alt";
}
