using System.Numerics;
using Anatomi3D.Core.Model;
using Anatomi3D.Graphics;

namespace Anatomi3D.Desktop.App;

/// <summary>
/// Atlas durumunu her karede yapı başına GPU durumuna çevirir: açılış animasyonu, katman soyma ve göster/gizle
/// taramaları, çift tıklama kazısı, deri penceresi, X-ray saydamlığı, seçim vurgusu, kalp atışı ve solunum.
/// </summary>
public sealed class SceneAnimator
{
    private readonly AnatomyModel model;
    private readonly int n;
    private readonly Appearance[] appearance;
    public PartMaterialGpu[] Materials { get; }
    private readonly float[] dig, peel, hide, introDelay;
    private readonly Vector3[] digOrigin;
    private readonly float[] digRadius;
    private readonly bool[] digArmed;
    private readonly Vector3[] physioPivot;
    private readonly byte[] physioKind; // 0 yok, 1 kalp, 2 akciğer
    private readonly float[] capBias;
    private float introStart = float.NaN;
    private ColorMode builtMode = (ColorMode)(-1);
    public bool Animating { get; private set; }
    public bool IntroDone { get; private set; }

    /// <summary>Kazı animasyonu (sn)</summary>
    private const float DigTime = 1.15f;
    /// <summary>Katman soyma / göster-gizle taraması (sn)</summary>
    private const float SweepTime = 1.0f;
    private const float SweepTop = 1.76f;

    public SceneAnimator(AnatomyModel model)
    {
        this.model = model;
        n = model.Parts.Length;
        appearance = model.Parts.Select(Appearances.Of).ToArray();
        Materials = new PartMaterialGpu[n];
        dig = new float[n];
        peel = new float[n];
        hide = Enumerable.Repeat(1f, n).ToArray();
        introDelay = new float[n];
        digOrigin = new Vector3[n];
        digRadius = new float[n];
        digArmed = new bool[n];
        physioPivot = new Vector3[n];
        physioKind = new byte[n];
        // kesit kapakları: iç içe yapılarda içteki önde görünsün (deri en arkada, kemik ve organlar önde)
        capBias = model.Parts.Select(p => 0.00004f * p.Category switch
        {
            Category.Skin or Category.Hair => 9,
            Category.Fascia or Category.Serosa or Category.Meninges => 8,
            Category.Muscle => 7,
            Category.Vein or Category.Artery or Category.Heart or Category.Nerve => 5,
            Category.Respiratory or Category.Digestive or Category.Urinary or Category.Genital or Category.Endocrine => 4,
            Category.Ligament or Category.Cartilage => 3,
            Category.Cns or Category.Sense => 2,
            _ => 1,
        } + (p.Index % 97) * 0.0000002f).ToArray();

        // açılışta katmanlar sırayla belirir: önce iskelet, en son deri
        foreach (var p in model.Parts)
            introDelay[p.Index] = p.Category switch
            {
                Category.Bone => 0f, Category.Teeth or Category.Cartilage => 0.1f, Category.Ligament => 0.35f,
                Category.Cns or Category.Sense or Category.Meninges => 0.55f,
                Category.Heart or Category.Respiratory => 0.7f,
                Category.Digestive or Category.Urinary or Category.Genital or Category.Endocrine or Category.Serosa => 0.75f,
                Category.Artery => 0.95f, Category.Vein => 1.0f, Category.Nerve => 1.1f,
                Category.Muscle or Category.Fascia => 1.35f, Category.Skin => 2.1f, Category.Hair => 2.3f,
                _ => 1f,
            };

        // canlı fizyoloji: kalp ve akciğer loblarının ölçekleneceği merkezler
        Vector3 Mid(IEnumerable<Part> ps)
        {
            var list = ps.ToList();
            return list.Count == 0 ? Vector3.Zero : list.Aggregate(Vector3.Zero, (a, p) => a + p.Center) / list.Count;
        }
        var heart = model.Parts.Where(p => p.Category == Category.Heart).ToList();
        var lungR = model.Parts.Where(p => p.En.Contains("lobe of right lung", StringComparison.OrdinalIgnoreCase)).ToList();
        var lungL = model.Parts.Where(p => p.En.Contains("lobe of left lung", StringComparison.OrdinalIgnoreCase)).ToList();
        var hc = Mid(heart);
        var rc = Mid(lungR);
        var lc = Mid(lungL);
        foreach (var p in heart) { physioKind[p.Index] = 1; physioPivot[p.Index] = hc; }
        foreach (var p in lungR) { physioKind[p.Index] = 2; physioPivot[p.Index] = rc; }
        foreach (var p in lungL) { physioKind[p.Index] = 2; physioPivot[p.Index] = lc; }
    }

    public Appearance AppearanceOf(int part) => appearance[part];

    /// <summary>Malzeme tablosunu (renk moduna göre) üretir; değiştiyse true.</summary>
    public bool BuildMaterials(ColorMode mode)
    {
        if (mode == builtMode) return false;
        builtMode = mode;
        var parts = model.Parts;
        for (int i = 0; i < n; i++)
        {
            var p = parts[i];
            var a = appearance[i];
            var (family, kind) = a.Surface switch
            {
                SurfaceKind.Muscle => (SurfaceFamily.Fiber, 1u),
                SurfaceKind.Tendon => (SurfaceFamily.Fiber, 2u),
                SurfaceKind.Nerve => (SurfaceFamily.Fiber, 3u),
                SurfaceKind.Vessel => (SurfaceFamily.Fiber, 4u),
                SurfaceKind.HeartMuscle => (SurfaceFamily.Fiber, 5u),
                SurfaceKind.Bone => (SurfaceFamily.Bone, 0u),
                SurfaceKind.Skin => (SurfaceFamily.Skin, 0u),
                SurfaceKind.Wet => (SurfaceFamily.Wet, 1u),
                SurfaceKind.Brain => (SurfaceFamily.Wet, 2u),
                SurfaceKind.Iris => (SurfaceFamily.Eye, 1u),
                SurfaceKind.Sclera => (SurfaceFamily.Eye, 2u),
                SurfaceKind.Glass => (SurfaceFamily.Glass, 0u),
                _ => (SurfaceFamily.None, 0u),
            };
            var m = new PartMaterialGpu
            {
                Albedo = mode == ColorMode.Function ? Appearances.FunctionAlbedo(p, a) : Appearances.LinearAlbedo(p, a),
                Roughness = a.Roughness,
                Sheen = Appearances.SrgbToLinear(Vector3.Clamp(a.Sheen, Vector3.Zero, Vector3.One)),
                Clearcoat = a.Clearcoat * 0.4f,
                Center = p.Center, Param = 0, Axis = p.Axis, Family = (uint)family, Kind = kind,
                Bump = a.Bump, Freq = a.Freq, Subsurface = a.Subsurface,
            };
            if (a.Surface == SurfaceKind.Iris)
            {
                m.Axis = p.Minor;
                m.Param = p.Radius;
            }
            else if (a.Surface == SurfaceKind.Sclera)
            {
                // göz küresi merkezi ve limbus açısı: aynı taraftaki arka segment ve iris yarıçapından
                var ball = parts.FirstOrDefault(q => q.En == "Posterior segment of eyeball" && q.Side == p.Side);
                var iris = parts.FirstOrDefault(q => q.En == "Iris" && q.Side == p.Side);
                if (ball != null) m.Center = ball.Center;
                float R = ball?.Radius ?? p.Radius;
                float ri = (iris?.Radius ?? R * 0.5f) * 0.98f;
                m.Param = MathF.Cos(MathF.Asin(MathF.Min(0.95f, ri / R)));
                m.Axis = Vector3.UnitZ;
            }
            Materials[i] = m;
        }
        return true;
    }

    public void StartIntro(float time)
    {
        introStart = time;
        IntroDone = false;
    }

    public void SkipIntro(float time)
    {
        introStart = time - 10f;
        Array.Fill(hide, 0f);
    }

    private static float EaseInOut(float t) => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;

    private static float Approach(float cur, float target, float step) =>
        cur < target ? MathF.Min(target, cur + step) : MathF.Max(target, cur - step);

    /// <summary>Durumdan bu karenin çizim girdisini üretir.</summary>
    public void Update(AtlasState s, float time, float dt, FrameInput f, Vector3 selectColor)
    {
        if (f.States.Length != n)
        {
            f.States = new PartStateGpu[n];
            f.Render = new PartRender[n];
        }
        dt = MathF.Min(dt, 0.05f);
        var parts = model.Parts;
        bool anim = false;
        bool started = !float.IsNaN(introStart);
        if (started && time - introStart < 3f) anim = true;
        else if (started) IntroDone = true;

        int quizTarget = s.Quiz is { Last: { ok: false } } q ? q.Target : -1;
        bool quizDeep = quizTarget >= 0 && parts[quizTarget].Category != Category.Skin;
        bool ghostMode = (s.Reveal && s.Selected >= 0) || quizDeep;
        bool xray = s.ViewMode == ViewMode.XRay || ghostMode;
        bool clipOn = s.ClipAxis != ClipAxis.None;
        bool occlude = !clipOn && s.ViewMode == ViewMode.Solid && s.Isolated == null && !ghostMode;

        // kazı hedefleri ve deride açılan pencereler
        var dugOrigin = new Dictionary<int, Vector3>();
        foreach (var d in s.Dug) foreach (var id in d.Parts) dugOrigin[id] = d.Origin;
        f.Holes.Clear();
        int holeCount = 0;
        foreach (var p in parts)
        {
            if (p.Category != Category.Skin) continue;
            int i = p.Index;
            if (dig[i] > 0 || dugOrigin.ContainsKey(i) || s.HiddenParts.Contains(i))
            {
                if (holeCount < 16) f.Holes.Add(new Vector4(p.Center, MathF.Max(p.Radius * 1.3f, 0.09f)));
                holeCount++;
            }
        }
        if (holeCount > 16) { occlude = false; f.Holes.Clear(); }

        // fizyoloji: kalp ~70/dk (iki vuruşlu), solunum ~14/dk
        float ph = time % 0.86f / 0.86f;
        float beat = MathF.Exp(-MathF.Pow((ph - 0.08f) / 0.05f, 2)) + 0.55f * MathF.Exp(-MathF.Pow((ph - 0.3f) / 0.06f, 2));
        float breath = 0.5f - 0.5f * MathF.Cos(time / 4.3f * MathF.PI * 2);
        float pulse = 0.5f + 0.5f * MathF.Sin(time * 3.6f);

        var selMuscle = s.Selected >= 0 && s.ShowAttachments ? parts[s.Selected] : null;
        var attachShown = new HashSet<int>();
        if (selMuscle != null)
        {
            foreach (var a in selMuscle.Attachments) attachShown.Add(a.Index);
            // bir kas başı/parçası seçiliyse üst kasın yapışmalarını da göster
            if (selMuscle.Attachments.Count == 0 && selMuscle.Parent != null)
                foreach (var a in selMuscle.Parent.Attachments) attachShown.Add(a.Index);
        }

        for (int i = 0; i < n; i++)
        {
            var p = parts[i];
            ref var st = ref f.States[i];
            var ap = appearance[i];

            if (p.IsAttachment)
            {
                bool on = attachShown.Contains(i) && hide[i] < 1;
                hide[i] = Approach(hide[i], attachShown.Contains(i) ? 0 : 1, dt / 0.35f);
                st = new PartStateGpu
                {
                    Pivot = p.Center, Scale = 1, Opacity = 1,
                    Flags = (uint)(p.AttachmentKind == AttachmentKind.Origin ? PartStateFlags.AttachOrigin : PartStateFlags.AttachInsertion),
                    Dissolve = hide[i] > 0 ? EaseInOut(hide[i]) : 0,
                    DigOrigin = new Vector3(0, SweepTop, 0), DigDir = -Vector3.UnitY, DigRadius = SweepTop,
                };
                if (hide[i] > 0) st.Flags |= (uint)PartStateFlags.Sweep;
                f.Render[i] = on || hide[i] < 1 ? PartRender.Opaque : PartRender.Hidden;
                if (hide[i] is > 0 and < 1) anim = true;
                continue;
            }

            bool outer = p.Category is Category.Skin or Category.Hair or Category.Sense;
            bool shown = started && time >= introStart + introDelay[i] && !s.HiddenCats.Contains(p.Category) && !s.HiddenParts.Contains(i)
                         && (s.Isolated == null || s.Isolated.Contains(i));

            float pt = Categories.IsPeeled(p.Category, s.PeelStep) ? 1 : 0;
            if (peel[i] != pt) { peel[i] = Approach(peel[i], pt, dt / SweepTime); anim = true; }
            float ht = shown ? 0 : 1;
            if (hide[i] != ht) { hide[i] = Approach(hide[i], ht, dt / (ht > hide[i] ? SweepTime : SweepTime * 1.1f)); anim = true; }
            float sweep = MathF.Max(peel[i], hide[i]);

            bool hasOrigin = dugOrigin.TryGetValue(i, out var origin);
            if (hasOrigin && dig[i] == 0 && !digArmed[i])
            {
                digOrigin[i] = origin;
                digRadius[i] = Vector3.Distance(origin, p.Center) + p.Radius + 0.005f;
                digArmed[i] = true;
            }
            float dt2 = hasOrigin ? 1 : 0;
            if (dig[i] != dt2) { dig[i] = Approach(dig[i], dt2, dt / DigTime); anim = true; }
            if (dig[i] == 0 && !hasOrigin) digArmed[i] = false;

            st.Pivot = p.Center;
            st.Scale = 1;
            st.Flags = 0;
            st.Emissive = 0;
            st.CapBias = capBias[i];
            if (dig[i] > 0)
            {
                st.DigOrigin = digOrigin[i];
                st.DigRadius = digRadius[i];
                st.Dissolve = 1 - (1 - dig[i]) * (1 - dig[i]);
            }
            else if (sweep > 0)
            {
                st.Flags |= (uint)PartStateFlags.Sweep;
                st.DigOrigin = new Vector3(0, SweepTop, 0);
                st.DigDir = -Vector3.UnitY;
                st.DigRadius = SweepTop;
                st.Dissolve = EaseInOut(sweep);
            }
            else st.Dissolve = 0;
            bool isHole = p.Category == Category.Skin && (dig[i] > 0 || hasOrigin || s.HiddenParts.Contains(i));

            bool isSel = s.Selected == i;
            bool isHov = s.Hovered == i;
            bool isQuiz = quizTarget == i;
            float opacity = ap.Glass ? ap.GlassOpacity : 1;
            // normalde görünmeyen yapılar (göz sıvıları, zonula) seçilince / izole edilince görünür
            if (ap.Glass && opacity == 0 && (isSel || (s.Isolated?.Contains(i) ?? false))) opacity = 0.45f;
            if (xray && !isSel && !isQuiz)
            {
                if (ghostMode) opacity *= p.Category == Category.Bone ? 0.12f : p.Category == Category.Skin ? 0.03f : 0.06f;
                else opacity *= p.Category == Category.Skin ? 0.06f : p.Category == Category.Bone ? 0.28f : 0.16f;
            }
            if (p.Category == Category.Skin && !isHole && (sweep > 0 || opacity < 0.995f)) occlude = false;

            // deri kapalıyken iç yapılar yalnız deride açılan bir pencereye yakınsa çizilir
            bool windowed = false, inner = true;
            if (!outer && occlude)
            {
                windowed = true;
                inner = false;
                foreach (var h in f.Holes)
                    if (Vector3.Distance(new Vector3(h.X, h.Y, h.Z), p.Center) < h.W + p.Radius) { inner = true; break; }
            }
            if (windowed) st.Flags |= (uint)PartStateFlags.Windowed;

            bool visible = opacity > 0.01f && dig[i] < 1 && sweep < 1 && inner;
            f.Render[i] = !visible ? PartRender.Hidden : ap.Glass ? PartRender.Glass : opacity < 0.995f ? PartRender.Ghost : PartRender.Opaque;
            st.Opacity = opacity;

            // kalp atışı / solunum
            if (s.Alive && physioKind[i] != 0 && visible)
            {
                st.Scale = physioKind[i] == 1 ? 1 - 0.045f * beat : 1 + 0.035f * breath;
                st.Pivot = physioPivot[i];
                anim = true;
            }

            // vurgu
            if (isQuiz)
            {
                st.Tint = new Vector3(0.19f, 0.64f, 0.42f);
                st.TintAmount = 0.55f + 0.35f * pulse;
                st.Flags |= (uint)PartStateFlags.Quiz;
            }
            else if (isSel)
            {
                st.Tint = selectColor;
                st.TintAmount = 0.28f + 0.14f * pulse;
                st.Flags |= (uint)PartStateFlags.Selected;
            }
            else if (isHov)
            {
                st.Tint = new Vector3(0.45f, 0.75f, 1f);
                st.TintAmount = 0.28f;
                st.Flags |= (uint)PartStateFlags.Hovered;
            }
            else st.TintAmount = 0;
        }
        if (s.Selected >= 0 || quizTarget >= 0) anim = true; // nabız efekti
        Animating = anim;
    }
}
