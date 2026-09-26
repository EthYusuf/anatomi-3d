using System.Numerics;
using System.Text.RegularExpressions;
using SharpGLTF.Schema2;

namespace Anatomi3D.AssetBuilder;

/// <summary>Kaynak GLB'den çıkarılmış, dünya koordinatlarında tek parça yapı.</summary>
public sealed class RawPart
{
    public required string File { get; init; }
    public required string Name { get; init; }
    public required string[] Chain { get; init; }
    public required string Material { get; init; }
    public required Vector3[] Positions { get; init; }
    public required int[] Indices { get; init; }
    public string Category { get; set; } = "";
    public string Path => string.Join(" / ", Chain);
}

/// <summary>Kas yapışma işareti (Z-Anatomy'de kemik yüzeyindeki origo/insersiyo alanı).</summary>
public sealed record AttachmentInfo(string Muscle, char Kind, int Number, string Side);

public static partial class Source
{
    public static readonly string[] Files =
        ["SkeletalSystem100", "MuscularSystem100", "NervousSystem100", "CardioVascular41", "VisceralSystem100", "Joints100", "Regions"];

    [GeneratedRegex(@"^(?<m>.*)\.(?<k>[oe])(?<n>\d*)(?<s>[lr]?)$")]
    private static partial Regex AttachmentRx();

    [GeneratedRegex(@"\.(j|i|g)$")]
    private static partial Regex LabelRx();

    [GeneratedRegex("[a-z]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LetterRx();

    public static AttachmentInfo? ParseAttachment(string name)
    {
        var m = AttachmentRx().Match(name);
        if (!m.Success) return null;
        var muscle = m.Groups["m"].Value.Trim();
        if (muscle.Length == 0) return null;
        return new AttachmentInfo(muscle, m.Groups["k"].Value[0], m.Groups["n"].Value.Length > 0 ? int.Parse(m.Groups["n"].Value) : 0,
            m.Groups["s"].Value);
    }

    /// <summary>Etiket, grup ve kullanılmayan düğümler.</summary>
    public static bool Skip(string name)
    {
        if (!LetterRx().IsMatch(name)) return true; // bozuk kodlanmış adlar
        if (LabelRx().IsMatch(name)) return true;
        if (name.Contains("segment of liver")) return true; // karaciğer segmentleri bütün karaciğerle çakışır
        if (name.Contains("Papillary process")) return true;
        return false;
    }

    public static string? Classify(string file, string path, string name, string mat)
    {
        string p = path;
        switch (file)
        {
            case "SkeletalSystem100":
                if (p.Contains("Teeth.g")) return "teeth";
                if (p.Contains("cartilage", StringComparison.OrdinalIgnoreCase) || name.Contains("cartilage", StringComparison.OrdinalIgnoreCase)) return "cartilage";
                return "bone";
            case "MuscularSystem100":
                if (p.Contains("Fasciae.g") || p.Contains("Bursae") || p.Contains("Tendon sheaths") || mat == "Fascia") return "fascia";
                return "muscle";
            case "NervousSystem100":
                if (p.Contains("Sense organs.g")) return "sense";
                if (p.Contains("Meninges.g")) return "meninges";
                if (p.Contains("Central nervous system.g")) return "cns";
                return "nerve";
            case "CardioVascular41":
                if (p.Contains("Heart.g")) return "heart";
                if (p.Contains("Arterial system.g")) return "artery";
                return "vein";
            case "VisceralSystem100":
                if (Regex.IsMatch(p + "/" + name, "Pleura|Peritoneal|Thoracic cavity|Abdominopelvic|omentum|Mesocolon|Meso-appendix", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "serosa";
                if (p.Contains("Respiratory system.g")) return "respiratory";
                if (p.Contains("Urinary system.g")) return "urinary";
                if (p.Contains("Genital systems.g")) return "genital";
                if (p.Contains("Endocrine glands.g")) return "endocrine";
                return "digestive";
            case "Joints100":
                return "ligament";
            case "Regions":
                if (p.Contains("Hairs.g")) return "hair";
                return "skin";
        }
        return null;
    }

    /// <summary>Bir GLB dosyasındaki tüm mesh düğümlerini dünya koordinatlarında çıkarır.</summary>
    public static IEnumerable<RawPart> Read(string glbPath, string fileKey)
    {
        var model = ModelRoot.Load(glbPath, new ReadSettings { Validation = SharpGLTF.Validation.ValidationMode.Skip });
        foreach (var node in model.LogicalNodes)
        {
            var mesh = node.Mesh;
            if (mesh == null) continue;
            string name = node.Name ?? "";
            var chain = new List<string>();
            for (var p = node.VisualParent; p != null; p = p.VisualParent) chain.Insert(0, p.Name ?? "");
            var world = node.WorldMatrix;
            // aynalanmış dönüşüm (negatif determinant) üçgen yönünü tersine çevirir: dışa bakan yönü korumak için çevir
            bool mirrored = world.GetDeterminant() < 0;

            var pos = new List<Vector3>();
            var idx = new List<int>();
            var matTris = new Dictionary<string, int>();
            foreach (var prim in mesh.Primitives)
            {
                if (prim.DrawPrimitiveType != PrimitiveType.TRIANGLES) continue;
                var pa = prim.GetVertexAccessor("POSITION");
                if (pa == null) continue;
                var local = pa.AsVector3Array();
                int baseV = pos.Count;
                foreach (var v in local) pos.Add(Vector3.Transform(v, world));
                var ind = prim.GetIndices();
                int cnt;
                if (ind != null)
                {
                    for (int t = 0; t + 2 < ind.Count; t += 3)
                    {
                        idx.Add(baseV + (int)ind[t]);
                        idx.Add(baseV + (int)ind[mirrored ? t + 2 : t + 1]);
                        idx.Add(baseV + (int)ind[mirrored ? t + 1 : t + 2]);
                    }
                    cnt = ind.Count;
                }
                else
                {
                    for (int i = 0; i + 2 < local.Count; i += 3)
                    {
                        idx.Add(baseV + i);
                        idx.Add(baseV + (mirrored ? i + 2 : i + 1));
                        idx.Add(baseV + (mirrored ? i + 1 : i + 2));
                    }
                    cnt = local.Count;
                }
                string mn = prim.Material?.Name ?? "";
                matTris[mn] = matTris.GetValueOrDefault(mn) + cnt / 3;
            }
            if (idx.Count == 0) continue;
            string mat = matTris.OrderByDescending(kv => kv.Value).First().Key;
            mat = Regex.Replace(mat, @"\.\d+$", "");
            yield return new RawPart { File = fileKey, Name = name, Chain = chain.ToArray(), Material = mat, Positions = pos.ToArray(), Indices = idx.ToArray() };
        }
    }
}
