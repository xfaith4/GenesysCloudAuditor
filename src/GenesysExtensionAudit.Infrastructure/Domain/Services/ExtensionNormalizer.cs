using GenesysExtensionAudit.Domain.Services;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Default implementation: strips non-digit characters and returns null for blank/invalid values.
/// </summary>
public sealed class ExtensionNormalizer : IExtensionNormalizer
{
    public string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var digits = new string(raw.Trim().Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
