namespace PrimaryDisplaySwap.Models;

/// <summary>A selectable display mode (resolution + refresh rate) for a monitor.</summary>
public sealed class DisplayMode : IEquatable<DisplayMode>
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int RefreshRateHz { get; init; }
    public int BitsPerPixel { get; init; }

    public string ResolutionLabel => $"{Width} × {Height}";

    public string Label =>
        RefreshRateHz > 0 ? $"{Width} × {Height} · {RefreshRateHz} Hz" : $"{Width} × {Height}";

    public bool Equals(DisplayMode? other) =>
        other is not null &&
        Width == other.Width &&
        Height == other.Height &&
        RefreshRateHz == other.RefreshRateHz;

    public override bool Equals(object? obj) => Equals(obj as DisplayMode);

    public override int GetHashCode() => HashCode.Combine(Width, Height, RefreshRateHz);

    public override string ToString() => Label;
}
