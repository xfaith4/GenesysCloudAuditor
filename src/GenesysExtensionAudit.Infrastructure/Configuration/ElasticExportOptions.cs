using System.Text.RegularExpressions;

namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Controls optional Elastic export of normalized findings and run summaries.
/// Secrets are never stored here; the token is resolved from an environment variable at runtime.
/// </summary>
public sealed class ElasticExportOptions
{
    private static readonly Regex IndexNamePattern = new("^[a-z0-9._-]+$", RegexOptions.CultureInvariant);

    public bool Enabled { get; set; }
    public string EndpointUri { get; set; } = string.Empty;
    public string IndexName { get; set; } = "genesys-audit-findings";
    public string TokenEnvironmentVariableName { get; set; } = "GENESYS_AUDIT_ELASTIC_TOKEN";
    public bool IncludeRunSummaryDocument { get; set; } = true;
    public int BulkBatchSize { get; set; } = 250;

    public bool IsConfigured => TryValidate(out _);

    public bool TryValidate(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(EndpointUri))
        {
            errorMessage = "Elastic endpoint URI is required.";
            return false;
        }

        if (!Uri.TryCreate(EndpointUri.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            errorMessage = "Elastic endpoint URI must be an absolute HTTP or HTTPS URI.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(IndexName))
        {
            errorMessage = "Elastic target index is required.";
            return false;
        }

        if (!IndexNamePattern.IsMatch(IndexName.Trim()))
        {
            errorMessage = "Elastic target index may contain only lowercase letters, numbers, dots, underscores, and hyphens.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TokenEnvironmentVariableName))
        {
            errorMessage = "Elastic token environment variable name is required.";
            return false;
        }

        if (BulkBatchSize <= 0)
        {
            errorMessage = "Elastic bulk batch size must be greater than zero.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
