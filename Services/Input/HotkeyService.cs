using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Ouro.Services.Input
{
    /// <summary>
    /// Global system-wide hotkey service powered by Win32 Low-Level Keyboard Hook (WH_KEYBOARD_LL).
    /// Bypasses Windows' debugger reservation for F12, and reliably captures hotkey triggers
    /// across all exclusive fullscreen games, borderless windows, and background states.
    /// </summary>
    public sealed class HotkeyService : IInputService
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly object _syncLock = new();
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        private Key _targetKey = Key.F12;
        private ModifierKeys _targetModifiers = ModifierKeys.None;
        private bool _isDisposed;

        public event EventHandler? OnSaveClipRequested;

        public bool IsRegistered
        {
            get
            {
                lock (_syncLock)
                {
                    return _hookId != IntPtr.Zero;
                }
            }
        }

        public HotkeyService(bool autoRegisterDefault = true)
        {
            _proc = HookCallback;
            if (autoRegisterDefault)
            {
                RegisterSaveHotkey(Key.F12, ModifierKeys.None);
            }
        }

        public void RegisterSaveHotkey(Key key = Key.F12, ModifierKeys modifiers = ModifierKeys.None)
        {
            lock (_syncLock)
            {
                ThrowIfDisposed();

                _targetKey = key;
                _targetModifiers = modifiers;

                if (_hookId == IntPtr.Zero)
                {
                    using var curProcess = Process.GetCurrentProcess();
                    using var curModule = curProcess.MainModule;
                    IntPtr moduleHandle = GetModuleHandle(curModule?.ModuleName);
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);

                    if (_hookId == IntPtr.Zero)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HotkeyService] Failed to install WH_KEYBOARD_LL hook. Error: {Marshal.GetLastWin32Error()}");
                    }
                }
            }
        }

        public void RegisterSaveHotkey(string hotkeyPattern)
        {
            var (key, modifiers) = ParseHotkeyString(hotkeyPattern);
            RegisterSaveHotkey(key, modifiers);
        }

        public static (Key key, ModifierKeys modifiers) ParseHotkeyString(string hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString))
            {
                return (Key.F12, ModifierKeys.None);
            }

            var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            ModifierKeys modifiers = ModifierKeys.None;
            Key key = Key.F12;

            foreach (var part in parts)
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Control;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Alt;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Shift;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Windows;
                else if (Enum.TryParse<Key>(part, true, out var parsedKey))
                    key = parsedKey;
            }

            return (key, modifiers);
        }

        public void UnregisterSaveHotkey()
        {
            lock (_syncLock)
            {
                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                }
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Key key = KeyInterop.KeyFromVirtualKey(vkCode);

                if (key == _targetKey)
                {
                    bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool altPressed = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    bool shiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool winPressed = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

                    ModifierKeys currentModifiers = ModifierKeys.None;
                    if (ctrlPressed) currentModifiers |= ModifierKeys.Control;
                    if (altPressed) currentModifiers |= ModifierKeys.Alt;
                    if (shiftPressed) currentModifiers |= ModifierKeys.Shift;
                    if (winPressed) currentModifiers |= ModifierKeys.Windows;

                    if (currentModifiers == _targetModifiers)
                    {
                        // Offload event execution immediately to avoid delaying OS keyboard hook chain
                        Task.Run(() => OnSaveClipRequested?.Invoke(this, EventArgs.Empty));
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(HotkeyService));
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                UnregisterSaveHotkey();
            }
        }
    }
}
