using System.Globalization;
using System.Text;
using Anatomi3D.Core.Model;

namespace Anatomi3D.Core.Search;

/// <summary>
/// Türkçe duyarlı arama. Metin küçük harfe (tr-TR kuralları: I→ı, İ→i) çevrilir, aksanlar atılır (ç→c, ğ→g, ı→i,
/// ö→o, ş→s, ü→u) — böylece "kalca", "kalça", "KALÇA" aynı sonucu verir. Türkçe genel terimler İngilizce ve Latince
/// karşılıklarıyla da eşleşir ("kalp" → heart / cor, "atardamar" → artery).
/// </summary>
public static class SearchIndex
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var lower = s.ToLower(Tr).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch switch { 'ı' => 'i', '’' or '\'' => ' ', _ => ch });
        }
        return sb.ToString();
    }

    public static string BuildText(Part p)
    {
        var info = p.Info;
        return Normalize($"{p.Tr} | {p.La} | {p.En} | {info.Tr} {info.En}");
    }

    /// <summary>Türkçe genel terim → İngilizce/Latince karşılıklar.</summary>
    public static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["kemik"] = ["bone", "os "], ["kemigi"] = ["bone", "os "], ["kas"] = ["muscle", "musculus"], ["kasi"] = ["muscle", "musculus"],
        ["sinir"] = ["nerve", "nervus"], ["siniri"] = ["nerve", "nervus"], ["atardamar"] = ["artery", "arteria"], ["arter"] = ["artery", "arteria"],
        ["toplardamar"] = ["vein", "vena"], ["ven"] = ["vein", "vena"], ["bag"] = ["ligament"], ["kikirdak"] = ["cartilage"],
        ["eklem"] = ["joint", "articul"], ["kalp"] = ["heart", "cardi", "cor "], ["akciger"] = ["lung", "pulmo"], ["bobrek"] = ["kidney", "ren"],
        ["beyin"] = ["brain", "cerebr", "encephal"], ["beyincik"] = ["cerebell"], ["omurga"] = ["vertebra"], ["omur"] = ["vertebra"],
        ["kaburga"] = ["rib", "costa"], ["kafatasi"] = ["cranium", "skull", "cranial"], ["goz"] = ["eye", "ocul", "orbit"],
        ["kulak"] = ["ear", "auri"], ["burun"] = ["nasal", "nose", "nasi"], ["el"] = ["hand", "manus"], ["ayak"] = ["foot", "pedis"],
        ["parmak"] = ["finger", "digit", "toe"], ["bagirsak"] = ["intestin", "colon", "jejun", "ileum"], ["bez"] = ["gland"],
        ["dis"] = ["tooth", "teeth", "molar", "incisor", "canine", "premolar"], ["bolge"] = ["region"], ["deri"] = ["skin", "region"],
        ["sag"] = ["right", "dexter"], ["sol"] = ["left", "sinister"], ["karaciger"] = ["liver", "hepat"], ["mide"] = ["stomach", "gastr"],
        ["dalak"] = ["spleen", "splen"], ["pankreas"] = ["pancrea"], ["mesane"] = ["bladder", "vesica"], ["yuz"] = ["facial", "face"],
        ["boyun"] = ["neck", "cervical", "colli"], ["gogus"] = ["thorac", "pector", "chest"], ["karin"] = ["abdomin"], ["sirt"] = ["back", "dorsi"],
        ["bel"] = ["lumbar"], ["kalca"] = ["hip", "glute", "coxa"], ["diz"] = ["knee", "genu"], ["dirsek"] = ["elbow", "cubit"],
        ["omuz"] = ["shoulder", "deltoid", "humer"], ["bilek"] = ["wrist", "carp", "ankle", "tars"], ["uyluk"] = ["thigh", "femor"],
        ["bacak"] = ["leg", "crur"], ["kol"] = ["arm", "brach"], ["onkol"] = ["forearm", "antebrach"], ["cene"] = ["mandib", "maxill", "jaw"],
        ["dil"] = ["tongue", "lingu"], ["yutak"] = ["pharyn"], ["girtlak"] = ["laryn"], ["soluk borusu"] = ["trachea"],
        ["omurilik"] = ["spinal cord", "medulla spinalis"], ["tendon"] = ["tendon"], ["zar"] = ["membrane"], ["kapak"] = ["valve", "leaflet", "cusp"],
    };

    public sealed record Hit(Part Part, float Score);

    public static List<Hit> Query(IEnumerable<Part> parts, string query, int max = 80)
    {
        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return [];
        var alts = terms.Select(t => Synonyms.TryGetValue(t, out var s) ? s.Prepend(t).ToArray() : [t]).ToArray();
        var hits = new List<Hit>();
        foreach (var p in parts)
        {
            if (p.IsAttachment) continue;
            var text = p.SearchText;
            bool all = true;
            foreach (var a in alts)
            {
                bool any = false;
                foreach (var w in a) if (text.Contains(w, StringComparison.Ordinal)) { any = true; break; }
                if (!any) { all = false; break; }
            }
            if (!all) continue;
            // puan: ada tam/ön ek eşleşme önce, kısa adlar önce, büyük yapılar önce
            float score = 0;
            string tr = Normalize(p.Tr ?? ""), la = Normalize(p.La ?? ""), en = Normalize(p.En);
            string q = string.Join(' ', terms);
            if (tr == q || la == q || en == q) score -= 100;
            else if (tr.StartsWith(q) || la.StartsWith(q) || en.StartsWith(q)) score -= 40;
            else if (tr.StartsWith(terms[0]) || la.StartsWith(terms[0]) || en.StartsWith(terms[0])) score -= 20;
            score += text.Length * 0.05f;
            score -= MathF.Min(10f, p.Radius * 40f);
            if (p.Side == Side.Right) score += 0.01f;
            hits.Add(new Hit(p, score));
        }
        hits.Sort((a, b) => a.Score.CompareTo(b.Score));
        if (hits.Count > max) hits.RemoveRange(max, hits.Count - max);
        return hits;
    }
}
