namespace GenesysExtensionAudit.Application;

/// <summary>
/// Progress snapshot reported during an audit run via IProgress&lt;AuditProgress&gt;.
/// All properties are optional; callers should null-check before reading.
/// </summary>
public sealed class AuditProgress
{
    /// <summary>0–100, or negative if indeterminate.</summary>
    public int Percent { get; init; }

    /// <summary>Short detail line shown below the progress bar (e.g. "Fetched page 4/20").</summary>
    public string? Message { get; init; }

    /// <summary>High-level status shown in the shell header (e.g. "Running audit...").</summary>
    public string? Status { get; init; }
}
