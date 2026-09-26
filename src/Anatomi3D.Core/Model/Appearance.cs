using System.Numerics;

namespace Anatomi3D.Core.Model;

/// <summary>Prosedürel yüzey türü (render motorunda aile + tür olarak eşlenir).</summary>
public enum SurfaceKind
{
    None, Muscle, Tendon, Nerve, Vessel, HeartMuscle, Bone, Skin, Wet, Brain, Iris, Sclera, Glass, Hair,
}

/// <summary>Bir yapının fiziksel tabanlı görünümü (renkler doğrusal uzayda).</summary>
public readonly record struct Appearance(
    Vector3 Albedo,
    float Roughness,
    float Clearcoat,
    Vector3 Sheen,
    float Subsurface,
    SurfaceKind Surface,
    float Bump,
    float Freq = 1f,
    bool Glass = false,
    /// <summary>Cam yapının opaklığı; 0 = normalde görünmez (ön kamara sıvısı, lens)</summary>
    float GlassOpacity = 0.1f);

/// <summary>
/// Yapı başına malzeme seçimi. Renk paleti klasik anatomi atlası kurallarını izler (arter kırmızı, ven mavi,
/// sinir sarı, kas koyu kırmızı-kahve, tendon sedef beyazı, kemik fildişi); ıslak dokular ek parlak katman,
/// lifli dokular kadife parıltı, yumuşak dokular deri altı saçılım alır.
/// </summary>
public static class Appearances
{
    public static Vector3 SrgbToLinear(Vector3 c)
    {
        static float f(float x) => x <= 0.04045f ? x / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
        return new Vector3(f(c.X), f(c.Y), f(c.Z));
    }

    private static Vector3 H(string hex) => Categories.Hex(hex);

    private static float Hash(string s)
    {
        uint h = 2166136261;
        foreach (var ch in s) h = (h ^ ch) * 16777619;
        return (h % 1000) / 1000f;
    }

    public static Appearance Of(Part p)
    {
        string en = p.En.ToLowerInvariant();
        // göz: kornea ince ve çok parlak cam; ön kamara ve lens normalde görünmez; vitreus koyu (göz bebeği siyah)
        if (en == "cornea") return new(Vector3.One, 0.02f, 1, Vector3.Zero, 0, SurfaceKind.Glass, 0, Glass: true, GlassOpacity: 0.05f);
        if (en == "lens") return new(Vector3.One, 0.05f, 0, Vector3.Zero, 0, SurfaceKind.Glass, 0, Glass: true, GlassOpacity: 0f);
        if (en is "anterior chamber of eyeball" or "anterior segment of eyeball")
            return new(Vector3.One, 0f, 0, Vector3.Zero, 0, SurfaceKind.Glass, 0, Glass: true, GlassOpacity: 0f);
        if (en == "zonular fibres") return new(H("e8e2d4"), 0.4f, 0, Vector3.Zero, 0, SurfaceKind.Glass, 0, Glass: true, GlassOpacity: 0f);
        if (en == "vitreous body") return new(H("0b0707"), 0.9f, 0, Vector3.Zero, 0, SurfaceKind.None, 0);
        if (en == "iris") return new(Vector3.One, 0.55f, 0.6f, Vector3.Zero, 0, SurfaceKind.Iris, 1.2f);
        if (en == "sclera") return new(H("f0eae0"), 0.38f, 0.35f, Vector3.Zero, 0.25f, SurfaceKind.Sclera, 0.2f);

        switch (p.Category)
        {
            case Category.Skin:
                if (p.Material == "Nail") return new(H("e9cfc2"), 0.25f, 0.7f, Vector3.Zero, 0.2f, SurfaceKind.None, 0);
                return new(H("c9a08f"), 0.56f, 0.06f, H("ff9a80") * 0.1f, 0.6f, SurfaceKind.Skin, 1f);
            case Category.Hair:
                return new(H("2a1d16"), 0.6f, 0, H("8a6a50") * 0.4f, 0, SurfaceKind.Hair, 0);
            case Category.Muscle:
                if (p.Material == "Tendon") return new(H("e6dccb"), 0.3f, 0.5f, H("ffffff") * 0.35f, 0.3f, SurfaceKind.Tendon, 1f);
                return new(H("8e3a31"), 0.48f, 0.3f, H("ffb4a6") * 0.3f, 0.5f, SurfaceKind.Muscle, 1.1f);
            case Category.Fascia:
                return new(H("e3dccb"), 0.35f, 0.6f, H("ffffff") * 0.25f, 0.3f, SurfaceKind.Tendon, 0.6f);
            case Category.Bone:
                return new(H("e8dcc0"), 0.7f, 0.03f, Vector3.Zero, 0.1f, SurfaceKind.Bone, 1f);
            case Category.Teeth:
                return new(p.Material == "Teeth-roots" ? H("e8d9b4") : H("f4f0e6"), 0.18f, 1f, Vector3.Zero, 0.2f, SurfaceKind.None, 0);
            case Category.Cartilage:
                return new(H("b7d3d6"), 0.28f, 0.7f, H("e8ffff") * 0.2f, 0.5f, SurfaceKind.Wet, 0.3f);
            case Category.Ligament:
                if (p.Material == "Cartilage") return new(H("b9d5d8"), 0.25f, 0.8f, Vector3.Zero, 0.5f, SurfaceKind.Wet, 0.25f);
                return new(H("d8cfb4"), 0.35f, 0.45f, H("ffffff") * 0.3f, 0.3f, SurfaceKind.Tendon, 0.8f);
            case Category.Artery:
                return new(p.Material == "Pulmonary artery" ? H("3d5fc4") : H("b8231f"), 0.3f, 0.7f, Vector3.Zero, 0.4f, SurfaceKind.Vessel, 0.4f);
            case Category.Vein:
                return new(p.Material == "Pulmonary vein" ? H("c2393c") : H("34468f"), 0.32f, 0.7f, Vector3.Zero, 0.35f, SurfaceKind.Vessel, 0.4f);
            case Category.Heart:
                if (en.Contains("leaflet") || en.Contains("valve") || en.Contains("cusp"))
                    return new(H("e8d2b8"), 0.3f, 0.6f, Vector3.Zero, 0.4f, SurfaceKind.Wet, 0.3f);
                return new(H("8a2e28"), 0.44f, 0.5f, H("ffb4a6") * 0.25f, 0.5f, SurfaceKind.HeartMuscle, 0.9f, 0.8f);
            case Category.Nerve:
                return new(H("efd36a"), 0.36f, 0.4f, H("fff4c0") * 0.3f, 0.35f, SurfaceKind.Nerve, 0.9f);
            case Category.Cns:
            {
                var tint = p.Material != null && Categories.MaterialTint.TryGetValue(p.Material, out var t) ? t : H("e7b7bb");
                return new(tint, 0.45f, 0.35f, Vector3.Zero, 0.5f, SurfaceKind.Brain, 0.6f);
            }
            case Category.Meninges:
                return new(H("d6ccd8"), 0.3f, 0.6f, Vector3.Zero, 0.4f, SurfaceKind.Wet, 0.25f);
            case Category.Sense:
            {
                var tint = p.Material != null && Categories.MaterialTint.TryGetValue(p.Material, out var t) ? t : H("eef0f2");
                return new(tint, 0.35f, 0.5f, Vector3.Zero, 0.4f, SurfaceKind.Wet, 0.4f);
            }
            case Category.Respiratory:
                if (en.Contains("lobe of")) return new(H("e3a0a0"), 0.6f, 0.45f, H("ffd0d0") * 0.25f, 0.6f, SurfaceKind.Wet, 1.1f, 1.8f);
                return new(H("e6d5c0"), 0.35f, 0.5f, Vector3.Zero, 0.4f, SurfaceKind.Tendon, 0.5f);
            case Category.Digestive:
            {
                var organ = en switch
                {
                    "liver" => H("7a2f25"), "gallbladder" => H("4f7a3a"), "pancreas" => H("e0b38a"), "stomach" => H("d99a8a"),
                    "tongue" => H("c9605f"), "oesophagus" => H("d08a7c"), "duodenum" => H("dca08a"), "jejunum" => H("e0a590"),
                    "spleen" => H("6e2a3a"),
                    _ => p.Material != null && Categories.MaterialTint.TryGetValue(p.Material, out var t) ? t : H("d98870"),
                };
                return new(organ, 0.38f, 0.75f, H("ffc0b0") * 0.15f, 0.55f, SurfaceKind.Wet, 0.6f);
            }
            case Category.Urinary:
                return new(en == "kidney" ? H("8a3027") : H("c9795b"), 0.38f, 0.7f, Vector3.Zero, 0.5f, SurfaceKind.Wet, 0.5f);
            case Category.Genital:
                return new(H("d98c9c"), 0.4f, 0.6f, Vector3.Zero, 0.55f, SurfaceKind.Wet, 0.5f);
            case Category.Endocrine:
                return new(H("e0b25a"), 0.4f, 0.6f, Vector3.Zero, 0.5f, SurfaceKind.Wet, 0.5f);
            case Category.Serosa:
                return new(H("eadccc"), 0.25f, 0.8f, Vector3.Zero, 0.3f, SurfaceKind.Wet, 0.25f);
            case Category.Attachment:
                return new(H("d04040"), 0.5f, 0.2f, Vector3.Zero, 0, SurfaceKind.None, 0);
        }
        return new(Categories.Get(p.Category).Color, 0.5f, 0, Vector3.Zero, 0, SurfaceKind.None, 0);
    }

    /// <summary>Doğrusal albedo; komşu yapılar ayırt edilsin diye ada bağlı küçük ton/parlaklık farkı eklenir.</summary>
    public static Vector3 LinearAlbedo(Part p, Appearance a)
    {
        var c = a.Albedo;
        if (!a.Glass && p.Category is not (Category.Skin or Category.Hair) && a.Surface is not (SurfaceKind.Iris or SurfaceKind.Sclera))
        {
            float j = Hash(p.En) - 0.5f;
            float k = p.Category is Category.Bone or Category.Teeth ? 0.35f : 0.8f;
            c *= 1f + j * 0.14f * k;
        }
        return SrgbToLinear(Vector3.Clamp(c, Vector3.Zero, Vector3.One));
    }

    /// <summary>Kas fonksiyonu renklendirme modunda albedo.</summary>
    public static Vector3 FunctionAlbedo(Part p, Appearance a)
    {
        if (p.Category == Category.Muscle && p.Material != null && Categories.MuscleFunction.TryGetValue(p.Material, out var f))
            return SrgbToLinear(f.Color);
        var gray = new Vector3(0.6f, 0.63f, 0.65f);
        return SrgbToLinear(Vector3.Lerp(a.Albedo, gray, 0.75f));
    }
}
