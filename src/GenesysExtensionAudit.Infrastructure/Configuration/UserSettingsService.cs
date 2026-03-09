using System.Text.Json;
using System.Text.Json.Serialization;
using GenesysExtensionAudit.Infrastructure.Http;

namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Persists user-editable settings to %APPDATA%\GenesysCloudAuditor\user-settings.json.
/// The file is loaded as a high-priority configuration source (see Bootstrapper.cs) so that
/// IOptionsMonitor automatically reflects saved changes without an app restart.
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
            return doc?.GitHub ?? new GitHubOptions();
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
        existing.GitHub = options;
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
            return doc?.GenesysOAuth ?? new GenesysOAuthOptions();
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
        existing.GenesysOAuth = options;
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
            return JsonSerializer.Deserialize<UserSettingsFile>(raw, JsonOpts) ?? new UserSettingsFile();
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
        public GenesysOAuthOptions? GenesysOAuth { get; set; }

        [JsonPropertyName("GitHub")]
        public GitHubOptions? GitHub { get; set; }
    }
}
