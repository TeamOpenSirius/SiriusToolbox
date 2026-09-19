using System.Windows.Forms;

namespace Sirius.ToolboxUI;

internal sealed class ToolboxWindowRegistry(Form owner)
{
    private readonly Dictionary<string, Form> _windows = new(StringComparer.Ordinal);

    public T OpenOrActivate<T>(string key, Func<T> factory, Action? onClosed = null) where T : Form
    {
        if (_windows.TryGetValue(key, out var existing) && !existing.IsDisposed)
        {
            if (existing.WindowState == FormWindowState.Minimized)
                existing.WindowState = FormWindowState.Normal;
            existing.BringToFront();
            existing.Activate();
            return (T)existing;
        }

        var window = factory();
        _windows[key] = window;
        window.FormClosed += (_, _) =>
        {
            _windows.Remove(key);
            onClosed?.Invoke();
        };
        window.Show(owner);
        return window;
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values.ToArray())
        {
            if (!window.IsDisposed)
                window.Close();
        }
        _windows.Clear();
    }
}
