namespace PrimaryDisplaySwap.Models;

public sealed class MonitorInfo
{
    public required int Index { get; init; }
    /// <summary>GDI device name, e.g. \\.\DISPLAY1.</summary>
    public required string DeviceName { get; init; }
    /// <summary>Friendly monitor name, e.g. "DELL U3421WE".</summary>
    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int PositionX { get; init; }
    public required int PositionY { get; init; }
    public required bool IsPrimary { get; init; }
    public int RefreshRateHz { get; init; }

    public string DisplayLabel =>
        $"{Name} ({Width}x{Height}){(IsPrimary ? " — primary" : "")}";

    /// <summary>Compact label for menus: "1 · DELL U3421WE".</summary>
    public string NumberedName => $"{Index + 1} · {Name}";

    /// <summary>Resolution and refresh, e.g. "3440×1440 · 60 Hz".</summary>
    public string SpecsLabel =>
        RefreshRateHz > 0 ? $"{Width}×{Height} · {RefreshRateHz} Hz" : $"{Width}×{Height}";

    /// <summary>One-line tray menu entry with optional primary marker.</summary>
    public string TrayMenuLine =>
        IsPrimary
            ? $"✓ {NumberedName}  —  {SpecsLabel}  ·  Primary"
            : $"   {NumberedName}  —  {SpecsLabel}";

    public override string ToString() => DisplayLabel;
}
