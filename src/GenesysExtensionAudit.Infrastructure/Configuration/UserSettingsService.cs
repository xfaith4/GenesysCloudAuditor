using System.Text.Json;
using System.Text.Json.Serialization;

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
        Directory.CreateDirectory(AppDataPath);

        // Preserve any other sections already in the file.
        UserSettingsFile existing = new();
        if (File.Exists(SettingsFilePath))
        {
            try
            {
                var raw = File.ReadAllText(SettingsFilePath);
                existing = JsonSerializer.Deserialize<UserSettingsFile>(raw, JsonOpts) ?? new();
            }
            catch { /* ignore corrupt file */ }
        }

        existing.GitHub = options;
        var json = JsonSerializer.Serialize(existing, JsonOpts);
        File.WriteAllText(SettingsFilePath, json);
    }

    // ── Internal file model ───────────────────────────────────────────────────

    private sealed class UserSettingsFile
    {
        [JsonPropertyName("GitHub")]
        public GitHubOptions? GitHub { get; set; }
    }
}
