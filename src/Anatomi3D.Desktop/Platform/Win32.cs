using System.Runtime.InteropServices;

namespace Anatomi3D.Desktop.Platform;

/// <summary>Uygulamanın kullandığı Win32 API bildirimleri.</summary>
internal static unsafe class Win32
{
    public const uint WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008,
        WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_QUIT = 0x0012, WM_ERASEBKGND = 0x0014, WM_SETCURSOR = 0x0020,
        WM_GETMINMAXINFO = 0x0024, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104,
        WM_SYSKEYUP = 0x0105, WM_TIMER = 0x0113, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
        WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_RBUTTONDBLCLK = 0x0206,
        WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MBUTTONDBLCLK = 0x0209, WM_MOUSEWHEEL = 0x020A,
        WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C, WM_MOUSEHWHEEL = 0x020E, WM_ENTERSIZEMOVE = 0x0231,
        WM_EXITSIZEMOVE = 0x0232, WM_MOUSELEAVE = 0x02A3, WM_DPICHANGED = 0x02E0, WM_INPUTLANGCHANGE = 0x0051,
        WM_DROPFILES = 0x0233;

    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000, WS_POPUP = 0x80000000;
    public const uint CS_HREDRAW = 0x0002, CS_VREDRAW = 0x0001, CS_DBLCLKS = 0x0008, CS_OWNDC = 0x0020;
    public const int SW_SHOW = 5, SW_SHOWMAXIMIZED = 3, SW_HIDE = 0;
    public const int GWL_STYLE = -16;
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_NOOWNERZORDER = 0x0200;
    public const int SIZE_MINIMIZED = 1;
    public const uint PM_REMOVE = 0x0001;
    public const uint TME_LEAVE = 0x00000002;
    public const int IDC_ARROW = 32512, IDC_IBEAM = 32513, IDC_WAIT = 32514, IDC_CROSS = 32515, IDC_SIZEALL = 32646,
        IDC_SIZENWSE = 32642, IDC_SIZENESW = 32643, IDC_SIZEWE = 32644, IDC_SIZENS = 32645, IDC_HAND = 32649, IDC_NO = 32648;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_CAPTION_COLOR = 35;
    public const uint QS_ALLINPUT = 0x04FF;
    public const int HTCLIENT = 1;
    public const uint MWMO_INPUTAVAILABLE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX, ptY;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
        public RECT rcDevice;
    }

    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(char* name);
    [DllImport("user32.dll", SetLastError = true)] public static extern ushort RegisterClassExW(WNDCLASSEXW* wc);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PeekMessageW(MSG* msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(MSG* msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(MSG* msg);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, RECT* rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, RECT* rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowTextW(IntPtr hwnd, string text);
    [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr GetCapture();
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool AdjustWindowRectExForDpi(RECT* rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(TRACKMOUSEEVENT* tme);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(IntPtr monitor, MONITORINFO* info);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hwnd, WINDOWPLACEMENT* wp);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hwnd, WINDOWPLACEMENT* wp);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr func);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hwnd, IntPtr id);
    [DllImport("user32.dll")] public static extern bool ValidateRect(IntPtr hwnd, RECT* rect);
    [DllImport("user32.dll")] public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr* handles, uint ms, uint wakeMask, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
    [DllImport("user32.dll")] public static extern IntPtr LoadImageW(IntPtr inst, char* name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern bool EmptyClipboard();
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, void* value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
    [DllImport("shell32.dll")] public static extern IntPtr ExtractIconW(IntPtr inst, char* file, uint index);

    public static int LoWord(IntPtr v) => (short)((long)v & 0xFFFF);
    public static int HiWord(IntPtr v) => (short)(((long)v >> 16) & 0xFFFF);

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
}
