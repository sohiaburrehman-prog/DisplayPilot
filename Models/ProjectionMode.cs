namespace PrimaryDisplaySwap.Models;

/// <summary>The four Windows "Project" (Win+P) display topologies.</summary>
public enum ProjectionMode
{
    /// <summary>PC screen only — show on the internal/primary display alone.</summary>
    PcScreenOnly,

    /// <summary>Duplicate — clone the same image to every display.</summary>
    Duplicate,

    /// <summary>Extend — spread the desktop across all displays.</summary>
    Extend,

    /// <summary>Second screen only — show on the external display alone.</summary>
    SecondScreenOnly,
}

public static class ProjectionModeExtensions
{
    public static string DisplayLabel(this ProjectionMode mode) => mode switch
    {
        ProjectionMode.PcScreenOnly => "PC screen only",
        ProjectionMode.Duplicate => "Duplicate",
        ProjectionMode.Extend => "Extend",
        ProjectionMode.SecondScreenOnly => "Second screen only",
        _ => mode.ToString(),
    };
}
