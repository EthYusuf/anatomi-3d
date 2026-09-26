using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anatomi3D.Core.Content;
using Anatomi3D.Graphics;

namespace Anatomi3D.Desktop.App;

public enum QualityPreset { Auto, Ultra, High, Medium, Low, Custom }

/// <summary>Kullanıcı ayarları: %APPDATA%\Anatomi3D\settings.json</summary>
public sealed class Settings
{
    public Lang Language { get; set; } = Lang.Tr;
    public QualityPreset Quality { get; set; } = QualityPreset.Auto;
    public uint Msaa { get; set; } = 4;
    public bool Shadows { get; set; } = true;
    public int ShadowMapSize { get; set; } = 2048;
    public bool Ssao { get; set; } = true;
    public bool Bloom { get; set; } = true;
    public bool Tessellation { get; set; } = true;
    public bool ProceduralDetail { get; set; } = true;
    public float LodPixelError { get; set; } = 0.75f;
    public bool VSync { get; set; } = true;
    public bool PreferIntegrated { get; set; }
    public float UiScale { get; set; } = 1f;
    public float Exposure { get; set; } = 1f;
    public bool ShowFps { get; set; }
    public bool ShowAttachments { get; set; } = true;
    public bool Physiology { get; set; } = true;
    public bool FirstRun { get; set; } = true;

    [JsonIgnore] public Vector3 SelectColor => new(0.2f, 0.58f, 1.0f);
    /// <summary>false: ayarlar diske yazılmaz (betikli/otomatik çalıştırmalar kullanıcı ayarlarını değiştirmesin)</summary>
    [JsonIgnore] public bool Persist { get; set; } = true;

    private static string PathOf() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anatomi3D", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load()
    {
        try
        {
            var p = PathOf();
            if (File.Exists(p)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(p), Json) ?? new Settings();
        }
        catch { /* bozuk ayar dosyası: varsayılanlar */ }
        return new Settings();
    }

    public void Save()
    {
        if (!Persist) return;
        try
        {
            var p = PathOf();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
            File.WriteAllText(p, JsonSerializer.Serialize(this, Json));
        }
        catch { /* yazılamadı: yoksay */ }
    }

    /// <summary>GPU'ya göre önerilen kalite: tümleşik GPU → Orta, 4 GB+ harici → Ultra, diğer harici → Yüksek.</summary>
    public static QualityPreset Recommended(GraphicsDevice gd) =>
        gd.IsIntegrated ? QualityPreset.Medium : gd.DedicatedVideoMemory >= 6UL << 30 ? QualityPreset.Ultra : QualityPreset.High;

    public void ApplyPreset(QualityPreset p, GraphicsDevice gd)
    {
        Quality = p;
        var eff = p == QualityPreset.Auto ? Recommended(gd) : p;
        switch (eff)
        {
            case QualityPreset.Ultra:
                Msaa = 8; Shadows = true; ShadowMapSize = 4096; Ssao = true; Bloom = true; Tessellation = true; ProceduralDetail = true; LodPixelError = 0.5f;
                break;
            case QualityPreset.High:
                Msaa = 4; Shadows = true; ShadowMapSize = 2048; Ssao = true; Bloom = true; Tessellation = true; ProceduralDetail = true; LodPixelError = 0.75f;
                break;
            case QualityPreset.Medium:
                Msaa = 2; Shadows = true; ShadowMapSize = 1536; Ssao = true; Bloom = true; Tessellation = false; ProceduralDetail = true; LodPixelError = 1.2f;
                break;
            case QualityPreset.Low:
                Msaa = 1; Shadows = false; ShadowMapSize = 1024; Ssao = false; Bloom = false; Tessellation = false; ProceduralDetail = false; LodPixelError = 2f;
                break;
        }
    }

    public RenderSettings ToRenderSettings(GraphicsDevice gd)
    {
        if (Quality == QualityPreset.Auto) ApplyPreset(QualityPreset.Auto, gd);
        return new RenderSettings
        {
            Msaa = Msaa, Shadows = Shadows, ShadowMapSize = ShadowMapSize, Ssao = Ssao, Bloom = Bloom, Tessellation = Tessellation,
            ProceduralDetail = ProceduralDetail, LodPixelError = LodPixelError,
            TessTargetPx = Quality == QualityPreset.Ultra ? 5f : 6.5f, TessMaxFactor = Quality == QualityPreset.Ultra ? 16f : 12f,
        };
    }
}
