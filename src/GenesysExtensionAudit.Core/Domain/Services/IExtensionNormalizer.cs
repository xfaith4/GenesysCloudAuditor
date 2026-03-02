namespace GenesysExtensionAudit.Domain.Services;

/// <summary>
/// Normalizes a raw extension string to a canonical join key.
/// Returns null when the value is blank or cannot be normalized.
/// </summary>
public interface IExtensionNormalizer
{
    string? Normalize(string? raw);
}
