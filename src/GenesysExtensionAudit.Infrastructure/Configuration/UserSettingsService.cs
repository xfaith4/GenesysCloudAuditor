using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenesysExtensionAudit.Infrastructure.Http;

namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Persists user-editable settings to %APPDATA%\GenesysCloudAuditor\user-settings.json.
/// The file is loaded as a high-priority configuration source (see Bootstrapper.cs) so that
/// IOptionsMonitor automatically reflects saved changes without an app restart.
/// Sensitive values are stored encrypted for the current Windows user when possible.
/// </summary>
public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GenesysCloudAuditor");

    /// <inheritdoc/>
    public string SettingsFilePath { get; } = Path.Combine(AppDataPath, "user-settings.json");

    /// <inheritdoc/>
    public GitHubOptions LoadGitHubSettings()
    {
        if (!File.Exists(SettingsFilePath))
            return new GitHubOptions();

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var doc = JsonSerializer.Deserialize<UserSettingsFile>(json, JsonOpts);
            return ToRuntimeGitHubOptions(doc?.GitHub);
        }
        catch
        {
            return new GitHubOptions();
        }
    }

    /// <inheritdoc/>
    public void SaveGitHubSettings(GitHubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var existing = LoadSettingsFile();
        existing.GitHub = ToStoredGitHubOptions(options);
        SaveSettingsFile(existing);
    }

    /// <inheritdoc/>
    public GenesysRegionOptions LoadGenesysSettings()
    {
        if (!File.Exists(SettingsFilePath))
            return new GenesysRegionOptions();

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var doc = JsonSerializer.Deserialize<UserSettingsFile>(json, JsonOpts);
            return doc?.Genesys ?? new GenesysRegionOptions();
        }
        catch
        {
            return new GenesysRegionOptions();
        }
    }

    /// <inheritdoc/>
    public void SaveGenesysSettings(GenesysRegionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var existing = LoadSettingsFile();
        existing.Genesys = options;
        SaveSettingsFile(existing);
    }

    /// <inheritdoc/>
    public GenesysOAuthOptions LoadGenesysOAuthSettings()
    {
        if (!File.Exists(SettingsFilePath))
            return new GenesysOAuthOptions();

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var doc = JsonSerializer.Deserialize<UserSettingsFile>(json, JsonOpts);
            return ToRuntimeGenesysOAuthOptions(doc?.GenesysOAuth);
        }
        catch
        {
            return new GenesysOAuthOptions();
        }
    }

    /// <inheritdoc/>
    public void SaveGenesysOAuthSettings(GenesysOAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var existing = LoadSettingsFile();
        existing.GenesysOAuth = ToStoredGenesysOAuthOptions(options);
        SaveSettingsFile(existing);
    }

    private UserSettingsFile LoadSettingsFile()
    {
        Directory.CreateDirectory(AppDataPath);

        if (!File.Exists(SettingsFilePath))
            return new UserSettingsFile();

        try
        {
            var raw = File.ReadAllText(SettingsFilePath);
            var file = JsonSerializer.Deserialize<UserSettingsFile>(raw, JsonOpts) ?? new UserSettingsFile();
            if (ContainsLegacySensitiveValues(file))
            {
                var migrated = MigrateLegacySensitiveValues(file);
                SaveSettingsFile(migrated);
                return migrated;
            }

            return file;
        }
        catch
        {
            return new UserSettingsFile();
        }
    }

    private void SaveSettingsFile(UserSettingsFile file)
    {
        Directory.CreateDirectory(AppDataPath);
        var json = JsonSerializer.Serialize(file, JsonOpts);
        File.WriteAllText(SettingsFilePath, json);
    }

    // ── Internal file model ───────────────────────────────────────────────────

    private sealed class UserSettingsFile
    {
        [JsonPropertyName("Genesys")]
        public GenesysRegionOptions? Genesys { get; set; }

        [JsonPropertyName("GenesysOAuth")]
        public StoredGenesysOAuthOptions? GenesysOAuth { get; set; }

        [JsonPropertyName("GitHub")]
        public StoredGitHubOptions? GitHub { get; set; }
    }

    private sealed class StoredGenesysOAuthOptions
    {
        public string AuthMode { get; set; } = "auto";
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecretProtected { get; set; }
        public string? ClientSecret { get; set; }
        public string PkceClientId { get; set; } = string.Empty;
        public string PkceRedirectUri { get; set; } = "http://127.0.0.1:45731/callback";
        public string PkceScope { get; set; } = GenesysOAuthOptions.DefaultPkceScope;
        public string? PkceAccessTokenProtected { get; set; }
        public string? PkceAccessToken { get; set; }
        public string? PkceRefreshTokenProtected { get; set; }
        public string? PkceRefreshToken { get; set; }
        public DateTimeOffset? PkceAccessTokenExpiresAtUtc { get; set; }
    }

    private sealed class StoredGitHubOptions
    {
        public string? TokenProtected { get; set; }
        public string? Token { get; set; }
        public string Owner { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public string Branch { get; set; } = "main";
        public string FolderPath { get; set; } = "audit-reports";
        public string CommitMessage { get; set; } = "chore: add audit report {fileName}";
        public bool CreateDraftPr { get; set; }
        public string PrBranchPrefix { get; set; } = "audit/";
    }

    private static StoredGitHubOptions ToStoredGitHubOptions(GitHubOptions options)
        => new()
        {
            TokenProtected = ProtectSensitive(options.Token),
            Owner = options.Owner,
            Repository = options.Repository,
            Branch = options.Branch,
            FolderPath = options.FolderPath,
            CommitMessage = options.CommitMessage,
            CreateDraftPr = options.CreateDraftPr,
            PrBranchPrefix = options.PrBranchPrefix
        };

    private static GitHubOptions ToRuntimeGitHubOptions(StoredGitHubOptions? stored)
    {
        if (stored is null)
            return new GitHubOptions();

        return new GitHubOptions
        {
            Token = UnprotectSensitive(stored.TokenProtected, stored.Token),
            Owner = stored.Owner,
            Repository = stored.Repository,
            Branch = stored.Branch,
            FolderPath = stored.FolderPath,
            CommitMessage = stored.CommitMessage,
            CreateDraftPr = stored.CreateDraftPr,
            PrBranchPrefix = stored.PrBranchPrefix
        };
    }

    private static StoredGenesysOAuthOptions ToStoredGenesysOAuthOptions(GenesysOAuthOptions options)
        => new()
        {
            AuthMode = options.AuthMode,
            ClientId = options.ClientId,
            ClientSecretProtected = ProtectSensitive(options.ClientSecret),
            PkceClientId = options.PkceClientId,
            PkceRedirectUri = options.PkceRedirectUri,
            PkceScope = options.PkceScope,
            PkceAccessTokenProtected = ProtectSensitive(options.PkceAccessToken),
            PkceRefreshTokenProtected = ProtectSensitive(options.PkceRefreshToken),
            PkceAccessTokenExpiresAtUtc = options.PkceAccessTokenExpiresAtUtc
        };

    private static GenesysOAuthOptions ToRuntimeGenesysOAuthOptions(StoredGenesysOAuthOptions? stored)
    {
        if (stored is null)
            return new GenesysOAuthOptions();

        return new GenesysOAuthOptions
        {
            AuthMode = stored.AuthMode,
            ClientId = stored.ClientId,
            ClientSecret = UnprotectSensitive(stored.ClientSecretProtected, stored.ClientSecret),
            PkceClientId = stored.PkceClientId,
            PkceRedirectUri = stored.PkceRedirectUri,
            PkceScope = string.IsNullOrWhiteSpace(stored.PkceScope) ? GenesysOAuthOptions.DefaultPkceScope : stored.PkceScope,
            PkceAccessToken = UnprotectSensitive(stored.PkceAccessTokenProtected, stored.PkceAccessToken),
            PkceRefreshToken = UnprotectSensitive(stored.PkceRefreshTokenProtected, stored.PkceRefreshToken),
            PkceAccessTokenExpiresAtUtc = stored.PkceAccessTokenExpiresAtUtc
        };
    }

    private static string? ProtectSensitive(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            return null;

        if (!OperatingSystem.IsWindows())
            return null;

        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string UnprotectSensitive(string? protectedValue, string? legacyPlaintext)
    {
        if (!string.IsNullOrWhiteSpace(protectedValue) && OperatingSystem.IsWindows())
        {
            try
            {
                var protectedBytes = Convert.FromBase64String(protectedValue);
                var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // Fall back to legacy plaintext if an older file still exists or decryption fails.
            }
        }

        return legacyPlaintext ?? string.Empty;
    }

    private static bool ContainsLegacySensitiveValues(UserSettingsFile file)
        => !string.IsNullOrWhiteSpace(file.GitHub?.Token)
           || !string.IsNullOrWhiteSpace(file.GenesysOAuth?.ClientSecret)
           || !string.IsNullOrWhiteSpace(file.GenesysOAuth?.PkceAccessToken)
           || !string.IsNullOrWhiteSpace(file.GenesysOAuth?.PkceRefreshToken);

    private static UserSettingsFile MigrateLegacySensitiveValues(UserSettingsFile file)
        => new()
        {
            Genesys = file.Genesys,
            GitHub = file.GitHub is null ? null : ToStoredGitHubOptions(ToRuntimeGitHubOptions(file.GitHub)),
            GenesysOAuth = file.GenesysOAuth is null ? null : ToStoredGenesysOAuthOptions(ToRuntimeGenesysOAuthOptions(file.GenesysOAuth))
        };
}
