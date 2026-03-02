namespace GenesysExtensionAudit.Domain.Services;

/// <summary>
/// Controls how extension strings are normalized for comparison and reporting.
/// </summary>
public sealed class ExtensionNormalizationOptions
{
    public static readonly ExtensionNormalizationOptions Default = new();

    /// <summary>Strip all non-digit characters (default: true).</summary>
    public bool DigitsOnly { get; init; } = true;

    /// <summary>Trim leading/trailing whitespace before processing (default: true).</summary>
    public bool Trim { get; init; } = true;

    /// <summary>Minimum digit length for a valid extension (default: 1).</summary>
    public int MinLength { get; init; } = 1;

    /// <summary>Maximum digit length for a valid extension (default: 20).</summary>
    public int MaxLength { get; init; } = 20;
}

/// <summary>
/// Outcome of normalizing a single extension string.
/// </summary>
public enum ExtensionNormalizationStatus
{
    Ok,
    Empty,
    WhitespaceOnly,
    NonDigitOnly,
    TooShort,
    TooLong,
}

/// <summary>
/// Result of a single extension normalization attempt.
/// </summary>
public sealed class ExtensionNormalizationResult
{
    private ExtensionNormalizationResult() { }

    public bool IsOk => Status == ExtensionNormalizationStatus.Ok;
    public ExtensionNormalizationStatus Status { get; private init; }
    public string? Normalized { get; private init; }
    public string Notes { get; private init; } = string.Empty;

    public static ExtensionNormalizationResult Success(string normalized)
        => new() { Status = ExtensionNormalizationStatus.Ok, Normalized = normalized };

    public static ExtensionNormalizationResult Fail(ExtensionNormalizationStatus status, string notes)
        => new() { Status = status, Notes = notes };
}

/// <summary>
/// Stateless normalization pipeline for extension strings.
/// Used by AuditEngine to produce stable join keys from raw API values.
/// </summary>
public static class ExtensionNormalization
{
    public static ExtensionNormalizationResult Normalize(
        string? raw,
        ExtensionNormalizationOptions? options = null)
    {
        options ??= ExtensionNormalizationOptions.Default;

        if (raw is null)
            return ExtensionNormalizationResult.Fail(ExtensionNormalizationStatus.Empty, "Value is null.");

        var value = options.Trim ? raw.Trim() : raw;

        if (value.Length == 0)
            return ExtensionNormalizationResult.Fail(ExtensionNormalizationStatus.Empty, "Value is empty after trim.");

        if (string.IsNullOrWhiteSpace(value))
            return ExtensionNormalizationResult.Fail(ExtensionNormalizationStatus.WhitespaceOnly, "Value is whitespace only.");

        string normalized;
        if (options.DigitsOnly)
        {
            normalized = new string(value.Where(char.IsDigit).ToArray());

            if (normalized.Length == 0)
                return ExtensionNormalizationResult.Fail(
                    ExtensionNormalizationStatus.NonDigitOnly,
                    $"Value '{value}' contains no digits.");
        }
        else
        {
            normalized = value;
        }

        if (normalized.Length < options.MinLength)
            return ExtensionNormalizationResult.Fail(
                ExtensionNormalizationStatus.TooShort,
                $"Normalized value '{normalized}' is shorter than MinLength={options.MinLength}.");

        if (normalized.Length > options.MaxLength)
            return ExtensionNormalizationResult.Fail(
                ExtensionNormalizationStatus.TooLong,
                $"Normalized value truncated at MaxLength={options.MaxLength}.");

        return ExtensionNormalizationResult.Success(normalized);
    }
}
