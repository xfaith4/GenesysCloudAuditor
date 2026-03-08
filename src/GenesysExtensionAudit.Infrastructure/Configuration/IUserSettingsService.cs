namespace GenesysExtensionAudit.Infrastructure.Configuration;

/// <summary>
/// Reads and persists user-editable settings to a JSON file in %APPDATA%.
/// Changes written via <see cref="SaveGitHubSettings"/> are picked up immediately
/// by any <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> registered
/// with reloadOnChange.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>Full path to the user settings JSON file.</summary>
    string SettingsFilePath { get; }

    /// <summary>Returns the current GitHub options from the persisted file, or defaults if not yet saved.</summary>
    GitHubOptions LoadGitHubSettings();

    /// <summary>Persists <paramref name="options"/> to the user settings file.</summary>
    void SaveGitHubSettings(GitHubOptions options);
}
