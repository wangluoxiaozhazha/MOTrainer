using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MOTrainer.WinForms;

internal sealed class GlobalHotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private readonly IntPtr windowHandle;
    private readonly Dictionary<int, Action> handlers = [];
    private int nextId = 0x5000;
    private bool disposed;

    public GlobalHotkeyManager(IntPtr windowHandle)
    {
        this.windowHandle = windowHandle;
    }

    public bool Register(
        HotkeyGesture gesture,
        Action handler,
        out string error)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(GlobalHotkeyManager));
        }

        int id = nextId++;
        if (!RegisterHotKey(
                windowHandle,
                id,
                (uint)gesture.Modifiers | ModNoRepeat,
                (uint)(gesture.Key & Keys.KeyCode)))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        handlers.Add(id, handler);
        error = string.Empty;
        return true;
    }

    public bool ProcessMessage(ref Message message)
    {
        if (message.Msg != WmHotkey)
        {
            return false;
        }

        int id = message.WParam.ToInt32();
        if (!handlers.TryGetValue(id, out Action? handler))
        {
            return false;
        }

        handler();
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (int id in handlers.Keys)
        {
            UnregisterHotKey(windowHandle, id);
        }

        handlers.Clear();
        disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(
        IntPtr hWnd,
        int id);
}
