using System.Numerics;
using System.Text.Json;
using Anatomi3D.Core.Content;
using Anatomi3D.Core.Pack;

namespace Anatomi3D.Core.Model;

public enum Side : byte { None, Left, Right }

public enum AttachmentKind : byte { Origin, Insertion }

/// <summary>Bir anatomik yapı (paketteki bir parça).</summary>
public sealed class Part
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required Category Category { get; init; }
    /// <summary>İngilizce ad (taraf eki olmadan)</summary>
    public required string En { get; init; }
    /// <summary>Latince ad (Terminologia Anatomica)</summary>
    public string? La { get; init; }
    /// <summary>Türkçe ad (sözlükte varsa)</summary>
    public string? Tr { get; set; }
    public Side Side { get; init; }
    public string? Material { get; init; }
    public int Group { get; init; }
    public Part? Parent { get; set; }
    public List<Part> Children { get; } = [];
    public string? DescriptionKey { get; init; }
    public Vector3 Center { get; init; }
    public float Radius { get; init; }
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }
    public Vector3 Axis { get; init; }
    public Vector3 Minor { get; init; }
    public PartFlags Flags { get; init; }
    public int Triangles { get; init; }

    /// <summary>Kas yapışma alanı parçası ise: hangi kas, origo mu insersiyo mu</summary>
    public string? AttachmentMuscle { get; init; }
    public AttachmentKind AttachmentKind { get; init; }
    /// <summary>Kaslar için: kemik üzerindeki origo / insersiyo alanı parçaları</summary>
    public List<Part> Attachments { get; } = [];

    /// <summary>Arama için normalize metin</summary>
    public string SearchText { get; set; } = "";

    public bool IsAttachment => Category == Category.Attachment;
    public CategoryInfo Info => Categories.Get(Category);

    public string DisplayName(Lang lang)
    {
        string side = Side switch
        {
            Side.Left => lang == Lang.Tr ? " (sol)" : " (left)",
            Side.Right => lang == Lang.Tr ? " (sağ)" : " (right)",
            _ => "",
        };
        return (lang == Lang.Tr ? Tr ?? La ?? En : En) + side;
    }

    /// <summary>İkincil ad: TR'de Latince (yoksa İngilizce), EN'de Latince.</summary>
    public string? SecondaryName(Lang lang)
    {
        if (lang == Lang.Tr) return Tr != null ? La ?? En : La != null ? En : null;
        return La;
    }

    public override string ToString() => $"{Id} ({Category})";
}

/// <summary>Model paketinin üst verisiyle kurulan yapı ağacı.</summary>
public sealed class AnatomyModel
{
    public Part[] Parts { get; }
    public IReadOnlyDictionary<string, Part> ById { get; }
    /// <summary>Grup yolları: her öğe [İngilizce, Latince?]</summary>
    public string[][][] Groups { get; }
    public string Source { get; }
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public Dictionary<string, string> Descriptions { get; }
    /// <summary>Türkçe ad sözlüğü (yapı ve grup adları)</summary>
    public TurkishNames Names { get; private set; } = new();

    /// <summary>Grup adının arayüz dilindeki karşılığı.</summary>
    public string GroupLabel(string[] g, Lang lang) =>
        lang == Lang.Tr ? Names.Lookup(g[0]) ?? (g.Length > 1 ? g[1] : g[0]) : g[0];

    private AnatomyModel(Part[] parts, string[][][] groups, string source, Dictionary<string, string> descriptions)
    {
        Parts = parts;
        ById = parts.ToDictionary(p => p.Id);
        Groups = groups;
        Source = source;
        Descriptions = descriptions;
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        foreach (var p in parts)
        {
            if (p.Category is not (Category.Bone or Category.Skin)) continue;
            mn = Vector3.Min(mn, p.Min);
            mx = Vector3.Max(mx, p.Max);
        }
        BoundsMin = mn;
        BoundsMax = mx;
    }

    public Vector3 BoundsCenter => (BoundsMin + BoundsMax) * 0.5f;

    private sealed class MetaFile
    {
        public string source { get; set; } = "";
        public string[][][] groups { get; set; } = [];
        public MetaPart[] parts { get; set; } = [];
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
        public MetaAtt? att { get; set; }
    }

    private sealed class MetaAtt
    {
        public string m { get; set; } = "";
        public string k { get; set; } = "";
        public int n { get; set; }
    }

    public static AnatomyModel FromPack(PackData pack, TurkishNames names)
    {
        var meta = JsonSerializer.Deserialize<MetaFile>(pack.MetaJson) ?? throw new InvalidDataException("META okunamadı");
        var desc = JsonSerializer.Deserialize<Dictionary<string, string>>(pack.DescriptionsJson) ?? [];
        if (meta.parts.Length != pack.Parts.Length) throw new InvalidDataException("META ve PART sayıları uyuşmuyor");

        var parts = new Part[meta.parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var m = meta.parts[i];
            var r = pack.Parts[i];
            parts[i] = new Part
            {
                Index = i, Id = m.id, Category = Categories.FromKey(m.cat).Id, En = m.en, La = m.la,
                Side = m.side switch { "l" => Side.Left, "r" => Side.Right, _ => Side.None },
                Material = m.mat, Group = m.g, DescriptionKey = m.desc,
                Center = r.Center, Radius = r.Radius, Min = r.Min, Max = r.Max, Axis = r.Axis, Minor = r.Minor,
                Flags = (PartFlags)r.Flags, Triangles = (int)(r.Lod0.IndexCount / 3),
                AttachmentMuscle = m.att?.m,
                AttachmentKind = m.att?.k == "e" ? AttachmentKind.Insertion : AttachmentKind.Origin,
                Tr = m.att == null ? names.Lookup(m.en) : null,
            };
        }
        var byId = parts.ToDictionary(p => p.Id);
        for (int i = 0; i < parts.Length; i++)
        {
            var pid = meta.parts[i].parent;
            if (pid != null && byId.TryGetValue(pid, out var parent))
            {
                parts[i].Parent = parent;
                parent.Children.Add(parts[i]);
            }
        }
        // kas yapışma alanlarını kaslara bağla (aynı ad ve taraf)
        var muscles = parts.Where(p => p.Category == Category.Muscle)
            .GroupBy(p => (p.En.ToLowerInvariant(), p.Side)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var a in parts.Where(p => p.IsAttachment))
            if (muscles.TryGetValue((a.AttachmentMuscle!.ToLowerInvariant(), a.Side), out var list))
                foreach (var mm in list) mm.Attachments.Add(a);

        var model = new AnatomyModel(parts, meta.groups, meta.source, desc) { Names = names };
        foreach (var p in parts) p.SearchText = Search.SearchIndex.BuildText(p);
        return model;
    }

    /// <summary>Yapı + aynı kategorideki alt yapıları (kazı ile birlikte kalkanlar).</summary>
    public List<Part> WithChildren(Part p)
    {
        var list = new List<Part> { p };
        foreach (var c in p.Children)
            if (c.Category == p.Category) list.AddRange(WithChildren(c));
        return list;
    }

    /// <summary>Karşı taraftaki aynı yapı.</summary>
    public Part? Counterpart(Part p)
    {
        if (p.Side == Side.None) return null;
        foreach (var q in Parts)
            if (q.En == p.En && q.Category == p.Category && q.Side != Side.None && q.Side != p.Side) return q;
        return null;
    }
}
