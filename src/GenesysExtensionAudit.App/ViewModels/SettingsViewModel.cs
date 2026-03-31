using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GenesysExtensionAudit.Infrastructure.Configuration;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.ViewModels;

/// <summary>
/// ViewModel for the Settings tab.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IUserSettingsService _userSettings;
    private readonly IOptionsMonitor<GitHubOptions> _gitHubMonitor;
    private readonly IOptionsMonitor<ElasticExportOptions> _elasticMonitor;
    private readonly IOptionsMonitor<GenesysRegionOptions> _genesysMonitor;
    private readonly IOptionsMonitor<GenesysOAuthOptions> _oauthMonitor;
    private readonly IGenesysPkceAuthService _pkceAuthService;

    private string _genesysRegion = "usw2.pure.cloud";
    private string _authMode = "auto";
    private string _pkceClientId = string.Empty;
    private string _pkceRedirectUri = "http://127.0.0.1:45731/callback";
    private string _pkceScope = GenesysOAuthOptions.DefaultPkceScope;
    private string _clientId = string.Empty;
    private string _clientSecret = string.Empty;
    private DateTimeOffset? _pkceAccessTokenExpiresAtUtc;

    private string _gitHubToken = string.Empty;
    private string _gitHubOwner = string.Empty;
    private string _gitHubRepository = string.Empty;
    private string _gitHubBranch = "main";
    private string _gitHubFolderPath = "audit-reports";
    private bool _createDraftPr;
    private string _prBranchPrefix = "audit/";

    private bool _elasticExportEnabled;
    private string _elasticEndpointUri = string.Empty;
    private string _elasticIndexName = "genesys-audit-findings";
    private string _elasticTokenEnvironmentVariableName = "GENESYS_AUDIT_ELASTIC_TOKEN";

    private string _statusMessage = string.Empty;
    private bool _hasError;
    private bool _isAuthenticatingPkce;

    public SettingsViewModel(
        IUserSettingsService userSettings,
        IOptionsMonitor<GitHubOptions> gitHubMonitor,
        IOptionsMonitor<ElasticExportOptions> elasticMonitor,
        IOptionsMonitor<GenesysRegionOptions> genesysMonitor,
        IOptionsMonitor<GenesysOAuthOptions> oauthMonitor,
        IGenesysPkceAuthService pkceAuthService)
    {
        _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        _gitHubMonitor = gitHubMonitor ?? throw new ArgumentNullException(nameof(gitHubMonitor));
        _elasticMonitor = elasticMonitor ?? throw new ArgumentNullException(nameof(elasticMonitor));
        _genesysMonitor = genesysMonitor ?? throw new ArgumentNullException(nameof(genesysMonitor));
        _oauthMonitor = oauthMonitor ?? throw new ArgumentNullException(nameof(oauthMonitor));
        _pkceAuthService = pkceAuthService ?? throw new ArgumentNullException(nameof(pkceAuthService));

        SaveCommand = new RelayCommand(Save);
        StartPkceAuthCommand = new RelayCommand(StartPkceAuthAsync, () => !IsAuthenticatingPkce);
        CheckPkcePortCommand = new RelayCommand(CheckPkcePortAvailability);

        LoadFromOptions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string GenesysRegion
    {
        get => _genesysRegion;
        set => SetField(ref _genesysRegion, value);
    }

    public string AuthMode
    {
        get => _authMode;
        set
        {
            if (SetField(ref _authMode, NormalizeAuthMode(value)))
                OnPropertyChanged(nameof(IsClientCredentialsMode));
        }
    }

    public bool IsClientCredentialsMode => AuthMode == "client_credentials";

    public string PkceClientId
    {
        get => _pkceClientId;
        set => SetField(ref _pkceClientId, value);
    }

    public string PkceRedirectUri
    {
        get => _pkceRedirectUri;
        set => SetField(ref _pkceRedirectUri, value);
    }

    public string PkceScope
    {
        get => _pkceScope;
        set => SetField(ref _pkceScope, value);
    }

    public DateTimeOffset? PkceAccessTokenExpiresAtUtc
    {
        get => _pkceAccessTokenExpiresAtUtc;
        private set => SetField(ref _pkceAccessTokenExpiresAtUtc, value);
    }

    public string ClientId
    {
        get => _clientId;
        set => SetField(ref _clientId, value);
    }

    public string ClientSecret
    {
        get => _clientSecret;
        set => SetField(ref _clientSecret, value);
    }

    public bool IsAuthenticatingPkce
    {
        get => _isAuthenticatingPkce;
        private set
        {
            if (SetField(ref _isAuthenticatingPkce, value) && StartPkceAuthCommand is RelayCommand cmd)
                cmd.RaiseCanExecuteChanged();
        }
    }

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

    public bool CreateDraftPr
    {
        get => _createDraftPr;
        set => SetField(ref _createDraftPr, value);
    }

    public string PrBranchPrefix
    {
        get => _prBranchPrefix;
        set => SetField(ref _prBranchPrefix, value);
    }

    public bool ElasticExportEnabled
    {
        get => _elasticExportEnabled;
        set => SetField(ref _elasticExportEnabled, value);
    }

    public string ElasticEndpointUri
    {
        get => _elasticEndpointUri;
        set => SetField(ref _elasticEndpointUri, value);
    }

    public string ElasticIndexName
    {
        get => _elasticIndexName;
        set => SetField(ref _elasticIndexName, value);
    }

    public string ElasticTokenEnvironmentVariableName
    {
        get => _elasticTokenEnvironmentVariableName;
        set => SetField(ref _elasticTokenEnvironmentVariableName, value);
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
    public ICommand StartPkceAuthCommand { get; }
    public ICommand CheckPkcePortCommand { get; }

    private void Save()
    {
        try
        {
            HasError = false;

            _userSettings.SaveGenesysSettings(new GenesysRegionOptions
            {
                Region = string.IsNullOrWhiteSpace(GenesysRegion) ? "usw2.pure.cloud" : GenesysRegion.Trim(),
                PageSize = _genesysMonitor.CurrentValue.PageSize,
                IncludeInactive = _genesysMonitor.CurrentValue.IncludeInactive,
                MaxRequestsPerSecond = _genesysMonitor.CurrentValue.MaxRequestsPerSecond
            });

            // Preserve cached PKCE tokens that may already exist in persisted settings.
            var existingOAuth = _userSettings.LoadGenesysOAuthSettings();
            _userSettings.SaveGenesysOAuthSettings(new GenesysOAuthOptions
            {
                AuthMode = NormalizeAuthMode(AuthMode),
                ClientId = ClientId.Trim(),
                ClientSecret = ClientSecret.Trim(),
                PkceClientId = PkceClientId.Trim(),
                PkceRedirectUri = string.IsNullOrWhiteSpace(PkceRedirectUri)
                    ? "http://127.0.0.1:45731/callback"
                    : PkceRedirectUri.Trim(),
                PkceScope = string.IsNullOrWhiteSpace(PkceScope)
                    ? GenesysOAuthOptions.DefaultPkceScope
                    : PkceScope.Trim(),
                PkceAccessToken = existingOAuth.PkceAccessToken,
                PkceRefreshToken = existingOAuth.PkceRefreshToken,
                PkceAccessTokenExpiresAtUtc = existingOAuth.PkceAccessTokenExpiresAtUtc
            });

            _userSettings.SaveGitHubSettings(new GitHubOptions
            {
                Token = GitHubToken.Trim(),
                Owner = GitHubOwner.Trim(),
                Repository = GitHubRepository.Trim(),
                Branch = string.IsNullOrWhiteSpace(GitHubBranch) ? "main" : GitHubBranch.Trim(),
                FolderPath = string.IsNullOrWhiteSpace(GitHubFolderPath) ? "audit-reports" : GitHubFolderPath.Trim(),
                CreateDraftPr = CreateDraftPr,
                PrBranchPrefix = string.IsNullOrWhiteSpace(PrBranchPrefix) ? "audit/" : PrBranchPrefix.Trim(),
                CommitMessage = _gitHubMonitor.CurrentValue.CommitMessage
            });

            var elasticOptions = new ElasticExportOptions
            {
                Enabled = ElasticExportEnabled,
                EndpointUri = ElasticEndpointUri.Trim(),
                IndexName = ElasticIndexName.Trim(),
                TokenEnvironmentVariableName = ElasticTokenEnvironmentVariableName.Trim(),
                IncludeRunSummaryDocument = _elasticMonitor.CurrentValue.IncludeRunSummaryDocument,
                BulkBatchSize = _elasticMonitor.CurrentValue.BulkBatchSize
            };

            if ((ElasticExportEnabled || HasElasticConfigInput()) &&
                !elasticOptions.TryValidate(out var elasticValidationError))
            {
                throw new InvalidOperationException(elasticValidationError);
            }

            _userSettings.SaveElasticExportSettings(elasticOptions);

            StatusMessage = "Settings saved. Changes take effect immediately.";
            OnPropertyChanged(nameof(IsConfigured));
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to save settings: {ex.Message}";
        }
    }

    private async void StartPkceAuthAsync()
    {
        if (IsAuthenticatingPkce)
            return;

        try
        {
            HasError = false;
            IsAuthenticatingPkce = true;

            // Save first so auth service reads the latest values from IOptionsMonitor.
            Save();
            if (HasError)
                return;

            StatusMessage = "Opening browser for Genesys Cloud sign-in...";
            var result = await _pkceAuthService.AuthenticateAsync(CancellationToken.None).ConfigureAwait(true);
            if (!result.Success)
            {
                HasError = true;
                StatusMessage = result.Message;
                return;
            }

            var existingOAuth = _userSettings.LoadGenesysOAuthSettings();
            _userSettings.SaveGenesysOAuthSettings(new GenesysOAuthOptions
            {
                AuthMode = NormalizeAuthMode(AuthMode),
                ClientId = existingOAuth.ClientId,
                ClientSecret = existingOAuth.ClientSecret,
                PkceClientId = string.IsNullOrWhiteSpace(PkceClientId) ? existingOAuth.PkceClientId : PkceClientId.Trim(),
                PkceRedirectUri = string.IsNullOrWhiteSpace(PkceRedirectUri) ? existingOAuth.PkceRedirectUri : PkceRedirectUri.Trim(),
                PkceScope = string.IsNullOrWhiteSpace(PkceScope)
                    ? (string.IsNullOrWhiteSpace(existingOAuth.PkceScope) ? GenesysOAuthOptions.DefaultPkceScope : existingOAuth.PkceScope)
                    : PkceScope.Trim(),
                PkceAccessToken = result.AccessToken,
                PkceRefreshToken = result.RefreshToken,
                PkceAccessTokenExpiresAtUtc = result.AccessTokenExpiresAtUtc
            });

            PkceAccessTokenExpiresAtUtc = result.AccessTokenExpiresAtUtc;
            StatusMessage = $"PKCE authentication succeeded. Token expires at {result.AccessTokenExpiresAtUtc:u}.";
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"PKCE authentication failed: {ex.Message}";
        }
        finally
        {
            IsAuthenticatingPkce = false;
        }
    }

    private void CheckPkcePortAvailability()
    {
        HasError = false;
        var available = _pkceAuthService.IsRedirectPortAvailable(PkceRedirectUri, out var message);
        HasError = !available;
        StatusMessage = message;
    }

    private void LoadFromOptions()
    {
        var savedRegion = _userSettings.LoadGenesysSettings();
        var fallbackRegion = _genesysMonitor.CurrentValue;

        GenesysRegion = Coalesce(savedRegion.Region, fallbackRegion.Region, "usw2.pure.cloud");

        var savedOAuth = _userSettings.LoadGenesysOAuthSettings();
        var fallbackOAuth = _oauthMonitor.CurrentValue;

        AuthMode = NormalizeAuthMode(Coalesce(savedOAuth.AuthMode, fallbackOAuth.AuthMode, "auto"));
        ClientId = Coalesce(savedOAuth.ClientId, fallbackOAuth.ClientId);
        ClientSecret = Coalesce(savedOAuth.ClientSecret, fallbackOAuth.ClientSecret);
        PkceClientId = Coalesce(savedOAuth.PkceClientId, fallbackOAuth.PkceClientId, ClientId);
        PkceRedirectUri = Coalesce(savedOAuth.PkceRedirectUri, fallbackOAuth.PkceRedirectUri, "http://127.0.0.1:45731/callback");
        PkceScope = Coalesce(savedOAuth.PkceScope, fallbackOAuth.PkceScope, GenesysOAuthOptions.DefaultPkceScope);
        PkceAccessTokenExpiresAtUtc = savedOAuth.PkceAccessTokenExpiresAtUtc ?? fallbackOAuth.PkceAccessTokenExpiresAtUtc;

        var savedGitHub = _userSettings.LoadGitHubSettings();
        var fallbackGitHub = _gitHubMonitor.CurrentValue;

        GitHubToken = Coalesce(savedGitHub.Token, fallbackGitHub.Token);
        GitHubOwner = Coalesce(savedGitHub.Owner, fallbackGitHub.Owner);
        GitHubRepository = Coalesce(savedGitHub.Repository, fallbackGitHub.Repository);
        GitHubBranch = Coalesce(savedGitHub.Branch, fallbackGitHub.Branch, "main");
        GitHubFolderPath = Coalesce(savedGitHub.FolderPath, fallbackGitHub.FolderPath, "audit-reports");
        CreateDraftPr = savedGitHub.CreateDraftPr || fallbackGitHub.CreateDraftPr;
        PrBranchPrefix = Coalesce(savedGitHub.PrBranchPrefix, fallbackGitHub.PrBranchPrefix, "audit/");

        var savedElastic = _userSettings.LoadElasticExportSettings();
        var fallbackElastic = _elasticMonitor.CurrentValue;

        ElasticExportEnabled = savedElastic.Enabled || fallbackElastic.Enabled;
        ElasticEndpointUri = Coalesce(savedElastic.EndpointUri, fallbackElastic.EndpointUri);
        ElasticIndexName = Coalesce(savedElastic.IndexName, fallbackElastic.IndexName, "genesys-audit-findings");
        ElasticTokenEnvironmentVariableName = Coalesce(savedElastic.TokenEnvironmentVariableName, fallbackElastic.TokenEnvironmentVariableName, "GENESYS_AUDIT_ELASTIC_TOKEN");
    }

    private static string NormalizeAuthMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "auto";

        return mode.Trim().ToLowerInvariant() switch
        {
            "pkce" => "pkce",
            "clientcredentials" => "client_credentials",
            "client_credentials" => "client_credentials",
            _ => "auto"
        };
    }

    private static string Coalesce(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v!;
        return string.Empty;
    }

    private bool HasElasticConfigInput()
        => !string.IsNullOrWhiteSpace(ElasticEndpointUri);

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
