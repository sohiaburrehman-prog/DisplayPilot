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

    public override string ToString() => DisplayLabel;
}
