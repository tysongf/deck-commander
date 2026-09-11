using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DeckCommander
{
    /// <summary>
    /// System-wide hotkeys. Registered with Windows rather than handled as WPF key
    /// events, so they still fire while IL-2 has focus.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        const int WM_HOTKEY = 0x0312;

        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModNoRepeat = 0x4000;

        public const uint VkPageUp = 0x21;
        public const uint VkPageDown = 0x22;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        readonly IntPtr _hwnd;
        readonly HwndSource _source;
        readonly Dictionary<int, Action> _handlers = new Dictionary<int, Action>();
        int _nextId = 0xB100;

        public HotkeyManager(HwndSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
            _hwnd = source.Handle;
            _source.AddHook(Hook);
        }

        /// <summary>Registers a hotkey. Returns false if another application owns it.</summary>
        public bool Register(uint modifiers, uint virtualKey, Action handler)
        {
            int id = _nextId++;
            if (!RegisterHotKey(_hwnd, id, modifiers | ModNoRepeat, virtualKey)) return false;
            _handlers[id] = handler;
            return true;
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                Action a;
                if (_handlers.TryGetValue(wParam.ToInt32(), out a))
                {
                    handled = true;
                    try { a(); } catch { }
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            foreach (var id in _handlers.Keys) UnregisterHotKey(_hwnd, id);
            _handlers.Clear();
            if (_source != null) _source.RemoveHook(Hook);
        }
    }
}
