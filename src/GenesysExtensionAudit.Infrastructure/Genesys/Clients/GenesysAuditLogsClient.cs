using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysAuditLogsClient
{
    /// <summary>Returns a flat sorted list of service names from the tenant's service mapping.</summary>
    Task<IReadOnlyList<string>> GetServiceMappingsAsync(CancellationToken ct);

    /// <summary>
    /// Returns structured service catalog information including entity types and actions per service.
    /// Used to populate filter dropdowns in the UI.
    /// </summary>
    Task<IReadOnlyList<AuditServiceInfo>> GetServiceCatalogAsync(CancellationToken ct);

    Task<string> SubmitAuditQueryAsync(AuditLogsSubmitRequestDto request, CancellationToken ct);
    Task<AuditQueryStatusDto> GetAuditQueryStatusAsync(string transactionId, CancellationToken ct);

    /// <summary>
    /// Fetches one page of results. The initial call (nextUri null) requests pageSize=500
    /// with expand=user so that user details are inline rather than requiring secondary lookups.
    /// </summary>
    Task<AuditLogsResultsPageDto> GetAuditQueryResultsPageAsync(string transactionId, string? nextUri, CancellationToken ct);
}

public sealed class GenesysAuditLogsClient : GenesysCloudApiClient, IGenesysAuditLogsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public GenesysAuditLogsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysAuditLogsClient> logger)
        : base(http, tokenProvider, regionOptions, logger)
    {
    }

    // ─── Service Mapping ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<string>> GetServiceMappingsAsync(CancellationToken ct)
    {
        var catalog = await GetServiceCatalogAsync(ct).ConfigureAwait(false);
        return catalog.Select(s => s.ServiceName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<AuditServiceInfo>> GetServiceCatalogAsync(CancellationToken ct)
    {
        // Try standard service mapping first, then realtime, then action catalog as fallback.
        var standard = await TryGetCatalogAsync("/api/v2/audits/query/servicemapping", ct).ConfigureAwait(false);
        var realtime = await TryGetCatalogAsync("/api/v2/audits/query/realtime/servicemapping", ct).ConfigureAwait(false);

        var byService = new Dictionary<string, (HashSet<string> EntityTypes, HashSet<string> Actions)>(
            StringComparer.OrdinalIgnoreCase);

        if (standard.HasValue)
            ParseServiceMapping(standard.Value, byService);
        if (realtime.HasValue)
            ParseServiceMapping(realtime.Value, byService);

        // Fallback: action catalog gives us service names at minimum.
        if (byService.Count == 0)
        {
            var catalog = await TryGetCatalogAsync("/api/v2/audits/query/actioncatalog", ct).ConfigureAwait(false);
            if (catalog.HasValue)
                ParseServiceMapping(catalog.Value, byService);
        }

        return byService
            .Select(kvp => new AuditServiceInfo
            {
                ServiceName = kvp.Key,
                EntityTypes = kvp.Value.EntityTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                Actions = kvp.Value.Actions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ─── Query ───────────────────────────────────────────────────────────────

    public async Task<string> SubmitAuditQueryAsync(AuditLogsSubmitRequestDto request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, ApiUri("/api/v2/audits/query"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var json = await SendJsonAsync<JsonElement>(message, ct).ConfigureAwait(false);
        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("transactionId", out var tx) &&
            tx.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(tx.GetString()))
        {
            return tx.GetString()!;
        }

        throw new InvalidOperationException("Audit query submit response did not contain transactionId.");
    }

    public Task<AuditQueryStatusDto> GetAuditQueryStatusAsync(string transactionId, CancellationToken ct)
    {
        var path = $"/api/v2/audits/query/{transactionId}";
        return GetJsonAsync<AuditQueryStatusDto>(path, ct);
    }

    public async Task<AuditLogsResultsPageDto> GetAuditQueryResultsPageAsync(
        string transactionId,
        string? nextUri,
        CancellationToken ct)
    {
        // Follow the nextUri cursor directly when paging; the cursor already includes all query params.
        if (!string.IsNullOrWhiteSpace(nextUri))
        {
            if (Uri.TryCreate(nextUri, UriKind.Absolute, out var absolute))
            {
                using var msg = new HttpRequestMessage(HttpMethod.Get, absolute);
                msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return await SendJsonAsync<AuditLogsResultsPageDto>(msg, ct).ConfigureAwait(false);
            }

            return await GetJsonAsync<AuditLogsResultsPageDto>(nextUri, ct).ConfigureAwait(false);
        }

        // Initial fetch: request maximum page size and expand user details inline
        // so secondary user-lookup calls are not required.
        var path = $"/api/v2/audits/query/{transactionId}/results?pageSize=500&expand=user";
        return await GetJsonAsync<AuditLogsResultsPageDto>(path, ct).ConfigureAwait(false);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Parses a service-mapping JSON element and populates <paramref name="byService"/>.
    /// The Genesys service mapping is a JSON object keyed by service name, where each value
    /// is an object containing "entityTypes" (string array) and "actions" (string array).
    /// Example:
    /// <code>
    /// {
    ///   "Architect": { "entityTypes": ["Flow","Prompt"], "actions": ["CREATE","UPDATE","DELETE"] },
    ///   "Routing":   { "entityTypes": ["Queue"],          "actions": ["CREATE","UPDATE"] }
    /// }
    /// </code>
    /// If the shape differs (e.g. wrapped in an "entities" list), top-level keys are still
    /// added as service names with empty entity type / action sets.
    /// </summary>
    private static void ParseServiceMapping(
        JsonElement root,
        Dictionary<string, (HashSet<string> EntityTypes, HashSet<string> Actions)> byService)
        => ParseServiceElement(root, byService);

    private static void ParseServiceElement(
        JsonElement element,
        Dictionary<string, (HashSet<string> EntityTypes, HashSet<string> Actions)> byService)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryParseNamedServiceElement(element, byService))
                    return;

                foreach (var prop in element.EnumerateObject())
                {
                    var name = prop.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (IsContainerPropertyName(name))
                    {
                        ParseServiceElement(prop.Value, byService);
                        continue;
                    }

                    if (LooksLikeServiceDescriptor(prop.Value))
                    {
                        MergeServiceDescriptor(name, prop.Value, byService);
                        continue;
                    }

                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        ParseServiceElement(prop.Value, byService);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ParseServiceElement(item, byService);

                break;
        }
    }

    private static bool TryParseNamedServiceElement(
        JsonElement element,
        Dictionary<string, (HashSet<string> EntityTypes, HashSet<string> Actions)> byService)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var serviceName =
            GetStringProperty(element, "serviceName")
            ?? GetStringProperty(element, "service")
            ?? GetStringProperty(element, "name");

        if (string.IsNullOrWhiteSpace(serviceName) || IsContainerPropertyName(serviceName))
            return false;

        if (!LooksLikeServiceDescriptor(element))
            return false;

        MergeServiceDescriptor(serviceName, element, byService);
        return true;
    }

    private static bool LooksLikeServiceDescriptor(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        return element.TryGetProperty("entityTypes", out _)
               || element.TryGetProperty("actions", out _)
               || element.TryGetProperty("actionMappings", out _)
               || element.TryGetProperty("entityType", out _)
               || element.TryGetProperty("action", out _);
    }

    private static void MergeServiceDescriptor(
        string serviceName,
        JsonElement descriptor,
        Dictionary<string, (HashSet<string> EntityTypes, HashSet<string> Actions)> byService)
    {
        var normalizedServiceName = serviceName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedServiceName) || IsContainerPropertyName(normalizedServiceName))
            return;

        if (!byService.TryGetValue(normalizedServiceName, out var entry))
        {
            entry = (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                     new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            byService[normalizedServiceName] = entry;
        }

        AddStrings(descriptor, "entityTypes", entry.EntityTypes);
        AddStrings(descriptor, "actions", entry.Actions);
        AddString(descriptor, "entityType", entry.EntityTypes);
        AddString(descriptor, "action", entry.Actions);

        if (descriptor.TryGetProperty("actionMappings", out var actionMappings))
            ParseActionMappings(actionMappings, entry.Actions);
    }

    private static void ParseActionMappings(JsonElement actionMappings, HashSet<string> actions)
    {
        switch (actionMappings.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in actionMappings.EnumerateArray())
                {
                    AddString(item, "action", actions);
                    AddString(item, "name", actions);
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                            actions.Add(value);
                    }
                }

                break;

            case JsonValueKind.Object:
                foreach (var prop in actionMappings.EnumerateObject())
                {
                    var name = prop.Name?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        actions.Add(name);

                    AddString(prop.Value, "action", actions);
                    AddString(prop.Value, "name", actions);
                }

                break;
        }
    }

    private static void AddStrings(JsonElement element, string propertyName, HashSet<string> values)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) ||
            propertyValue.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in propertyValue.EnumerateArray())
        {
            var value = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }
    }

    private static void AddString(JsonElement element, string propertyName, HashSet<string> values)
    {
        var value = GetStringProperty(element, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(value);
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) ||
            propertyValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyValue.GetString()?.Trim();
    }

    private async Task<JsonElement?> TryGetCatalogAsync(string path, CancellationToken ct)
    {
        try
        {
            return await GetJsonAsync<JsonElement>(path, ct).ConfigureAwait(false);
        }
        catch (GenesysApiException ex) when ((int)ex.StatusCode == 404 || (int)ex.StatusCode == 400)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsContainerPropertyName(string name)
        => name.Equals("entities", StringComparison.OrdinalIgnoreCase)
           || name.Equals("services", StringComparison.OrdinalIgnoreCase)
           || name.Equals("results", StringComparison.OrdinalIgnoreCase)
           || name.Equals("items", StringComparison.OrdinalIgnoreCase)
           || name.Equals("serviceMappings", StringComparison.OrdinalIgnoreCase)
           || name.Equals("actionMappings", StringComparison.OrdinalIgnoreCase)
           || name.Equals("selfUri", StringComparison.OrdinalIgnoreCase)
           || name.Equals("nextUri", StringComparison.OrdinalIgnoreCase)
           || name.Equals("previousUri", StringComparison.OrdinalIgnoreCase);
}
