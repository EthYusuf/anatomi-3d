using System.Numerics;
using Anatomi3D.Core.Model;

namespace Anatomi3D.Desktop.UI;

/// <summary>Hazır bölge / sistem görünümleri: ilgili yapıları izole eder ve kamerayı çerçeveler.</summary>
public sealed record RegionPreset(string Tr, string En, string Icon, string GroupTr, string GroupEn,
    Category[] Categories, string[] Groups, string[] ContextGroups, float Yaw = 0, float Pitch = 0.05f);

public static class Regions
{
    private static readonly Category[] Skeletal = [Category.Bone, Category.Cartilage, Category.Teeth, Category.Ligament];
    private static readonly Category[] Muscular = [Category.Muscle];
    private static readonly Category[] All = Enum.GetValues<Category>().Where(c => c != Category.Attachment && c != Category.Skin && c != Category.Hair).ToArray();

    public static readonly RegionPreset[] Presets =
    [
        // iskelet
        new("Kafatası", "Skull", Icons.Person, "İskelet", "Skeleton", Skeletal,
            ["Cranium", "Extracranial bones of head", "Auditory ossicles", "Teeth", "Nasal cartilages", "Joints of skull"], [], 0.55f, 0.1f),
        new("Omurga", "Vertebral column", Icons.List, "İskelet", "Skeleton", Skeletal,
            ["Vertebral column", "Joints of vertebral column", "Sacrum", "Coccyx"], [], 0.9f, 0.05f),
        new("Göğüs kafesi", "Thoracic cage", Icons.Library, "İskelet", "Skeleton", Skeletal,
            ["Thoracic skeleton", "Thoracic joints", "Thoracic vertebrae"], [], 0.35f, 0.08f),
        new("Üst ekstremite kemikleri", "Bones of upper limb", Icons.Move, "İskelet", "Skeleton", Skeletal,
            ["Bones of upper limb", "Joints of upper limb"], []),
        new("Alt ekstremite kemikleri", "Bones of lower limb", Icons.Move, "İskelet", "Skeleton", Skeletal,
            ["Bones of lower limb", "Joints of lower limb"], []),
        // kaslar
        new("Baş ve yüz kasları", "Muscles of head", Icons.Person, "Kaslar", "Muscles", Muscular,
            ["Muscles of head"], ["Cranium", "Extracranial bones of head", "Teeth"], 0.45f, 0.05f),
        new("Boyun kasları", "Muscles of neck", Icons.Person, "Kaslar", "Muscles", Muscular,
            ["Muscles of neck"], ["Cervical vertebrae", "Extracranial bones of head", "Laryngeal cartilages"], 0.5f, 0.05f),
        new("Sırt kasları", "Muscles of back", Icons.Person, "Kaslar", "Muscles", Muscular,
            ["Epaxial muscles", "Hypaxial muscles of back"], ["Vertebral column", "Thoracic skeleton", "Bones of pectoral girdle", "Bones of pelvic girdle"], MathF.PI, 0.05f),
        new("Göğüs ve karın kasları", "Muscles of thorax & abdomen", Icons.Person, "Kaslar", "Muscles", Muscular,
            ["Muscles of thorax", "Muscles of abdomen", "Thoracic part of muscular system"], ["Thoracic skeleton", "Bones of pelvic girdle", "Vertebral column"]),
        new("Üst ekstremite kasları", "Muscles of upper limb", Icons.Move, "Kaslar", "Muscles", Muscular,
            ["Muscles of upper limb"], ["Bones of upper limb"], 0.4f, 0.05f),
        new("Alt ekstremite kasları", "Muscles of lower limb", Icons.Move, "Kaslar", "Muscles", Muscular,
            ["Muscles of lower limb", "Muscles of pelvis"], ["Bones of lower limb"], 0.4f, 0.05f),
        // sinir sistemi
        new("Beyin", "Brain", Icons.Lightning, "Sinir sistemi", "Nervous system", [Category.Cns],
            ["Brain"], [], 0.8f, 0.25f),
        new("Kraniyal sinirler", "Cranial nerves", Icons.Lightning, "Sinir sistemi", "Nervous system", [Category.Nerve, Category.Cns],
            ["Cranial nerves", "Brain"], [], 0.6f, -0.2f),
        new("Omurilik ve spinal sinirler", "Spinal cord & nerves", Icons.Lightning, "Sinir sistemi", "Nervous system", [Category.Cns, Category.Nerve],
            ["Spinal cord", "Spinal nerves", "Roots of nerves", "Ganglia"], [], MathF.PI, 0.05f),
        new("Göz", "Eye", Icons.Eye, "Duyu organları", "Sense organs", [Category.Sense, Category.Muscle, Category.Nerve],
            ["Sense organs / Eye", "Orbit"], [], 0.5f, 0.1f),
        new("Kulak", "Ear", Icons.Globe, "Duyu organları", "Sense organs", [Category.Sense, Category.Bone],
            ["Sense organs / Ear", "Auditory ossicles"], [], MathF.PI / 2, 0.1f),
        // dolaşım
        new("Kalp", "Heart", Icons.Heart, "Dolaşım", "Circulation", [Category.Heart, Category.Artery, Category.Vein],
            ["Heart", "Arteries of heart", "Cardiac veins"], [], 0.25f, 0.1f),
        new("Arter sistemi", "Arterial system", Icons.Heart, "Dolaşım", "Circulation", [Category.Artery, Category.Heart],
            ["Arterial system"], []),
        new("Ven sistemi", "Venous system", Icons.Heart, "Dolaşım", "Circulation", [Category.Vein, Category.Heart],
            ["Venous system"], []),
        // iç organlar
        new("Solunum sistemi", "Respiratory system", Icons.Library, "İç organlar", "Viscera", [Category.Respiratory],
            ["Respiratory system"], []),
        new("Sindirim sistemi", "Digestive system", Icons.Library, "İç organlar", "Viscera", [Category.Digestive],
            ["Digestive system"], []),
        new("Üriner ve genital sistem", "Urogenital system", Icons.Library, "İç organlar", "Viscera", [Category.Urinary, Category.Genital],
            ["Urinary system", "Genital systems"], []),
        new("Endokrin bezler", "Endocrine glands", Icons.Library, "İç organlar", "Viscera", [Category.Endocrine],
            ["Endocrine glands"], [], 0.3f, 0.05f),
    ];

    /// <summary>Ön ayarın gösterdiği yapılar: ana yapılar + bağlam (iskelet) yapıları.</summary>
    public static (List<int> main, List<int> context) Resolve(AnatomyModel model, RegionPreset r)
    {
        var main = new List<int>();
        var ctx = new List<int>();
        foreach (var p in model.Parts)
        {
            if (p.IsAttachment) continue;
            var path = string.Join(" / ", model.Groups[p.Group].Select(g => g[0]));
            bool inMain = r.Categories.Contains(p.Category) && r.Groups.Any(g => path.Contains(g, StringComparison.OrdinalIgnoreCase) || p.En.Contains(g, StringComparison.OrdinalIgnoreCase));
            if (inMain) { main.Add(p.Index); continue; }
            if (r.ContextGroups.Length > 0 && (p.Category is Category.Bone or Category.Cartilage or Category.Teeth) &&
                r.ContextGroups.Any(g => path.Contains(g, StringComparison.OrdinalIgnoreCase)))
                ctx.Add(p.Index);
        }
        return (main, ctx);
    }

    public static (Vector3 center, float radius) Bounds(AnatomyModel model, IEnumerable<int> parts)
    {
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        foreach (var i in parts)
        {
            mn = Vector3.Min(mn, model.Parts[i].Min);
            mx = Vector3.Max(mx, model.Parts[i].Max);
        }
        return mn.X > mx.X ? (Vector3.Zero, 1) : ((mn + mx) * 0.5f, (mx - mn).Length() * 0.5f);
    }
}
