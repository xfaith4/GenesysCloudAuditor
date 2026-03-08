using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.ViewModels;

/// <summary>
/// ViewModel for the Settings tab.
/// Loads current GitHub configuration from IOptionsMonitor and persists
/// changes via IUserSettingsService (written to %APPDATA%\GenesysCloudAuditor\user-settings.json).
/// Because that file is loaded with reloadOnChange=true, IOptionsMonitor
/// automatically picks up the new values and propagates them to GitHubUploadService.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IUserSettingsService _userSettings;
    private readonly IOptionsMonitor<GitHubOptions> _gitHubMonitor;

    private string _gitHubToken = string.Empty;
    private string _gitHubOwner = string.Empty;
    private string _gitHubRepository = string.Empty;
    private string _gitHubBranch = "main";
    private string _gitHubFolderPath = "audit-reports";
    private bool _createDraftPr;
    private string _prBranchPrefix = "audit/";
    private string _statusMessage = string.Empty;
    private bool _hasError;

    public SettingsViewModel(
        IUserSettingsService userSettings,
        IOptionsMonitor<GitHubOptions> gitHubMonitor)
    {
        _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        _gitHubMonitor = gitHubMonitor ?? throw new ArgumentNullException(nameof(gitHubMonitor));

        SaveCommand = new RelayCommand(Save);
        LoadFromOptions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── GitHub fields ─────────────────────────────────────────────────────────

    public string GitHubToken
    {
        get => _gitHubToken;
        set => SetField(ref _gitHubToken, value);
    }

    public string GitHubOwner
    {
        get => _gitHubOwner;
        set => SetField(ref _gitHubOwner, value);
    }

    public string GitHubRepository
    {
        get => _gitHubRepository;
        set => SetField(ref _gitHubRepository, value);
    }

    /// <summary>
    /// The base branch for the repository.
    /// When CreateDraftPr=false this is the branch commits land on directly.
    /// When CreateDraftPr=true this is the PR target branch.
    /// </summary>
    public string GitHubBranch
    {
        get => _gitHubBranch;
        set => SetField(ref _gitHubBranch, value);
    }

    public string GitHubFolderPath
    {
        get => _gitHubFolderPath;
        set => SetField(ref _gitHubFolderPath, value);
    }

    /// <summary>When true, export creates a draft PR instead of committing directly.</summary>
    public bool CreateDraftPr
    {
        get => _createDraftPr;
        set => SetField(ref _createDraftPr, value);
    }

    /// <summary>Prefix used for the auto-generated PR source branch (e.g. "audit/").</summary>
    public string PrBranchPrefix
    {
        get => _prBranchPrefix;
        set => SetField(ref _prBranchPrefix, value);
    }

    // ── Status ────────────────────────────────────────────────────────────────

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetField(ref _hasError, value);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(GitHubToken) &&
        !string.IsNullOrWhiteSpace(GitHubOwner) &&
        !string.IsNullOrWhiteSpace(GitHubRepository);

    public ICommand SaveCommand { get; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Save()
    {
        try
        {
            HasError = false;
            _userSettings.SaveGitHubSettings(new GitHubOptions
            {
                Token = GitHubToken.Trim(),
                Owner = GitHubOwner.Trim(),
                Repository = GitHubRepository.Trim(),
                Branch = string.IsNullOrWhiteSpace(GitHubBranch) ? "main" : GitHubBranch.Trim(),
                FolderPath = string.IsNullOrWhiteSpace(GitHubFolderPath) ? "audit-reports" : GitHubFolderPath.Trim(),
                CreateDraftPr = CreateDraftPr,
                PrBranchPrefix = string.IsNullOrWhiteSpace(PrBranchPrefix) ? "audit/" : PrBranchPrefix.Trim(),
                CommitMessage = _gitHubMonitor.CurrentValue.CommitMessage  // preserve advanced setting
            });

            StatusMessage = "Settings saved. Changes take effect immediately.";
            OnPropertyChanged(nameof(IsConfigured));
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to save settings: {ex.Message}";
        }
    }

    private void LoadFromOptions()
    {
        // Prefer the persisted user-settings file; fall back to appsettings.json values.
        var saved = _userSettings.LoadGitHubSettings();
        var fallback = _gitHubMonitor.CurrentValue;

        GitHubToken = Coalesce(saved.Token, fallback.Token);
        GitHubOwner = Coalesce(saved.Owner, fallback.Owner);
        GitHubRepository = Coalesce(saved.Repository, fallback.Repository);
        GitHubBranch = Coalesce(saved.Branch, fallback.Branch, "main");
        GitHubFolderPath = Coalesce(saved.FolderPath, fallback.FolderPath, "audit-reports");
        CreateDraftPr = saved.CreateDraftPr || fallback.CreateDraftPr;
        PrBranchPrefix = Coalesce(saved.PrBranchPrefix, fallback.PrBranchPrefix, "audit/");
    }

    private static string Coalesce(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v!;
        return string.Empty;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
