using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.BestPractices;

public interface IBestPracticesContentService
{
    void EnsureLoaded();
    BestPracticesContentSnapshot GetSnapshot();
    BestPracticesContentStatus GetStatus();
}

public sealed class BestPracticesContentService : IBestPracticesContentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly BestPracticesOptions _options;
    private readonly ILogger<BestPracticesContentService> _logger;
    private readonly object _syncRoot = new();
    private BestPracticesContentSnapshot? _snapshot;

    public BestPracticesContentService(
        IOptions<BestPracticesOptions> options,
        ILogger<BestPracticesContentService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void EnsureLoaded() => _ = GetSnapshot();

    public BestPracticesContentSnapshot GetSnapshot()
    {
        if (_snapshot is not null)
            return _snapshot;

        lock (_syncRoot)
        {
            _snapshot ??= LoadSnapshot();
            return _snapshot;
        }
    }

    public BestPracticesContentStatus GetStatus() => GetSnapshot().Status;

    private BestPracticesContentSnapshot LoadSnapshot()
    {
        var messages = new List<string>();
        var fileStatuses = new List<BestPracticeFileStatus>();
        var resolvedRoot = ResolveRootPath(_options.RootPath);

        if (resolvedRoot is null)
        {
            messages.Add("Unable to resolve shared best-practices root path.");
            var missingRootStatus = new BestPracticesContentStatus(
                RootPathResolved: false,
                ResolvedRootPath: null,
                IsHealthy: false,
                CatalogCount: 0,
                MappingCount: 0,
                GlossaryCount: 0,
                Files: [],
                ReferenceDocuments: BuildReferenceDocuments(null),
                Messages: messages);

            _logger.LogWarning("Best-practices content root could not be resolved from base directory {BaseDirectory}.", AppContext.BaseDirectory);
            return BestPracticesContentSnapshot.Empty(missingRootStatus);
        }

        _logger.LogInformation("Best-practices content root resolved to {RootPath}", resolvedRoot);

        var catalog = LoadJsonFile<BestPracticeCatalog>(
            "Catalog",
            ResolveUnderRoot(resolvedRoot, _options.CatalogPath),
            ResolveUnderRoot(resolvedRoot, _options.CatalogSchemaPath),
            ValidateGlossaryLikeShape: false,
            fileStatuses,
            messages);

        var map = LoadJsonFile<BestPracticeMap>(
            "Map",
            ResolveUnderRoot(resolvedRoot, _options.MapPath),
            ResolveUnderRoot(resolvedRoot, _options.MapSchemaPath),
            ValidateGlossaryLikeShape: false,
            fileStatuses,
            messages);

        var glossary = LoadJsonFile<GlossaryCatalog>(
            "Glossary",
            ResolveUnderRoot(resolvedRoot, _options.GlossaryPath),
            schemaPath: null,
            ValidateGlossaryLikeShape: true,
            fileStatuses,
            messages);

        var status = new BestPracticesContentStatus(
            RootPathResolved: true,
            ResolvedRootPath: resolvedRoot,
            IsHealthy: fileStatuses.All(f => f.Exists && f.LoadedSuccessfully && f.ValidationPassed),
            CatalogCount: catalog?.Catalog.Count ?? 0,
            MappingCount: map?.Mappings.Count ?? 0,
            GlossaryCount: glossary?.Terms.Count ?? 0,
            Files: fileStatuses,
            ReferenceDocuments: BuildReferenceDocuments(resolvedRoot),
            Messages: messages);

        _logger.LogInformation(
            "Best-practices content loaded. CatalogEntries={CatalogCount} MappingEntries={MappingCount} GlossaryTerms={GlossaryCount} Healthy={Healthy}",
            status.CatalogCount,
            status.MappingCount,
            status.GlossaryCount,
            status.IsHealthy);

        return new BestPracticesContentSnapshot(
            catalog ?? new BestPracticeCatalog(),
            map ?? new BestPracticeMap(),
            glossary ?? new GlossaryCatalog(),
            status);
    }

    private T? LoadJsonFile<T>(
        string logicalName,
        string filePath,
        string? schemaPath,
        bool ValidateGlossaryLikeShape,
        ICollection<BestPracticeFileStatus> fileStatuses,
        ICollection<string> messages)
        where T : class
    {
        var fileMessages = new List<string>();
        if (!File.Exists(filePath))
        {
            var message = $"{logicalName} file not found: {filePath}";
            _logger.LogWarning("{Message}", message);
            messages.Add(message);
            fileStatuses.Add(new BestPracticeFileStatus(logicalName, filePath, false, false, false, fileMessages.Append(message).ToList()));
            return null;
        }

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            var message = $"{logicalName} file could not be read: {ex.Message}";
            _logger.LogWarning(ex, "{Message}", message);
            messages.Add(message);
            fileStatuses.Add(new BestPracticeFileStatus(logicalName, filePath, true, false, false, fileMessages.Append(message).ToList()));
            return null;
        }

        JsonNode? documentNode;
        try
        {
            documentNode = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            var message = $"{logicalName} file contains invalid JSON: {ex.Message}";
            _logger.LogWarning(ex, "{Message}", message);
            messages.Add(message);
            fileStatuses.Add(new BestPracticeFileStatus(logicalName, filePath, true, false, false, fileMessages.Append(message).ToList()));
            return null;
        }

        var validationPassed = true;
        if (!string.IsNullOrWhiteSpace(schemaPath))
        {
            if (!File.Exists(schemaPath))
            {
                var schemaMessage = $"{logicalName} schema file not found: {schemaPath}";
                fileMessages.Add(schemaMessage);
                messages.Add(schemaMessage);
                validationPassed = false;
                _logger.LogWarning("{Message}", schemaMessage);
            }
            else
            {
                var schemaErrors = ValidateAgainstSchema(documentNode, schemaPath);
                if (schemaErrors.Count > 0)
                {
                    validationPassed = false;
                    foreach (var error in schemaErrors)
                    {
                        var message = $"{logicalName} schema validation: {error}";
                        fileMessages.Add(message);
                        messages.Add(message);
                        _logger.LogWarning("{Message}", message);
                    }
                }
            }
        }

        if (ValidateGlossaryLikeShape)
        {
            var glossaryErrors = ValidateGlossary(documentNode);
            if (glossaryErrors.Count > 0)
            {
                validationPassed = false;
                foreach (var error in glossaryErrors)
                {
                    var message = $"{logicalName} validation: {error}";
                    fileMessages.Add(message);
                    messages.Add(message);
                    _logger.LogWarning("{Message}", message);
                }
            }
        }

        if (!validationPassed && _options.FailOnValidationError)
        {
            fileStatuses.Add(new BestPracticeFileStatus(logicalName, filePath, true, false, false, fileMessages));
            return null;
        }

        try
        {
            var model = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (model is null)
            {
                var message = $"{logicalName} deserialized to null.";
                _logger.LogWarning("{Message}", message);
                fileMessages.Add(message);
                messages.Add(message);
                fileStatuses.Add(new BestPracticeFileStatus(logicalName, filePath, true, validationPassed, false, fileMessages));
                return null;
            }

            fileStatuses.Add(new BestPracticeFileStatus(logicalName, filePath, true, validationPassed, true, fileMessages));
            return model;
        }
        catch (Exception ex)
        {
            var message = $"{logicalName} deserialization failed: {ex.Message}";
            _logger.LogWarning(ex, "{Message}", message);
            fileMessages.Add(message);
            messages.Add(message);
            fileStatuses.Add(new BestPracticeFileStatus(logicalName, filePath, true, validationPassed, false, fileMessages));
            return null;
        }
    }

    private static string? ResolveRootPath(string configuredRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            var normalizedConfiguredPath = configuredRootPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathFullyQualified(normalizedConfiguredPath) && Directory.Exists(normalizedConfiguredPath))
                return Path.GetFullPath(normalizedConfiguredPath);

            var relativeCandidate = Path.GetFullPath(normalizedConfiguredPath, AppContext.BaseDirectory);
            if (Directory.Exists(relativeCandidate))
                return relativeCandidate;
        }

        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "shared", "Genesys.BestPractices");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string ResolveUnderRoot(string rootPath, string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(rootPath, normalized));
    }

    private static IReadOnlyList<BestPracticeReferenceDocument> BuildReferenceDocuments(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return
            [
                new BestPracticeReferenceDocument("README", null, false),
                new BestPracticeReferenceDocument("Glossary", null, false),
                new BestPracticeReferenceDocument("Roadmap", null, false),
                new BestPracticeReferenceDocument("BestPractices", null, false)
            ];
        }

        var documents = new[]
        {
            new { Name = "README", RelativePath = "README.md" },
            new { Name = "Glossary", RelativePath = "Glossary.md" },
            new { Name = "Roadmap", RelativePath = "Roadmap.md" },
            new { Name = "BestPractices", RelativePath = Path.Combine("best-practices", "BestPractices.md") }
        };

        return documents
            .Select(document =>
            {
                var path = ResolveUnderRoot(rootPath, document.RelativePath);
                return new BestPracticeReferenceDocument(document.Name, path, File.Exists(path));
            })
            .ToList();
    }

    private static IReadOnlyList<string> ValidateAgainstSchema(JsonNode? documentNode, string schemaPath)
    {
        JsonNode? schemaNode;
        try
        {
            schemaNode = JsonNode.Parse(File.ReadAllText(schemaPath));
        }
        catch (Exception ex)
        {
            return [$"schema could not be read: {ex.Message}"];
        }

        var errors = new List<string>();
        ValidateNode(documentNode, schemaNode, "$", errors);
        return errors;
    }

    private static IReadOnlyList<string> ValidateGlossary(JsonNode? documentNode)
    {
        var errors = new List<string>();
        if (documentNode is not JsonObject glossaryObject)
        {
            errors.Add("root must be a JSON object.");
            return errors;
        }

        if (GetString(glossaryObject["version"]) is null)
            errors.Add("$.version is required.");

        var generatedOn = GetString(glossaryObject["generated_on"]);
        if (generatedOn is null || !Regex.IsMatch(generatedOn, "^\\d{4}-\\d{2}-\\d{2}$"))
            errors.Add("$.generated_on must be a yyyy-MM-dd string.");

        if (glossaryObject["terms"] is not JsonArray termsArray)
        {
            errors.Add("$.terms must be an array.");
            return errors;
        }

        for (var i = 0; i < termsArray.Count; i++)
        {
            if (termsArray[i] is not JsonObject termObject)
            {
                errors.Add($"$.terms[{i}] must be an object.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(GetString(termObject["term"])))
                errors.Add($"$.terms[{i}].term is required.");
            if (string.IsNullOrWhiteSpace(GetString(termObject["domain"])))
                errors.Add($"$.terms[{i}].domain is required.");
            if (string.IsNullOrWhiteSpace(GetString(termObject["definition"])))
                errors.Add($"$.terms[{i}].definition is required.");
        }

        return errors;
    }

    private static void ValidateNode(JsonNode? instance, JsonNode? schemaNode, string path, ICollection<string> errors)
    {
        if (schemaNode is not JsonObject schema)
            return;

        var declaredType = GetString(schema["type"]);
        if (!string.IsNullOrWhiteSpace(declaredType) && !MatchesType(instance, declaredType))
        {
            errors.Add($"{path} must be {declaredType}.");
            return;
        }

        if (schema["enum"] is JsonArray enumArray)
        {
            var actualValue = GetScalarValue(instance);
            var allowedValues = enumArray
                .Select(GetScalarValue)
                .Where(value => value is not null)
                .ToHashSet(StringComparer.Ordinal);

            if (actualValue is null || !allowedValues.Contains(actualValue))
                errors.Add($"{path} must be one of [{string.Join(", ", allowedValues)}].");
        }

        if (instance is JsonValue stringValue && GetString(schema["pattern"]) is string pattern)
        {
            var text = stringValue.ToString();
            if (!Regex.IsMatch(text, pattern))
                errors.Add($"{path} does not match required pattern.");
        }

        if (instance is JsonArray array)
        {
            if (schema["minItems"] is JsonValue minItemsValue && minItemsValue.TryGetValue<int>(out var minItems) && array.Count < minItems)
                errors.Add($"{path} must contain at least {minItems} item(s).");

            if (schema["uniqueItems"] is JsonValue uniqueItemsValue && uniqueItemsValue.TryGetValue<bool>(out var uniqueItems) && uniqueItems)
            {
                var duplicates = array
                    .Select(item => item?.ToJsonString() ?? "null")
                    .GroupBy(item => item, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToList();

                if (duplicates.Count > 0)
                    errors.Add($"{path} contains duplicate item(s).");
            }

            if (schema["items"] is JsonNode itemSchema)
            {
                for (var i = 0; i < array.Count; i++)
                    ValidateNode(array[i], itemSchema, $"{path}[{i}]", errors);
            }
        }

        if (instance is JsonObject obj)
        {
            var properties = schema["properties"] as JsonObject;
            var required = schema["required"] as JsonArray;
            if (required is not null)
            {
                foreach (var requiredNode in required)
                {
                    var requiredName = GetScalarValue(requiredNode);
                    if (!string.IsNullOrWhiteSpace(requiredName) && !obj.ContainsKey(requiredName))
                        errors.Add($"{path}.{requiredName} is required.");
                }
            }

            if (schema["additionalProperties"] is JsonValue additionalPropertiesValue &&
                additionalPropertiesValue.TryGetValue<bool>(out var allowAdditionalProperties) &&
                !allowAdditionalProperties &&
                properties is not null)
            {
                var allowedNames = properties.Select(property => property.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var property in obj)
                {
                    if (!allowedNames.Contains(property.Key))
                        errors.Add($"{path}.{property.Key} is not allowed by schema.");
                }
            }

            if (properties is not null)
            {
                foreach (var property in properties)
                {
                    if (obj.TryGetPropertyValue(property.Key, out var childNode))
                        ValidateNode(childNode, property.Value, $"{path}.{property.Key}", errors);
                }
            }
        }
    }

    private static bool MatchesType(JsonNode? instance, string declaredType) => declaredType switch
    {
        "object" => instance is JsonObject,
        "array" => instance is JsonArray,
        "string" => instance is JsonValue value && value.TryGetValue<string>(out _),
        "boolean" => instance is JsonValue value && value.TryGetValue<bool>(out _),
        _ => true
    };

    private static string? GetScalarValue(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;

        if (value.TryGetValue<string>(out var stringValue))
            return stringValue;
        if (value.TryGetValue<bool>(out var boolValue))
            return boolValue ? "true" : "false";
        if (value.TryGetValue<int>(out var intValue))
            return intValue.ToString();

        return value.ToJsonString();
    }

    private static string? GetString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var stringValue) ? stringValue : null;
}
