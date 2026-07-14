using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TypedPond.LockScreen;

/// <summary>
/// Installs a low-level (system-wide) keyboard hook that suppresses key
/// combinations a user could otherwise use to escape the lock screen:
/// Alt+Tab, Alt+F4, the Windows key, Ctrl+Esc, and Ctrl+Shift+Esc.
///
/// Notes / limitations:
/// - Ctrl+Alt+Del cannot be intercepted from user mode (it is a Secure
///   Attention Sequence handled by the OS), so it is intentionally not
///   handled here.
/// - The hook callback runs on the thread that installed it, so this type
///   must be created and disposed on a thread with a running message loop
///   (i.e. the WPF UI thread).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    // Virtual key codes.
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    // GetAsyncKeyState "down" mask (high-order bit).
    private const int KeyDownMask = 0x8000;

    // Flags field of the low-level keyboard event. Bit 5 (0x20) is set when
    // Alt is held (equivalent to the WM_SYSKEY* messages).
    private const uint LLKHF_ALTDOWN = 0x20;

    // Keep the delegate alive for the lifetime of the hook so the GC does
    // not collect it while native code still holds the pointer.
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    /// <summary>Installs the hook. Safe to call once; no-op if already installed.</summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        using Process current = Process.GetCurrentProcess();
        using ProcessModule module = current.MainModule
            ?? throw new InvalidOperationException("Unable to resolve the main module for the keyboard hook.");

        IntPtr moduleHandle = GetModuleHandle(module.ModuleName);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);

        if (_hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to install low-level keyboard hook. Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && ShouldSuppress(wParam, lParam))
        {
            // Returning a non-zero value without calling CallNextHookEx
            // swallows the keystroke.
            return (IntPtr)1;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool ShouldSuppress(IntPtr wParam, IntPtr lParam)
    {
        int message = wParam.ToInt32();
        bool isKeyDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
        if (!isKeyDown)
        {
            // Only decide on key-down; let key-up events flow so modifier
            // state stays consistent.
            return false;
        }

        KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int vk = (int)data.vkCode;

        bool altDown = (data.flags & LLKHF_ALTDOWN) != 0;
        bool ctrlDown = IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL);
        bool shiftDown = IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT);

        // Windows key (either side).
        if (vk == VK_LWIN || vk == VK_RWIN)
        {
            return true;
        }

        // Alt+Tab.
        if (vk == VK_TAB && altDown)
        {
            return true;
        }

        // Alt+F4.
        if (vk == VK_F4 && altDown)
        {
            return true;
        }

        // Ctrl+Esc (Start menu) and Ctrl+Shift+Esc (Task Manager). The
        // Ctrl+Shift+Esc case is covered by the Ctrl+Esc check since shift
        // being additionally held does not change our decision to suppress.
        if (vk == VK_ESCAPE && ctrlDown)
        {
            return true;
        }

        // Keep shift referenced so the intent (Task Manager combo) is clear
        // even though Ctrl+Esc already covers it.
        _ = shiftDown;

        return false;
    }

    private static bool IsKeyDown(int vk)
    {
        return (GetAsyncKeyState(vk) & KeyDownMask) != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~KeyboardHook()
    {
        Dispose();
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
