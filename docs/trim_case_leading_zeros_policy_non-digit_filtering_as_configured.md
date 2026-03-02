```csharp
// ExtensionNormalization.cs
using System;
using System.Text;

namespace GenesysCloudExtensionAudit
{
    /// <summary>
    /// Configures how extension strings are normalized for comparison/join keys.
    /// </summary>
    public sealed class ExtensionNormalizationOptions
    {
        /// <summary>
        /// If true, non-digit characters are removed and the result must be digits only.
        /// If false, the result may be alphanumeric (A-Z0-9) depending on AllowAlphanumeric.
        /// </summary>
        public bool DigitsOnly { get; init; } = false;

        /// <summary>
        /// If true and DigitsOnly==false, letters A-Z are allowed (after uppercasing).
        /// If false and DigitsOnly==false, only digits are allowed.
        /// </summary>
        public bool AllowAlphanumeric { get; init; } = true;

        /// <summary>
        /// If true, leading zeros are preserved (default/safest PBX semantics).
        /// If false, leading zeros are trimmed for numeric-only (DigitsOnly==true) values.
        /// </summary>
        public bool PreserveLeadingZeros { get; init; } = true;

        /// <summary>
        /// If true, normalization will attempt to remove common "ext" prefixes like:
        /// "ext", "ext.", "extension", "x" (case-insensitive) when at the start.
        /// </summary>
        public bool StripExtensionPrefixes { get; init; } = true;

        /// <summary>
        /// If true, spaces and common separators are removed before validation.
        /// In DigitsOnly mode, separators are effectively removed anyway.
        /// </summary>
        public bool RemoveCommonSeparators { get; init; } = true;

        /// <summary>
        /// If specified, normalized value shorter than MinLength is invalid.
        /// </summary>
        public int? MinLength { get; init; } = null;

        /// <summary>
        /// If specified, normalized value longer than MaxLength is invalid.
        /// </summary>
        public int? MaxLength { get; init; } = null;

        public static ExtensionNormalizationOptions Default { get; } = new();
    }

    public enum ExtensionNormalizationStatus
    {
        Ok = 0,
        Empty = 1,
        InvalidFormat = 2,
        InvalidLength = 3,
    }

    public sealed class ExtensionNormalizationResult
    {
        public string? Normalized { get; init; }
        public ExtensionNormalizationStatus Status { get; init; }
        public string Notes { get; init; } = "";

        public bool IsOk => Status == ExtensionNormalizationStatus.Ok;

        public override string ToString()
            => IsOk ? Normalized ?? "" : $"{Status}: {Notes}";
    }

    /// <summary>
    /// Normalization utilities for comparing extensions from different sources (profile vs. assignment list).
    /// Produces a stable join key (Normalized) or an error/empty status.
    /// </summary>
    public static class ExtensionNormalization
    {
        public static ExtensionNormalizationResult Normalize(string? raw, ExtensionNormalizationOptions? options = null)
        {
            options ??= ExtensionNormalizationOptions.Default;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new ExtensionNormalizationResult
                {
                    Normalized = null,
                    Status = ExtensionNormalizationStatus.Empty,
                    Notes = "Value is null/empty/whitespace."
                };
            }

            var s = raw.Trim();

            if (s.Length == 0)
            {
                return new ExtensionNormalizationResult
                {
                    Normalized = null,
                    Status = ExtensionNormalizationStatus.Empty,
                    Notes = "Value is empty after trimming."
                };
            }

            // Normalize case early for prefix stripping and alphanumeric comparisons.
            s = s.ToUpperInvariant();

            if (options.StripExtensionPrefixes)
            {
                s = StripKnownPrefix(s);
                s = s.Trim();
            }

            if (options.RemoveCommonSeparators && !options.DigitsOnly)
            {
                // In non-digits-only mode, remove separators but keep alphanumerics.
                s = RemoveCommonSeparators(s);
            }

            string normalized;
            if (options.DigitsOnly)
            {
                normalized = KeepDigitsOnly(s);

                if (normalized.Length == 0)
                {
                    return new ExtensionNormalizationResult
                    {
                        Normalized = null,
                        Status = ExtensionNormalizationStatus.Empty,
                        Notes = "No digits found after applying DigitsOnly filtering."
                    };
                }

                if (!options.PreserveLeadingZeros)
                {
                    normalized = TrimLeadingZerosButKeepOneIfAllZeros(normalized);
                }
            }
            else
            {
                // Validate and keep only allowed characters (no filtering beyond optional separator removal).
                if (options.AllowAlphanumeric)
                {
                    if (!IsAllUpperAlphaNumeric(normalizedCandidate: s))
                    {
                        return new ExtensionNormalizationResult
                        {
                            Normalized = null,
                            Status = ExtensionNormalizationStatus.InvalidFormat,
                            Notes = "Contains characters outside A-Z0-9 (after configured separator removal)."
                        };
                    }
                    normalized = s;
                }
                else
                {
                    // Digits only but without digit-filtering: must already be digits.
                    if (!IsAllDigits(s))
                    {
                        return new ExtensionNormalizationResult
                        {
                            Normalized = null,
                            Status = ExtensionNormalizationStatus.InvalidFormat,
                            Notes = "Contains non-digit characters (AllowAlphanumeric=false)."
                        };
                    }
                    normalized = s;

                    if (!options.PreserveLeadingZeros)
                    {
                        normalized = TrimLeadingZerosButKeepOneIfAllZeros(normalized);
                    }
                }
            }

            // Length validation
            if (options.MinLength.HasValue && normalized.Length < options.MinLength.Value)
            {
                return new ExtensionNormalizationResult
                {
                    Normalized = null,
                    Status = ExtensionNormalizationStatus.InvalidLength,
                    Notes = $"Length {normalized.Length} is less than MinLength {options.MinLength.Value}."
                };
            }

            if (options.MaxLength.HasValue && normalized.Length > options.MaxLength.Value)
            {
                return new ExtensionNormalizationResult
                {
                    Normalized = null,
                    Status = ExtensionNormalizationStatus.InvalidLength,
                    Notes = $"Length {normalized.Length} is greater than MaxLength {options.MaxLength.Value}."
                };
            }

            return new ExtensionNormalizationResult
            {
                Normalized = normalized,
                Status = ExtensionNormalizationStatus.Ok,
                Notes = ""
            };
        }

        /// <summary>
        /// Convenience comparison: normalizes both values and compares normalized keys.
        /// Returns false if either value is not Ok.
        /// </summary>
        public static bool AreEquivalent(string? a, string? b, ExtensionNormalizationOptions? options = null)
        {
            var na = Normalize(a, options);
            if (!na.IsOk) return false;

            var nb = Normalize(b, options);
            if (!nb.IsOk) return false;

            return StringComparer.Ordinal.Equals(na.Normalized, nb.Normalized);
        }

        /// <summary>
        /// Produces a stable dictionary key (or null) from a raw extension.
        /// </summary>
        public static string? ToKeyOrNull(string? raw, ExtensionNormalizationOptions? options = null)
        {
            var r = Normalize(raw, options);
            return r.IsOk ? r.Normalized : null;
        }

        private static string StripKnownPrefix(string s)
        {
            // We only strip from the beginning (after any trimming has been applied by caller).
            // Supported prefixes (case-insensitive due to earlier uppercasing):
            // "EXTENSION", "EXT.", "EXT", "X" followed by optional separators and then content.
            // Examples:
            // "ext 1234" -> "1234"
            // "EXT.1234" -> "1234"
            // "x-1234" -> "1234"
            // NOTE: We do not attempt to parse full phone numbers; this only removes explicit extension markers.
            ReadOnlySpan<char> span = s.AsSpan();

            span = span.TrimStart();

            if (StartsWith(span, "EXTENSION"))
            {
                span = span.Slice("EXTENSION".Length);
                return span.ToString();
            }

            if (StartsWith(span, "EXT."))
            {
                span = span.Slice("EXT.".Length);
                return span.ToString();
            }

            if (StartsWith(span, "EXT"))
            {
                span = span.Slice("EXT".Length);
                return span.ToString();
            }

            if (StartsWith(span, "X"))
            {
                // Only strip "X" when it appears as a standalone marker at start.
                // E.g. "X1234" often indicates extension 1234; we treat it as marker too.
                span = span.Slice(1);
                return span.ToString();
            }

            return s;
        }

        private static bool StartsWith(ReadOnlySpan<char> s, string prefix)
            => s.Length >= prefix.Length && s.Slice(0, prefix.Length).SequenceEqual(prefix.AsSpan());

        private static string RemoveCommonSeparators(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                // Remove typical visual separators
                if (ch == ' ' || ch == '\t' || ch == '-' || ch == '.' || ch == '(' || ch == ')' || ch == '_')
                    continue;

                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string KeepDigitsOnly(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch >= '0' && ch <= '9') sb.Append(ch);
            }
            return sb.ToString();
        }

        private static bool IsAllDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (var ch in s)
            {
                if (ch < '0' || ch > '9') return false;
            }
            return true;
        }

        private static bool IsAllUpperAlphaNumeric(string normalizedCandidate)
        {
            if (normalizedCandidate.Length == 0) return false;
            foreach (var ch in normalizedCandidate)
            {
                bool ok = (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z');
                if (!ok) return false;
            }
            return true;
        }

        private static string TrimLeadingZerosButKeepOneIfAllZeros(string digits)
        {
            int i = 0;
            while (i < digits.Length && digits[i] == '0') i++;

            if (i == 0) return digits;
            if (i >= digits.Length) return "0";
            return digits.Substring(i);
        }
    }
}
```
