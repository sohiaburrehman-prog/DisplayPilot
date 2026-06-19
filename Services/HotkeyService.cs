using System.Runtime.InteropServices;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Registers the application's global hotkeys against a window handle and maps
/// incoming WM_HOTKEY messages back to a logical action. Re-registration is
/// safe to call repeatedly (e.g. after the user rebinds a key).
/// </summary>
public sealed class HotkeyService
{
    public const int OpenPanelHotkeyId = 9001;
    public const int CyclePrimaryHotkeyId = 9002;

    public const int WmHotKey = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private bool _openPanelRegistered;
    private bool _cycleRegistered;

    public void Initialize(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>
    /// Re-registers both hotkeys from the current settings. Returns a result
    /// describing which bindings succeeded so the caller can surface failures.
    /// </summary>
    public HotkeyApplyResult Apply(AppSettings settings)
    {
        var result = new HotkeyApplyResult();

        if (_hwnd == IntPtr.Zero)
        {
            return result;
        }

        if (_openPanelRegistered)
        {
            UnregisterHotKey(_hwnd, OpenPanelHotkeyId);
            _openPanelRegistered = false;
        }

        if (_cycleRegistered)
        {
            UnregisterHotKey(_hwnd, CyclePrimaryHotkeyId);
            _cycleRegistered = false;
        }

        if (settings.OpenPanelHotkey.IsBound)
        {
            _openPanelRegistered = RegisterHotKey(
                _hwnd, OpenPanelHotkeyId, settings.OpenPanelHotkey.Modifiers, settings.OpenPanelHotkey.Key);
            result.OpenPanelRegistered = _openPanelRegistered;
            result.OpenPanelRequested = true;
            AppLogger.Log($"Hotkey (open panel) register: {_openPanelRegistered} [{Describe(settings.OpenPanelHotkey)}]");
        }

        if (settings.CyclePrimaryHotkey.IsBound)
        {
            _cycleRegistered = RegisterHotKey(
                _hwnd, CyclePrimaryHotkeyId, settings.CyclePrimaryHotkey.Modifiers, settings.CyclePrimaryHotkey.Key);
            result.CyclePrimaryRegistered = _cycleRegistered;
            result.CyclePrimaryRequested = true;
            AppLogger.Log($"Hotkey (cycle primary) register: {_cycleRegistered} [{Describe(settings.CyclePrimaryHotkey)}]");
        }

        return result;
    }

    public void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_openPanelRegistered)
        {
            UnregisterHotKey(_hwnd, OpenPanelHotkeyId);
            _openPanelRegistered = false;
        }

        if (_cycleRegistered)
        {
            UnregisterHotKey(_hwnd, CyclePrimaryHotkeyId);
            _cycleRegistered = false;
        }
    }

    /// <summary>Human-readable shortcut text, e.g. "Ctrl + Shift + M".</summary>
    public static string Describe(HotkeyConfig hotkey)
    {
        if (hotkey is null || hotkey.Key == 0)
        {
            return "None";
        }

        var parts = new List<string>();
        if ((hotkey.Modifiers & AppSettings.ModControl) != 0) parts.Add("Ctrl");
        if ((hotkey.Modifiers & AppSettings.ModAlt) != 0) parts.Add("Alt");
        if ((hotkey.Modifiers & AppSettings.ModShift) != 0) parts.Add("Shift");
        if ((hotkey.Modifiers & AppSettings.ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(hotkey.Key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(uint vk)
    {
        return vk switch
        {
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),                 // A–Z
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),                 // 0–9
            >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),                     // F1–F12
            0x20 => "Space",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBA => ";",
            0xDE => "'",
            0xBB => "=",
            0xBD => "-",
            _ => $"VK_{vk:X2}",
        };
    }
}

public sealed class HotkeyApplyResult
{
    public bool OpenPanelRequested { get; set; }
    public bool OpenPanelRegistered { get; set; }
    public bool CyclePrimaryRequested { get; set; }
    public bool CyclePrimaryRegistered { get; set; }

    public bool HasFailure =>
        (OpenPanelRequested && !OpenPanelRegistered) ||
        (CyclePrimaryRequested && !CyclePrimaryRegistered);

    public string FailureMessage
    {
        get
        {
            var failed = new List<string>();
            if (OpenPanelRequested && !OpenPanelRegistered) failed.Add("open panel");
            if (CyclePrimaryRequested && !CyclePrimaryRegistered) failed.Add("cycle primary");
            return failed.Count == 0
                ? string.Empty
                : $"Could not register the {string.Join(" and ", failed)} hotkey — it may be in use by another app.";
        }
    }
}
