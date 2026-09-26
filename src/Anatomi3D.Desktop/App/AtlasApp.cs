using System.Diagnostics;
using System.Numerics;
using Anatomi3D.Core.Content;
using Anatomi3D.Core.Model;
using Anatomi3D.Core.Pack;
using Anatomi3D.Desktop.Platform;
using Anatomi3D.Desktop.UI;
using Anatomi3D.Graphics;
using ImGuiNET;
using Vortice.Direct3D11;
using Vortice.DXGI;
using GpuPreference = Anatomi3D.Graphics.GpuPreference;

namespace Anatomi3D.Desktop.App;

/// <summary>Komut satırı seçenekleri.</summary>
public sealed class AppOptions
{
    public string DataDir = Path.Combine(AppContext.BaseDirectory, "Data");
    public GpuPreference Gpu = GpuPreference.HighPerformance;
    public int Width = 1600, Height = 900;
    public bool Maximized = true;
    public string? Script;
    public bool NoIntro;
    public bool Debug;
    public string? ContentReport;
}

/// <summary>
/// Uygulama: pencere, GPU, model yükleme, kare döngüsü, giriş yönlendirme, seçim, kamera ve arayüz.
/// </summary>
public sealed class AtlasApp : IDisposable
{
    private readonly AppOptions opt;
    private readonly Window window;
    private readonly GraphicsDevice gd;
    private readonly SwapChainTarget swap;
    private readonly ImGuiBackend imgui;
    public Settings Settings { get; }

    // yükleme
    private Task<(PackData pack, AnatomyModel model)>? loadTask;
    private float loadProgress;
    private string? loadError;

    // sahne
    public AnatomyModel? Model { get; private set; }
    public AtlasState? State { get; private set; }
    public AnatomyRenderer? Renderer { get; private set; }
    private SceneAnimator? animator;
    private AtlasUI? ui;
    public OrbitCamera Camera { get; } = new();
    private readonly FrameInput frame = new();
    private int lastStateVersion = -1;

    // zaman
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTime;
    public float Time { get; private set; }
    public float Fps { get; private set; }
    private float fpsAccum;
    private int fpsFrames;
    public float FrameMs { get; private set; }

    // fare
    private Vector2 mouse;
    private Vector2 downPos;
    private float downTime;
    private MouseButton? dragButton;
    private bool dragMoved;
    private bool mouseInWindow;
    private bool hoverDirty;
    public Vector3? HoverWorld { get; private set; }
    private float lastClickTime = -10;
    private Vector2 lastClickPos;
    public Vector2 MousePos => mouse;

    // ekran görüntüsü / betik
    private readonly Queue<string> script = new();
    private int scriptWait;
    private string? pendingShot;
    private bool quitRequested;
    public bool UiVisible { get; set; } = true;
    private int redrawFrames = 3;

    public const int TokenHover = 1, TokenClick = 2, TokenDig = 3;

    public AtlasApp(AppOptions opt)
    {
        this.opt = opt;
        Settings = Settings.Load();
        Settings.Persist = opt.Script == null;
        window = new Window("Anatomi 3D — Anatomi Atlası", opt.Width, opt.Height);
        gd = GraphicsDevice.Create(Settings.PreferIntegrated ? GpuPreference.LowPower : opt.Gpu, opt.Debug);
        swap = new SwapChainTarget(gd, window.Handle, window.Width, window.Height);
        imgui = new ImGuiBackend(gd, window);
        window.Resized += (w, h) => { swap.Resize(w, h); Renderer?.Resize(w, h); redrawFrames = 3; };
        window.DpiChanged += s => { imgui.SetScale(s * Settings.UiScale); redrawFrames = 3; };
        window.ModalFrame = () => Frame(modal: true);
        if (Math.Abs(Settings.UiScale - 1f) > 0.01f) imgui.SetScale(window.DpiScale * Settings.UiScale);
        if (opt.Script != null)
            foreach (var c in opt.Script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) script.Enqueue(c);
        window.Show(opt.Maximized && opt.Script == null);
        StartLoading();
    }

    public GraphicsDevice Device => gd;
    public Window Window => window;
    public ImGuiBackend Gui => imgui;

    private void StartLoading()
    {
        string pak = Path.Combine(opt.DataDir, "anatomy.pak");
        string names = Path.Combine(opt.DataDir, "content", "names.tr.json");
        loadTask = Task.Run(() =>
        {
            if (!File.Exists(pak))
                throw new FileNotFoundException(
                    $"Model paketi bulunamadı: {pak}\n\n" +
                    "Kaynak koddan derlediyseniz paketi indirmek için depo kökünde şunu çalıştırın:\n" +
                    "powershell -ExecutionPolicy Bypass -File tools\\veri-indir.ps1\n\n" +
                    "Ardından uygulamayı yeniden derleyin (dotnet build).", pak);
            var pack = PackReader.Read(pak, p => loadProgress = p * 0.9f);
            var tr = TurkishNames.Load(names);
            var model = AnatomyModel.FromPack(pack, tr);
            ContentLibrary.Load(Path.Combine(opt.DataDir, "content"));
            loadProgress = 0.95f;
            return (pack, model);
        });
    }

    private void FinishLoading()
    {
        var (pack, model) = loadTask!.Result;
        loadTask = null;
        Model = model;
        State = new AtlasState(model) { Lang = Settings.Language };
        Renderer = new AnatomyRenderer(gd, pack, Settings.ToRenderSettings(gd));
        Renderer.Resize(window.Width, window.Height);
        animator = new SceneAnimator(model);
        animator.BuildMaterials(ColorMode.Anatomic);
        Renderer.UploadMaterials(animator.Materials);
        ui = new AtlasUI(this);
        if (opt.NoIntro || opt.Script != null) animator.SkipIntro(Time);
        else animator.StartIntro(Time + 0.15f);
        Camera.Target = new Vector3(0, 0.92f, 0);
        Camera.Distance = 4.1f;
        Camera.Yaw = 0;
        Camera.Pitch = 0.02f;
        if (!opt.NoIntro && opt.Script == null)
        {
            // açılış: uzaktan ve hafif yandan süzülerek yaklaş
            Camera.Target = new Vector3(0, 1.1f, 0);
            Camera.Distance = 6.2f;
            Camera.Yaw = 0.35f;
            Camera.Pitch = 0.12f;
            Camera.Snap();
            Camera.FlyTo(new Vector3(0, 0.92f, 0), 4.1f, 0, 0.02f, 2.6f);
        }
        else Camera.Snap();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
    }

    public void Run()
    {
        while (!quitRequested && window.PumpMessages())
        {
            if (window.Minimized)
            {
                window.WaitForInput(50);
                continue;
            }
            bool busy = loadTask != null || redrawFrames > 0 || script.Count > 0 || scriptWait > 0 || pendingShot != null ||
                        (animator?.Animating ?? false) || Camera.IsAnimating || window.Events.Count > 0 || dragButton != null ||
                        (ui?.NeedsRedraw ?? false);
            if (!busy)
            {
                // boşta: ileti gelene kadar bekle (dizüstünde pil ve fan dostu)
                window.WaitForInput(250);
                if (window.Events.Count == 0) { redrawFrames = 1; }
            }
            Frame(modal: false);
        }
    }

    private void Frame(bool modal)
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - lastTime, 0.0001, 0.1);
        lastTime = now;
        Time = (float)now;
        fpsAccum += dt;
        fpsFrames++;
        if (fpsAccum > 0.5f) { Fps = fpsFrames / fpsAccum; fpsAccum = 0; fpsFrames = 0; }
        var tStart = clock.Elapsed.TotalMilliseconds;
        if (redrawFrames > 0) redrawFrames--;

        if (loadTask != null && loadTask.IsCompleted)
        {
            if (loadTask.IsFaulted)
            {
                loadError = loadTask.Exception?.GetBaseException().Message ?? "Bilinmeyen hata";
                loadTask = null;
            }
            else FinishLoading();
        }

        var events = new List<InputEvent>(window.Events);
        window.Events.Clear();
        imgui.ProcessEvents(events);
        imgui.NewFrame(dt, consumeEvents: false);
        var io = ImGui.GetIO();

        if (Model != null && State != null && Renderer != null)
        {
            HandleInput(events, io);
            RunScript();
            ApplyCameraRequest();
            Camera.Update(dt);

            // hover sorgusu: fare hareket ettiyse
            if (hoverDirty && mouseInWindow && !io.WantCaptureMouse && dragButton == null)
            {
                Renderer.RequestPick(TokenHover, (int)mouse.X, (int)mouse.Y);
                hoverDirty = false;
            }
            else if (io.WantCaptureMouse && State.Hovered >= 0) { State.Hovered = -1; }

            if (State.Version != lastStateVersion || animator!.BuildMaterials(State.ColorMode))
            {
                if (animator!.BuildMaterials(State.ColorMode) || lastStateVersion < 0) Renderer.UploadMaterials(animator.Materials);
                lastStateVersion = State.Version;
            }
            animator!.Update(State, Time, dt, frame, Settings.SelectColor);
            FillFrame();
            ui!.Draw(dt);
        }
        else
        {
            AtlasUI.DrawSplash(this, loadProgress, loadError);
        }

        var ctx = gd.Context;
        var bb = swap.BackBufferView!;
        if (Renderer != null && Model != null)
        {
            Renderer.Render(frame, bb);
            ProcessPicks();
        }
        else
        {
            ctx.ClearRenderTargetView(bb, new Vortice.Mathematics.Color4(0.055f, 0.066f, 0.09f, 1f));
        }
        if (UiVisible || Model == null) imgui.Render(bb);
        else { ImGui.Render(); }

        if (pendingShot != null)
        {
            SaveBackBuffer(pendingShot);
            pendingShot = null;
        }
        FrameMs = (float)(clock.Elapsed.TotalMilliseconds - tStart);
        if (!swap.Present(Settings.VSync || modal)) loadError ??= "GPU cihazı kayboldu (sürücü sıfırlandı). Lütfen uygulamayı yeniden başlatın.";
    }

    private void FillFrame()
    {
        var s = State!;
        float aspect = window.Width / (float)Math.Max(1, window.Height);
        frame.Camera = Camera.State(aspect);
        frame.Time = Time;
        frame.Wireframe = s.ViewMode == ViewMode.Wire;
        frame.Selected.Clear();
        if (s.Selected >= 0 && frame.Render.Length > s.Selected && frame.Render[s.Selected] != PartRender.Hidden) frame.Selected.Add(s.Selected);
        if (s.Quiz is { Last: { ok: false } } q && q.Target >= 0) frame.Selected.Add(q.Target);
        frame.Hovered = s.Quiz == null ? s.Hovered : -1;
        frame.SelectColor = Settings.SelectColor;
        frame.Clip = ClipPlane(s);
        frame.Exposure = Settings.Exposure;
    }

    private Vector4? ClipPlane(AtlasState s)
    {
        if (s.ClipAxis == ClipAxis.None) return null;
        var mn = Model!.BoundsMin;
        var mx = Model.BoundsMax;
        var mid = (mn + mx) * 0.5f;
        var size = mx - mn;
        float sign = s.ClipFlip ? -1 : 1;
        return s.ClipAxis switch
        {
            // n·p + d ≥ 0 tutulur; varsayılan: sagittalde sağ yarı (−x) kalır, koronalde arka yarı, transverste alt yarı
            ClipAxis.Sagittal => new Vector4(-sign, 0, 0, sign * (mid.X + s.ClipPos * size.X / 2)),
            ClipAxis.Coronal => new Vector4(0, 0, -sign, sign * (mid.Z + s.ClipPos * size.Z / 2)),
            _ => new Vector4(0, -sign, 0, sign * (mid.Y + s.ClipPos * size.Y / 2)),
        };
    }

    // ------------------------------------------------------------------------------------------------ giriş
    private void HandleInput(List<InputEvent> events, ImGuiIOPtr io)
    {
        var s = State!;
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case InputKind.MouseMove:
                {
                    var p = new Vector2(e.X, e.Y);
                    var d = p - mouse;
                    mouse = p;
                    mouseInWindow = true;
                    hoverDirty = true;
                    if (dragButton != null)
                    {
                        if ((p - downPos).Length() > 4) dragMoved = true;
                        if (dragMoved)
                        {
                            bool pan = dragButton == MouseButton.Right || dragButton == MouseButton.Middle || io.KeyShift;
                            if (pan) Camera.Pan(d.X, d.Y, window.Height);
                            else Camera.Orbit(d.X, d.Y, window.Height);
                        }
                    }
                    break;
                }
                case InputKind.MouseLeave:
                    mouseInWindow = false;
                    if (s.Hovered >= 0) s.Hovered = -1;
                    break;
                case InputKind.MouseDown:
                    mouse = new Vector2(e.X, e.Y);
                    if (io.WantCaptureMouse) break;
                    if (dragButton == null)
                    {
                        dragButton = e.Button;
                        downPos = mouse;
                        downTime = Time;
                        dragMoved = false;
                        Camera.BeginDrag();
                    }
                    break;
                case InputKind.MouseUp:
                    mouse = new Vector2(e.X, e.Y);
                    if (dragButton == e.Button)
                    {
                        Camera.EndDrag();
                        if (!dragMoved && e.Button == MouseButton.Left && !io.WantCaptureMouse)
                        {
                            bool dbl = Time - lastClickTime < 0.34f && (mouse - lastClickPos).Length() < 12;
                            lastClickTime = dbl ? -10 : Time;
                            lastClickPos = mouse;
                            Renderer!.RequestPick(dbl ? TokenDig : TokenClick, (int)mouse.X, (int)mouse.Y);
                        }
                        dragButton = null;
                    }
                    break;
                case InputKind.Wheel:
                    if (!io.WantCaptureMouse && !e.Horizontal) Camera.Zoom(e.Delta, HoverWorld);
                    break;
                case InputKind.KeyDown:
                    if (!io.WantCaptureKeyboard) HandleKey(e.Key, io);
                    break;
            }
        }
    }

    private void HandleKey(int vk, ImGuiIOPtr io)
    {
        var s = State!;
        bool ctrl = io.KeyCtrl, shift = io.KeyShift;
        if (ctrl && vk == 'Z') { if (shift) s.RestoreDigs(); else s.UndoDig(); return; }
        if (ctrl && vk == 'K') { ui?.FocusSearch(); return; }
        if (ctrl) return;
        switch (vk)
        {
            case 0x1B: // Esc
                if (s.Quiz != null) s.Quiz = null;
                else if (s.Isolated != null) s.Isolate(null);
                else s.Select(-1);
                s.Changed();
                break;
            case 'H': s.HideSelected(); break;
            case 'I':
                if (s.Isolated != null) s.Isolate(null);
                else if (s.Selected >= 0) s.Isolate([s.Selected]);
                break;
            case 'F': if (s.Selected >= 0) s.RequestCamera(CameraPreset.Focus, s.Selected); break;
            case 'X': s.SetViewMode(s.ViewMode == ViewMode.XRay ? ViewMode.Solid : ViewMode.XRay); break;
            case 'W': s.SetViewMode(s.ViewMode == ViewMode.Wire ? ViewMode.Solid : ViewMode.Wire); break;
            case 0xDD: s.Peel(); break;   // ]
            case 0xDB: s.Unpeel(); break; // [
            case 'U': s.UnhideAll(); break;
            case 0x24: s.RequestCamera(CameraPreset.Home); break; // Home
            case 0x7A: window.ToggleFullscreen(); break; // F11
            case 0x7B: TakeScreenshot(); break; // F12
            case '1': s.RequestCamera(CameraPreset.Front); break;
            case '2': s.RequestCamera(CameraPreset.Back); break;
            case '3': s.RequestCamera(CameraPreset.Left); break;
            case '4': s.RequestCamera(CameraPreset.Right); break;
            case '5': s.RequestCamera(CameraPreset.Top); break;
            case '6': s.RequestCamera(CameraPreset.Iso); break;
        }
    }

    // ------------------------------------------------------------------------------------------------ seçim sonuçları
    private void ProcessPicks()
    {
        var s = State!;
        foreach (var r in Renderer!.DrainPickResults())
        {
            int part = r.Part;
            if (part >= 0 && Model!.Parts[part].IsAttachment) part = -1;
            switch (r.Token)
            {
                case TokenHover:
                    HoverWorld = part >= 0 ? r.World : null;
                    if (s.Hovered != part) { s.Hovered = part; redrawFrames = 2; }
                    break;
                case TokenClick:
                    if (s.Quiz != null) { ui?.QuizPick(part); break; }
                    s.Select(part >= 0 && part != s.Selected ? part : -1);
                    break;
                case TokenDig:
                    if (s.Quiz != null || part < 0) break;
                    var p = Model!.Parts[part];
                    var ids = Model.WithChildren(p).Select(x => x.Index).ToArray();
                    if (s.Dig(part, ids, r.World)) ui?.Ripple(r.World, p.Radius);
                    s.Select(-1);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ kamera
    public void ApplyCameraRequest()
    {
        var s = State!;
        if (s.CameraRequest is not { } req) return;
        s.CameraRequest = null;
        var home = new Vector3(0, 0.92f, 0);
        float d = MathF.Max(Camera.Distance, 1.2f);
        var look = Camera.Target;
        switch (req.preset)
        {
            case CameraPreset.Home: Camera.FlyTo(home, 4.1f, 0, 0.02f, req.duration); break;
            case CameraPreset.Front: Camera.FlyTo(look, d, 0, 0, req.duration); break;
            case CameraPreset.Back: Camera.FlyTo(look, d, MathF.PI, 0, req.duration); break;
            case CameraPreset.Left: Camera.FlyTo(look, d, MathF.PI / 2, 0, req.duration); break;
            case CameraPreset.Right: Camera.FlyTo(look, d, -MathF.PI / 2, 0, req.duration); break;
            case CameraPreset.Top: Camera.FlyTo(look, d, Camera.Yaw, 1.5f, req.duration); break;
            case CameraPreset.Bottom: Camera.FlyTo(look, d, Camera.Yaw, -1.5f, req.duration); break;
            case CameraPreset.Iso: Camera.FlyTo(look, d, 0.65f, 0.35f, req.duration); break;
            case CameraPreset.Focus:
            {
                Vector3 c;
                float r;
                if (req.part >= 0)
                {
                    var p = Model!.Parts[req.part];
                    c = p.Center;
                    r = p.Radius;
                }
                else
                {
                    var set = s.Isolated ?? [];
                    if (set.Count == 0) return;
                    var mn = new Vector3(float.MaxValue);
                    var mx = new Vector3(float.MinValue);
                    foreach (var i in set) { mn = Vector3.Min(mn, Model!.Parts[i].Min); mx = Vector3.Max(mx, Model.Parts[i].Max); }
                    c = (mn + mx) * 0.5f;
                    r = (mx - mn).Length() * 0.5f;
                }
                float aspect = window.Width / (float)Math.Max(1, window.Height);
                float fov = Camera.FovY;
                float fit = r / MathF.Sin(MathF.Min(fov, fov * aspect) / 2);
                Camera.FlyTo(c, Math.Clamp(fit * 1.35f, 0.12f, 4.5f), null, null, req.duration);
                break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ ekran görüntüsü
    public void TakeScreenshot(string? path = null)
    {
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Anatomi 3D",
            $"anatomi3d_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        pendingShot = path;
        ui?.Toast(State?.Lang == Lang.En ? $"Screenshot saved: {Path.GetFileName(path)}" : $"Ekran görüntüsü kaydedildi: {Path.GetFileName(path)}");
    }

    private unsafe void SaveBackBuffer(string path)
    {
        var src = swap.BackBuffer!;
        var d = src.Description;
        d.Usage = ResourceUsage.Staging;
        d.BindFlags = BindFlags.None;
        d.CPUAccessFlags = CpuAccessFlags.Read;
        d.MiscFlags = ResourceOptionFlags.None;
        using var staging = gd.Device.CreateTexture2D(ref d);
        gd.Context.CopyResource(staging, src);
        var m = gd.Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var span = new ReadOnlySpan<byte>((void*)m.DataPointer, (int)(m.RowPitch * d.Height));
            PngWriter.Write(path, (int)d.Width, (int)d.Height, span, (int)m.RowPitch);
        }
        finally { gd.Context.Unmap(staging, 0); }
    }

    // ------------------------------------------------------------------------------------------------ betik (otomatik test / tanıtım)
    private void RunScript()
    {
        if (scriptWait > 0) { scriptWait--; return; }
        while (script.Count > 0 && scriptWait == 0 && pendingShot == null)
        {
            var cmd = script.Dequeue().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var s = State!;
            string arg(int i) => cmd.Length > i ? cmd[i] : "";
            float num(int i, float def = 0) => cmd.Length > i && float.TryParse(cmd[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
            int part(string id) => Model!.ById.TryGetValue(id, out var p) ? p.Index : -1;
            switch (cmd[0])
            {
                case "wait": scriptWait = (int)num(1, 30); break;
                case "shot": pendingShot = arg(1); break;
                case "quit": quitRequested = true; break;
                case "size":
                    Win32Size((int)num(1, 1920), (int)num(2, 1080));
                    scriptWait = 5;
                    break;
                case "cam":
                    s.RequestCamera(Enum.TryParse<CameraPreset>(arg(1), true, out var cp) ? cp : CameraPreset.Home, -1, 0.01f);
                    break;
                case "orbit": Camera.Yaw += num(1) * MathF.PI / 180; Camera.Pitch += num(2) * MathF.PI / 180; Camera.Snap(); break;
                case "dist": Camera.Distance = num(1, 4.1f); Camera.Snap(); break;
                case "target": Camera.Target = new Vector3(num(1), num(2), num(3)); Camera.Snap(); break;
                case "select": s.Select(part(arg(1)), cmd.Length > 2 && arg(2) == "focus"); break;
                case "focus": s.RequestCamera(CameraPreset.Focus, part(arg(1)), 0.01f); break;
                case "isolate": s.Isolate(cmd.Skip(1).Select(part).Where(i => i >= 0).ToArray()); break;
                case "peel": s.SetPeel((int)num(1)); break;
                case "xray": s.SetViewMode(arg(1) == "off" ? ViewMode.Solid : ViewMode.XRay); break;
                case "wire": s.SetViewMode(arg(1) == "off" ? ViewMode.Solid : ViewMode.Wire); break;
                case "color": s.SetColorMode(arg(1) == "function" ? ColorMode.Function : ColorMode.Anatomic); break;
                case "clip":
                    s.SetClip(Enum.TryParse<ClipAxis>(arg(1), true, out var ca) ? ca : ClipAxis.None);
                    s.SetClipPos(num(2));
                    break;
                case "hide": foreach (var id in cmd.Skip(1)) s.SetCategories([Categories.FromKey(id).Id], true); break;
                case "show": foreach (var id in cmd.Skip(1)) s.SetCategories([Categories.FromKey(id).Id], false); break;
                case "dig":
                {
                    int i = part(arg(1));
                    if (i >= 0) s.Dig(i, Model!.WithChildren(Model.Parts[i]).Select(x => x.Index).ToArray(), Model.Parts[i].Center);
                    break;
                }
                case "ui": UiVisible = arg(1) != "off"; break;
                case "fullscreen": if (!window.Fullscreen) window.ToggleFullscreen(); scriptWait = 5; break;
                case "region": ui?.ApplyRegion((int)num(1)); break;
                case "lang": s.Lang = arg(1) == "en" ? Lang.En : Lang.Tr; s.Changed(); break;
                case "hover": mouse = new Vector2(num(1), num(2)); hoverDirty = true; mouseInWindow = true; break;
                case "click": Renderer!.RequestPick(TokenClick, (int)num(1), (int)num(2)); scriptWait = 3; break;
                case "search": ui?.SetSearch(string.Join(' ', cmd.Skip(1))); break;
                case "panel": ui?.ScriptPanel(arg(1), arg(2)); break;
                case "quiz": ui?.StartQuiz(); break;
                case "quality": Settings.ApplyPreset(Enum.TryParse<QualityPreset>(arg(1), true, out var qp) ? qp : QualityPreset.High, gd); Renderer!.ApplySettings(Settings.ToRenderSettings(gd)); break;
                case "stats": File.AppendAllText(arg(1).Length > 0 ? arg(1) : "stats.txt",
                    $"{DateTime.Now:HH:mm:ss} fps={Fps:F1} cpu_ms={FrameMs:F2} gpu_ms={Renderer!.Profiler.TotalMs:F2} tris={Renderer.Stats.Triangles} draws={Renderer.Stats.DrawCalls} parts={Renderer.Stats.VisibleParts} msaa={Renderer.Stats.Samples} | " +
                    string.Join(", ", Renderer.Profiler.Last.Select(x => $"{x.name}={x.ms:F2}")) + "\n");
                    break;
            }
        }
    }

    private void Win32Size(int w, int h)
    {
        var r = new Win32.RECT { Right = w, Bottom = h };
        unsafe { Win32.AdjustWindowRectExForDpi(&r, Win32.WS_OVERLAPPEDWINDOW, false, 0, (uint)(window.DpiScale * 96)); }
        Win32.SetWindowPos(window.Handle, IntPtr.Zero, 0, 0, r.Right - r.Left, r.Bottom - r.Top, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    public void RequestRedraw(int frames = 2) => redrawFrames = Math.Max(redrawFrames, frames);

    public void ApplyRenderSettings()
    {
        Renderer?.ApplySettings(Settings.ToRenderSettings(gd));
        Settings.Save();
        RequestRedraw();
    }

    public void Dispose()
    {
        Settings.Save();
        ui?.Dispose();
        Renderer?.Dispose();
        imgui.Dispose();
        swap.Dispose();
        gd.Dispose();
        window.Dispose();
    }
}
