using System.Numerics;
using Anatomi3D.Core.Content;
using Anatomi3D.Core.Model;

namespace Anatomi3D.Desktop.App;

public enum ViewMode { Solid, XRay, Wire }
public enum ColorMode { Anatomic, Function }
public enum ClipAxis { None, Sagittal, Coronal, Axial }
public enum CameraPreset { Home, Front, Back, Left, Right, Top, Bottom, Iso, Focus }

/// <summary>Çift tıklamayla kaldırılan (kazılan) yapı; geri alma yığınında tutulur.</summary>
public sealed record DigEntry(int Part, int[] Parts, Vector3 Origin);

public sealed class QuizState
{
    public int Target = -1;
    public int Score;
    public int Total;
    public int Streak;
    public int BestStreak;
    /// <summary>Son cevap: null = bekleniyor</summary>
    public (bool ok, int picked)? Last;
    public float NextAt = -1;
}

/// <summary>
/// Atlasın kullanıcı tarafından değiştirilen durumu (görünürlük, seçim, kazı, katman, kesit, mod).
/// Değişiklikler <see cref="Version"/> sayacını artırır; görünüm ve arayüz buna göre güncellenir.
/// </summary>
public sealed class AtlasState
{
    private readonly AnatomyModel model;
    public AtlasState(AnatomyModel model)
    {
        this.model = model;
        HiddenCats = DefaultHidden();
    }

    public int Version { get; private set; }
    private void Touch() => Version++;

    public Lang Lang { get; set; } = Lang.Tr;
    public int PeelStep { get; private set; }
    public HashSet<Category> HiddenCats { get; private set; }
    public HashSet<int> HiddenParts { get; } = [];
    public List<DigEntry> Dug { get; } = [];
    public HashSet<int>? Isolated { get; private set; }
    public int Selected { get; private set; } = -1;
    public bool Reveal { get; private set; }
    public int Hovered { get; set; } = -1;
    public ViewMode ViewMode { get; private set; } = ViewMode.Solid;
    public ColorMode ColorMode { get; private set; } = ColorMode.Anatomic;
    public ClipAxis ClipAxis { get; private set; } = ClipAxis.None;
    public float ClipPos { get; private set; }
    public bool ClipFlip { get; private set; }
    public QuizState? Quiz { get; set; }
    public bool Alive { get; set; } = true;
    public bool ShowAttachments { get; set; } = true;

    /// <summary>Kamera isteği (kare döngüsü tarafından tüketilir)</summary>
    public (CameraPreset preset, int part, float duration)? CameraRequest { get; set; }

    private static HashSet<Category> DefaultHidden() => Categories.All.Where(c => c.HiddenByDefault).Select(c => c.Id).ToHashSet();

    public void Peel() { if (PeelStep < Categories.PeelSteps.Length) { PeelStep++; Touch(); } }
    public void Unpeel() { if (PeelStep > 0) { PeelStep--; Touch(); } }
    public void SetPeel(int n) { PeelStep = Math.Clamp(n, 0, Categories.PeelSteps.Length); Touch(); }

    public void ResetAll()
    {
        PeelStep = 0;
        HiddenCats = DefaultHidden();
        HiddenParts.Clear();
        Dug.Clear();
        Isolated = null;
        Selected = -1;
        Reveal = false;
        ClipAxis = ClipAxis.None;
        ClipPos = 0;
        ViewMode = ViewMode.Solid;
        ColorMode = ColorMode.Anatomic;
        CameraRequest = (CameraPreset.Home, -1, 0.9f);
        Touch();
    }

    public void ToggleCategory(Category c)
    {
        if (!HiddenCats.Remove(c)) HiddenCats.Add(c);
        Touch();
    }

    public void SetCategories(IEnumerable<Category> cats, bool hidden)
    {
        foreach (var c in cats)
            if (hidden) HiddenCats.Add(c);
            else HiddenCats.Remove(c);
        Touch();
    }

    public void ShowOnlyCategories(IEnumerable<Category> cats)
    {
        var keep = cats.ToHashSet();
        HiddenCats = Categories.All.Select(c => c.Id).Where(c => !keep.Contains(c)).ToHashSet();
        PeelStep = 0;
        Isolated = null;
        Touch();
    }

    public void TogglePart(int p)
    {
        if (!HiddenParts.Remove(p))
        {
            HiddenParts.Add(p);
            if (Selected == p) Selected = -1;
        }
        Touch();
    }

    public void SetPartsHidden(IEnumerable<int> parts, bool hidden)
    {
        foreach (var p in parts)
            if (hidden) HiddenParts.Add(p);
            else HiddenParts.Remove(p);
        Touch();
    }

    public void HideSelected()
    {
        if (Selected < 0) return;
        HiddenParts.Add(Selected);
        Selected = -1;
        Touch();
    }

    public void UnhideAll()
    {
        HiddenParts.Clear();
        HiddenCats = DefaultHidden();
        Isolated = null;
        PeelStep = 0;
        Dug.Clear();
        Touch();
    }

    public void Isolate(IEnumerable<int>? parts)
    {
        if (parts == null)
        {
            Isolated = null;
            CameraRequest = (CameraPreset.Home, -1, 0.9f);
        }
        else
        {
            Isolated = parts.ToHashSet();
            // izole edilen yapıların kategorileri görünür olsun
            foreach (var p in Isolated) HiddenCats.Remove(model.Parts[p].Category);
            int first = Isolated.First();
            CameraRequest = (CameraPreset.Focus, Isolated.Count == 1 ? first : -2, 0.9f);
        }
        Touch();
    }

    /// <summary>Seçim; <paramref name="focus"/> ile kameraya odaklan ve çevreyi saydamlaştır.</summary>
    public void Select(int p, bool focus = false)
    {
        Selected = p;
        Reveal = focus && p >= 0;
        if (focus && p >= 0) CameraRequest = (CameraPreset.Focus, p, 0.8f);
        Touch();
    }

    public void SetReveal(bool v) { Reveal = v; Touch(); }
    public void SetViewMode(ViewMode m) { ViewMode = m; Touch(); }
    public void SetColorMode(ColorMode m) { ColorMode = m; Touch(); }
    public void SetClip(ClipAxis a) { ClipAxis = a; ClipPos = 0; Touch(); }
    public void SetClipPos(float v) { ClipPos = Math.Clamp(v, -1, 1); Touch(); }
    public void ToggleClipFlip() { ClipFlip = !ClipFlip; Touch(); }

    public void RequestCamera(CameraPreset p, int part = -1, float duration = 0.8f) => CameraRequest = (p, part, duration);

    public bool Dig(int part, int[] parts, Vector3 origin)
    {
        foreach (var d in Dug) if (Array.IndexOf(d.Parts, part) >= 0) return false;
        Dug.Add(new DigEntry(part, parts, origin));
        if (Selected >= 0 && Array.IndexOf(parts, Selected) >= 0) Selected = -1;
        Touch();
        return true;
    }

    public void UndoDig()
    {
        if (Dug.Count == 0) return;
        Dug.RemoveAt(Dug.Count - 1);
        Touch();
    }

    public void RestoreDigs()
    {
        Dug.Clear();
        Touch();
    }

    public void Changed() => Touch();
}
