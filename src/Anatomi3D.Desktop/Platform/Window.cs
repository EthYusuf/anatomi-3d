using System.Runtime.InteropServices;
using static Anatomi3D.Desktop.Platform.Win32;

namespace Anatomi3D.Desktop.Platform;

public enum MouseButton { Left = 0, Right = 1, Middle = 2, X1 = 3, X2 = 4 }

public enum InputKind { MouseMove, MouseDown, MouseUp, Wheel, KeyDown, KeyUp, Char, Focus, MouseLeave }

public readonly record struct InputEvent(InputKind Kind, int X = 0, int Y = 0, MouseButton Button = MouseButton.Left,
    float Delta = 0, bool Horizontal = false, int Key = 0, char Char = '\0', bool Flag = false);

/// <summary>
/// Yerel Win32 penceresi: monitör başına DPI, koyu başlık çubuğu, çift tıklama, yeniden boyutlandırma sırasında
/// kesintisiz çizim, kenarlıksız tam ekran. Giriş olayları kuyruğa yazılır, uygulama her karede işler.
/// </summary>
public sealed unsafe class Window : IDisposable
{
    public IntPtr Handle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float DpiScale { get; private set; } = 1f;
    public bool Minimized { get; private set; }
    public bool Focused { get; private set; } = true;
    public bool Closing { get; private set; }
    public bool Fullscreen { get; private set; }
    public bool InSizeMove { get; private set; }

    public readonly List<InputEvent> Events = new(64);
    public event Action<int, int>? Resized;
    public event Action<float>? DpiChanged;
    /// <summary>Boyutlandırma/taşıma döngüsünde (modal) kare çizdirmek için</summary>
    public Action? ModalFrame;
    /// <summary>İmleç şekli (ImGui'den); IntPtr.Zero = gizli</summary>
    public IntPtr Cursor { get; set; }

    private readonly WndProc proc;
    private bool tracking;
    private WINDOWPLACEMENT savedPlacement;
    private uint savedStyle;
    private static readonly IntPtr TimerId = new(1);

    public Window(string title, int width, int height)
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        proc = WndProcImpl;
        var hInst = GetModuleHandleW(null);
        fixed (char* cls = "Anatomi3DWindow")
        {
            IntPtr icon;
            string exe = Environment.ProcessPath ?? "";
            fixed (char* pe = exe) icon = ExtractIconW(hInst, pe, 0);
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS | CS_OWNDC,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                hInstance = hInst,
                hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW),
                hIcon = icon,
                hIconSm = icon,
                lpszClassName = cls,
            };
            RegisterClassExW(&wc);
            fixed (char* t = title)
                Handle = CreateWindowExW(0, cls, t, WS_OVERLAPPEDWINDOW, unchecked((int)0x80000000), unchecked((int)0x80000000),
                    width, height, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        }
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("Pencere oluşturulamadı: " + Marshal.GetLastWin32Error());

        // koyu başlık çubuğu ve yuvarlak köşeler (Windows 10 20H1+ / 11)
        int dark = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        int round = 2;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, &round, sizeof(int));
        uint caption = 0x00201714; // BGR: #141720
        DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, &caption, sizeof(uint));

        DpiScale = GetDpiForWindow(Handle) / 96f;
        // istenen istemci alanını DPI'ya göre ölçekle ve ekranı aşmayacak şekilde ortala
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfoW(MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST), &mi);
        int workW = mi.rcWork.Right - mi.rcWork.Left, workH = mi.rcWork.Bottom - mi.rcWork.Top;
        var r = new RECT { Right = (int)(width * DpiScale), Bottom = (int)(height * DpiScale) };
        AdjustWindowRectExForDpi(&r, WS_OVERLAPPEDWINDOW, false, 0, (uint)(DpiScale * 96));
        int ww = Math.Min(r.Right - r.Left, workW), wh = Math.Min(r.Bottom - r.Top, workH);
        SetWindowPos(Handle, IntPtr.Zero, mi.rcWork.Left + (workW - ww) / 2, mi.rcWork.Top + (workH - wh) / 2, ww, wh, SWP_NOZORDER | SWP_NOACTIVATE);
        UpdateClientSize();
        timeBeginPeriod(1);
    }

    public void Show(bool maximized)
    {
        ShowWindow(Handle, maximized ? SW_SHOWMAXIMIZED : SW_SHOW);
        UpdateWindow(Handle);
        UpdateClientSize();
    }

    public void SetTitle(string t) => SetWindowTextW(Handle, t);

    private void UpdateClientSize()
    {
        RECT rc;
        GetClientRect(Handle, &rc);
        Width = Math.Max(1, rc.Right - rc.Left);
        Height = Math.Max(1, rc.Bottom - rc.Top);
    }

    /// <summary>Bekleyen tüm pencere iletilerini işler. false: uygulama kapanıyor.</summary>
    public bool PumpMessages()
    {
        MSG msg;
        while (PeekMessageW(&msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            if (msg.message == WM_QUIT) { Closing = true; return false; }
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        return !Closing;
    }

    /// <summary>Boştayken ileti gelene ya da süre dolana kadar bekler (işlemci ve GPU'yu yormaz).</summary>
    public void WaitForInput(int ms) => MsgWaitForMultipleObjectsEx(0, null, (uint)ms, QS_ALLINPUT, MWMO_INPUTAVAILABLE);

    public void ToggleFullscreen()
    {
        if (!Fullscreen)
        {
            savedPlacement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
            fixed (WINDOWPLACEMENT* wp = &savedPlacement) GetWindowPlacement(Handle, wp);
            savedStyle = (uint)GetWindowLongPtrW(Handle, GWL_STYLE);
            var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            GetMonitorInfoW(MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST), &mi);
            SetWindowLongPtrW(Handle, GWL_STYLE, (IntPtr)((savedStyle & ~WS_OVERLAPPEDWINDOW) | WS_POPUP | WS_VISIBLE));
            SetWindowPos(Handle, IntPtr.Zero, mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right - mi.rcMonitor.Left,
                mi.rcMonitor.Bottom - mi.rcMonitor.Top, SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
            Fullscreen = true;
        }
        else
        {
            SetWindowLongPtrW(Handle, GWL_STYLE, (IntPtr)savedStyle);
            fixed (WINDOWPLACEMENT* wp = &savedPlacement) SetWindowPlacement(Handle, wp);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
            Fullscreen = false;
        }
    }

    private IntPtr WndProcImpl(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp)
    {
        switch (msg)
        {
            case WM_CLOSE:
                Closing = true;
                PostQuitMessage(0);
                return IntPtr.Zero;
            case WM_DESTROY:
                return IntPtr.Zero;
            case WM_ERASEBKGND:
                return 1;
            case WM_SIZE:
            {
                Minimized = (int)wp == SIZE_MINIMIZED;
                if (!Minimized)
                {
                    int w = LoWord(lp) & 0xFFFF, h = HiWord(lp) & 0xFFFF;
                    if (w > 0 && h > 0 && (w != Width || h != Height))
                    {
                        Width = w;
                        Height = h;
                        Resized?.Invoke(w, h);
                        if (InSizeMove) ModalFrame?.Invoke();
                    }
                }
                return IntPtr.Zero;
            }
            case WM_ENTERSIZEMOVE:
                InSizeMove = true;
                SetTimer(hwnd, TimerId, 8, IntPtr.Zero);
                return IntPtr.Zero;
            case WM_EXITSIZEMOVE:
                InSizeMove = false;
                KillTimer(hwnd, TimerId);
                return IntPtr.Zero;
            case WM_TIMER:
                if (InSizeMove) ModalFrame?.Invoke();
                return IntPtr.Zero;
            case WM_PAINT:
                ValidateRect(hwnd, null);
                if (InSizeMove) ModalFrame?.Invoke();
                return IntPtr.Zero;
            case WM_DPICHANGED:
            {
                DpiScale = HiWord(wp) / 96f;
                var r = (RECT*)lp;
                SetWindowPos(hwnd, IntPtr.Zero, r->Left, r->Top, r->Right - r->Left, r->Bottom - r->Top, SWP_NOZORDER | SWP_NOACTIVATE);
                DpiChanged?.Invoke(DpiScale);
                return IntPtr.Zero;
            }
            case WM_GETMINMAXINFO:
            {
                var mmi = (MINMAXINFO*)lp;
                mmi->ptMinTrackSize.X = (int)(960 * DpiScale);
                mmi->ptMinTrackSize.Y = (int)(600 * DpiScale);
                return IntPtr.Zero;
            }
            case WM_SETFOCUS:
                Focused = true;
                Events.Add(new InputEvent(InputKind.Focus, Flag: true));
                return IntPtr.Zero;
            case WM_KILLFOCUS:
                Focused = false;
                Events.Add(new InputEvent(InputKind.Focus, Flag: false));
                return IntPtr.Zero;
            case WM_MOUSEMOVE:
                if (!tracking)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TME_LEAVE, hwndTrack = hwnd };
                    TrackMouseEvent(&tme);
                    tracking = true;
                }
                Events.Add(new InputEvent(InputKind.MouseMove, LoWord(lp), HiWord(lp)));
                return IntPtr.Zero;
            case WM_MOUSELEAVE:
                tracking = false;
                Events.Add(new InputEvent(InputKind.MouseLeave));
                return IntPtr.Zero;
            case WM_LBUTTONDOWN or WM_LBUTTONDBLCLK or WM_RBUTTONDOWN or WM_RBUTTONDBLCLK or WM_MBUTTONDOWN or WM_MBUTTONDBLCLK or WM_XBUTTONDOWN:
            {
                var b = msg switch
                {
                    WM_LBUTTONDOWN or WM_LBUTTONDBLCLK => MouseButton.Left,
                    WM_RBUTTONDOWN or WM_RBUTTONDBLCLK => MouseButton.Right,
                    WM_MBUTTONDOWN or WM_MBUTTONDBLCLK => MouseButton.Middle,
                    _ => HiWord(wp) == 1 ? MouseButton.X1 : MouseButton.X2,
                };
                if (GetCapture() == IntPtr.Zero) SetCapture(hwnd);
                bool dbl = msg is WM_LBUTTONDBLCLK or WM_RBUTTONDBLCLK or WM_MBUTTONDBLCLK;
                Events.Add(new InputEvent(InputKind.MouseDown, LoWord(lp), HiWord(lp), b, Flag: dbl));
                return IntPtr.Zero;
            }
            case WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP or WM_XBUTTONUP:
            {
                var b = msg switch
                {
                    WM_LBUTTONUP => MouseButton.Left,
                    WM_RBUTTONUP => MouseButton.Right,
                    WM_MBUTTONUP => MouseButton.Middle,
                    _ => HiWord(wp) == 1 ? MouseButton.X1 : MouseButton.X2,
                };
                if ((GetKeyState(0x01) & 0x8000) == 0 && (GetKeyState(0x02) & 0x8000) == 0 && (GetKeyState(0x04) & 0x8000) == 0) ReleaseCapture();
                Events.Add(new InputEvent(InputKind.MouseUp, LoWord(lp), HiWord(lp), b));
                return IntPtr.Zero;
            }
            case WM_MOUSEWHEEL:
                Events.Add(new InputEvent(InputKind.Wheel, Delta: HiWord(wp) / 120f));
                return IntPtr.Zero;
            case WM_MOUSEHWHEEL:
                Events.Add(new InputEvent(InputKind.Wheel, Delta: -HiWord(wp) / 120f, Horizontal: true));
                return IntPtr.Zero;
            case WM_KEYDOWN or WM_SYSKEYDOWN:
                Events.Add(new InputEvent(InputKind.KeyDown, Key: (int)wp));
                if (msg == WM_SYSKEYDOWN && (int)wp != 0x73) return IntPtr.Zero; // Alt+F4 dışındaki sistem tuşlarını yut
                break;
            case WM_KEYUP or WM_SYSKEYUP:
                Events.Add(new InputEvent(InputKind.KeyUp, Key: (int)wp));
                break;
            case WM_CHAR:
                if ((int)wp > 0 && (int)wp < 0x10000) Events.Add(new InputEvent(InputKind.Char, Char: (char)(int)wp));
                return IntPtr.Zero;
            case WM_SETCURSOR:
                if (LoWord(lp) == HTCLIENT)
                {
                    SetCursor(Cursor);
                    return 1;
                }
                break;
        }
        return DefWindowProcW(hwnd, msg, wp, lp);
    }

    public void Dispose()
    {
        timeEndPeriod(1);
        if (Handle != IntPtr.Zero) DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }
}
