using System.Numerics;

namespace Anatomi3D.Core.Model;

public enum BodySystem
{
    Integument,
    Muscular,
    Skeletal,
    Cardiovascular,
    Nervous,
    Visceral,
}

public enum Category
{
    Hair, Skin, Fascia, Muscle, Vein, Artery, Nerve, Heart, Respiratory, Digestive, Urinary, Genital, Endocrine, Serosa,
    Cns, Meninges, Sense, Ligament, Cartilage, Teeth, Bone, Attachment,
}

/// <summary>Kategori bilgisi: sistem, adlar, renk, katman soyma adımı.</summary>
public sealed record CategoryInfo(
    Category Id, BodySystem System, string Tr, string En, Vector3 Color, float Roughness,
    int? PeelStep, bool HiddenByDefault, string Key);

public sealed record SystemInfo(BodySystem Id, string Tr, string En, string Icon);

public static class Categories
{
    public static Vector3 Hex(string hex)
    {
        hex = hex.TrimStart('#');
        float c(int i) => Convert.ToInt32(hex.Substring(i, 2), 16) / 255f;
        return new Vector3(c(0), c(2), c(4));
    }

    public static readonly SystemInfo[] Systems =
    [
        new(BodySystem.Integument, "Deri ve Eklentileri", "Integument", ""),
        new(BodySystem.Muscular, "Kas Sistemi", "Muscular System", ""),
        new(BodySystem.Skeletal, "İskelet ve Eklemler", "Skeleton & Joints", ""),
        new(BodySystem.Cardiovascular, "Dolaşım Sistemi", "Cardiovascular System", ""),
        new(BodySystem.Nervous, "Sinir Sistemi", "Nervous System", ""),
        new(BodySystem.Visceral, "İç Organlar", "Viscera", ""),
    ];

    public static readonly CategoryInfo[] All =
    [
        new(Category.Hair, BodySystem.Integument, "Kıllar", "Hair", Hex("3a2c24"), 0.8f, 1, true, "hair"),
        new(Category.Skin, BodySystem.Integument, "Deri", "Skin", Hex("d8a88e"), 0.55f, 1, false, "skin"),
        new(Category.Fascia, BodySystem.Muscular, "Fasya, Bursa ve Kılıflar", "Fasciae, Bursae & Sheaths", Hex("d9d2bd"), 0.5f, 2, true, "fascia"),
        new(Category.Muscle, BodySystem.Muscular, "Kaslar", "Muscles", Hex("b8433c"), 0.55f, 3, false, "muscle"),
        new(Category.Vein, BodySystem.Cardiovascular, "Venler", "Veins", Hex("3c5fc4"), 0.4f, 4, false, "vein"),
        new(Category.Artery, BodySystem.Cardiovascular, "Arterler", "Arteries", Hex("d8322f"), 0.35f, 5, false, "artery"),
        new(Category.Nerve, BodySystem.Nervous, "Periferik Sinirler", "Peripheral Nerves", Hex("f0cf3c"), 0.45f, 6, false, "nerve"),
        new(Category.Heart, BodySystem.Cardiovascular, "Kalp", "Heart", Hex("b83a3a"), 0.45f, 7, false, "heart"),
        new(Category.Respiratory, BodySystem.Visceral, "Solunum Sistemi", "Respiratory System", Hex("e79a9e"), 0.6f, 7, false, "respiratory"),
        new(Category.Digestive, BodySystem.Visceral, "Sindirim Sistemi", "Digestive System", Hex("d98870"), 0.5f, 7, false, "digestive"),
        new(Category.Urinary, BodySystem.Visceral, "Üriner Sistem", "Urinary System", Hex("c9795b"), 0.5f, 7, false, "urinary"),
        new(Category.Genital, BodySystem.Visceral, "Genital Sistem", "Genital System", Hex("d98c9c"), 0.5f, 7, false, "genital"),
        new(Category.Endocrine, BodySystem.Visceral, "Endokrin Bezler", "Endocrine Glands", Hex("e0b25a"), 0.55f, 7, false, "endocrine"),
        new(Category.Serosa, BodySystem.Visceral, "Periton ve Plevra", "Peritoneum & Pleura", Hex("e8d6c4"), 0.4f, 7, true, "serosa"),
        new(Category.Cns, BodySystem.Nervous, "Beyin ve Omurilik", "Brain & Spinal Cord", Hex("e7b7bb"), 0.6f, null, false, "cns"),
        new(Category.Meninges, BodySystem.Nervous, "Meninksler", "Meninges", Hex("cfc6d6"), 0.45f, null, true, "meninges"),
        new(Category.Sense, BodySystem.Nervous, "Duyu Organları", "Sense Organs", Hex("eef0f2"), 0.25f, null, false, "sense"),
        new(Category.Ligament, BodySystem.Skeletal, "Eklemler ve Bağlar", "Joints & Ligaments", Hex("c9c3a6"), 0.55f, null, false, "ligament"),
        new(Category.Cartilage, BodySystem.Skeletal, "Kıkırdaklar", "Cartilages", Hex("a9cdd1"), 0.4f, null, false, "cartilage"),
        new(Category.Teeth, BodySystem.Skeletal, "Dişler", "Teeth", Hex("f6f2e6"), 0.25f, null, false, "teeth"),
        new(Category.Bone, BodySystem.Skeletal, "Kemikler", "Bones", Hex("e6dcc3"), 0.7f, null, false, "bone"),
        new(Category.Attachment, BodySystem.Skeletal, "Kas yapışma alanları", "Muscle attachments", Hex("d04040"), 0.5f, null, true, "attachment"),
    ];

    private static readonly Dictionary<string, CategoryInfo> ByKey = All.ToDictionary(c => c.Key);

    public static CategoryInfo Get(Category c) => All[(int)c];
    public static CategoryInfo FromKey(string key) => ByKey[key];
    public static SystemInfo System(BodySystem s) => Systems[(int)s];

    /// <summary>Katman soyma adımları (dıştan içe): her adımda soyulan kategoriler.</summary>
    public static readonly Category[][] PeelSteps = All
        .Where(c => c.PeelStep.HasValue)
        .GroupBy(c => c.PeelStep!.Value)
        .OrderBy(g => g.Key)
        .Select(g => g.Select(c => c.Id).ToArray())
        .ToArray();

    public static bool IsPeeled(Category c, int step)
    {
        for (int i = 0; i < step && i < PeelSteps.Length; i++)
            if (Array.IndexOf(PeelSteps[i], c) >= 0) return true;
        return false;
    }

    /// <summary>Z-Anatomy malzeme adlarına göre ince renk ayarı (beyin lobları, akciğer lobları vb.).</summary>
    public static readonly Dictionary<string, Vector3> MaterialTint = new()
    {
        ["Frontal lobe"] = Hex("e3a9a0"), ["Parietal lobe"] = Hex("d9b48c"), ["Temporal lobe"] = Hex("a9c2a0"),
        ["Occipital lobe"] = Hex("a6b5d6"), ["Cerebellum"] = Hex("d7a1c4"), ["White matter"] = Hex("f1ece2"),
        ["Brain-Inner"] = Hex("dcc0c0"), ["Nucleus"] = Hex("b89ad0"), ["Nucleus (afferent fibers)"] = Hex("8fb4e0"),
        ["Nucleus (efferent fibers)"] = Hex("e59a8c"), ["LCR"] = Hex("9fd3e6"), ["Interlobar sulci"] = Hex("c8a3a6"),
        ["Tendon"] = Hex("e9e2d0"), ["Teeth-roots"] = Hex("eadcb8"), ["Iris"] = Hex("5b7c9c"), ["Cornea"] = Hex("dfeff4"),
        ["Mucosa"] = Hex("e39a9a"), ["Gland"] = Hex("e0b25a"), ["Ductus"] = Hex("d9c46a"), ["Gallbladder"] = Hex("6f9b4c"),
        ["Intestine"] = Hex("dca08a"), ["Pulmonary artery"] = Hex("4a6fd8"), ["Pulmonary vein"] = Hex("d8424a"), ["Nail"] = Hex("f0d8cc"),
        ["Articular capsule"] = Hex("d7cfb2"), ["Bursa"] = Hex("b9d3e0"), ["Fat"] = Hex("f1d98a"), ["Bronchi"] = Hex("dfd2c0"),
        ["Lung-1"] = Hex("e8a0a6"), ["Lung-3"] = Hex("e3b0a0"), ["Lung-4"] = Hex("dca4b6"), ["Lung-6"] = Hex("e9b4a8"), ["Lung-8"] = Hex("d99ca8"),
    };

    /// <summary>Kas fonksiyonu renklendirmesi (Z-Anatomy kas malzeme adlarından).</summary>
    public static readonly Dictionary<string, (Vector3 Color, string Tr, string En)> MuscleFunction = new()
    {
        ["Flexion"] = (Hex("e5484d"), "Fleksiyon", "Flexion"),
        ["Flexion hand/foot"] = (Hex("f76b15"), "El/ayak fleksiyonu", "Hand/foot flexion"),
        ["Flexion fingers"] = (Hex("ffb224"), "Parmak fleksiyonu", "Finger flexion"),
        ["Extension"] = (Hex("3e63dd"), "Ekstansiyon", "Extension"),
        ["Extension hand/foot"] = (Hex("0090ff"), "El/ayak ekstansiyonu", "Hand/foot extension"),
        ["Extensor extremities"] = (Hex("00a2c7"), "Parmak ekstansiyonu", "Finger extension"),
        ["Abductor"] = (Hex("30a46c"), "Abdüksiyon", "Abduction"),
        ["Adductor"] = (Hex("8e4ec6"), "Addüksiyon", "Adduction"),
        ["External rotation"] = (Hex("12a594"), "Dış rotasyon", "External rotation"),
        ["Internal rotator"] = (Hex("d6409f"), "İç rotasyon", "Internal rotation"),
        ["Levator"] = (Hex("99d52a"), "Elevasyon", "Elevation"),
        ["Depressor"] = (Hex("ad7f58"), "Depresyon", "Depression"),
        ["Orbicularis/Constrictor"] = (Hex("e93d82"), "Sfinkter / konstriktör", "Sphincter / constrictor"),
        ["Masticator"] = (Hex("c2298a"), "Çiğneme", "Mastication"),
        ["Ingestion"] = (Hex("f5d90a"), "Yutma", "Swallowing"),
        ["Phonation"] = (Hex("7c66dc"), "Fonasyon", "Phonation"),
        ["Biarticular"] = (Hex("46a758"), "İki eklemli", "Biarticular"),
        ["Superficial"] = (Hex("e38b6b"), "Yüzeyel", "Superficial"),
        ["Diaphragm"] = (Hex("d4a5a5"), "Solunum", "Respiration"),
        ["Trapezius"] = (Hex("6e56cf"), "Omuz kuşağı", "Shoulder girdle"),
        ["Tendon"] = (Hex("e9e2d0"), "Tendon", "Tendon"),
    };
}
