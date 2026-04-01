using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.BestPractices;
using GenesysExtensionAudit.Infrastructure.Application;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Configuration;
using GenesysExtensionAudit.Infrastructure.Reporting;
using GenesysExtensionAudit.Services;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace GenesysExtensionAudit.ViewModels;

/// <summary>
/// ViewModel for running an audit.
/// Inputs:  PageSize, IncludeInactive, StaleFlowDays, InactiveUserDays
/// Controls: Start / Cancel
/// Feedback: Progress (percent + message), Error surface, last exported file path
/// </summary>
public sealed class RunAuditViewModel : INotifyPropertyChanged
{
    private const string AllCatalogEntitiesOption = "(All Catalog Entities)";
    private const string AllActionsOption = "(All Actions)";
    private const string AllEntityTypesOption = "(All Entity Types)";
    private const string ConsolidatedExportMode = "Consolidated";
    private const string SeparateExportMode = "Separate";

    private readonly IAuditOrchestrator _orchestrator;
    private readonly IExcelReportService _excelService;
    private readonly ICareEvidenceExportService _careEvidenceExportService;
    private readonly ICareEvidenceArtifactService _careEvidenceArtifactService;
    private readonly IElasticAuditExportService _elasticAuditExportService;
    private readonly IAuditSnapshotService _snapshotService;
    private readonly IBestPracticesContentService _bestPracticesContentService;
    private readonly IAuditLogCatalogCache _auditLogCatalogCache;
    private readonly IGitHubUploadService _gitHubUploadService;
    private readonly IOptionsMonitor<GitHubOptions> _gitHubOptions;
    private readonly IOptionsMonitor<ElasticExportOptions> _elasticOptions;
    private readonly ObservableCollection<string> _auditLogEntities = [];
    private readonly ObservableCollection<string> _auditLogActions = [];
    private readonly ObservableCollection<string> _auditLogEntityTypes = [];
    private readonly ObservableCollection<string> _auditLogSortOrders = ["Descending", "Ascending"];
    private readonly ObservableCollection<RunSummaryRow> _lastRunSummary = [];
    private readonly ObservableCollection<BestPracticeGuidanceFinding> _bestPracticeGuidance = [];
    private readonly ObservableCollection<string> _workbookExportModes = [ConsolidatedExportMode, SeparateExportMode];
    private readonly List<string> _progressConsoleLines = [];

    private int _pageSize = 100;
    private bool _includeInactive;
    private int _staleFlowDays = 90;
    private int _inactiveUserDays = 90;
    private bool _runExtensionAudit = true;
    private bool _runGroupAudit = true;
    private bool _runQueueAudit = true;
    private bool _runFlowAudit = true;
    private bool _runInactiveUserAudit = true;
    private bool _runDidAudit = true;
    private bool _runAuditLogs;
    private bool _runOperationalEventLogs;
    private int _operationalEventLookbackDays = 7;
    private bool _runOutboundEvents;
    private bool _runUserTelephonyAudit = true;
    private bool _runQueueServiceabilityAudit = true;
    private bool _runFlowDependencyAudit = true;
    private bool _runSiteTopologyAudit = true;
    private bool _runStaleLicenseAudit = true;
    private bool _runLicenseOverProvisioningAudit = true;
    private bool _runRoleGroupOverlapAudit = true;
    private bool _runPromptHygieneAudit = true;
    private bool _runChangeAdjacencyAudit = true;
    private bool _runFlappingDetectionAudit = true;
    private bool _runHotSpotAudit = true;
    private bool _isLoadingAuditLogEntities;
    private bool _auditLogEntitiesLoaded;
    private string _selectedAuditLogEntity = AllCatalogEntitiesOption;
    private int _auditLogLookbackHours = 24;
    private string _selectedAuditLogAction = AllActionsOption;
    private string _selectedAuditLogEntityType = AllEntityTypesOption;
    private string _auditLogUserIdFilter = string.Empty;
    private string _auditLogEntityIdFilter = string.Empty;
    private string _selectedAuditLogSortOrder = "Descending";
    private string _selectedWorkbookExportMode = ConsolidatedExportMode;
    private bool _pushToGitHub;
    private bool _pushToElasticSearch;
    private string _bestPracticesStatusSummary = "Best-practices content has not been evaluated yet.";
    private string _bestPracticesStatusDetails = string.Empty;
    private string _unmappedBestPracticeFindingTypesSummary = string.Empty;
    private bool _isRunning;
    private int _progressPercent;
    private string _progressMessage = string.Empty;
    private string _progressConsoleText = string.Empty;
    private string _statusMessage = "Ready.";
    private string? _errorMessage;
    private string? _lastExportPath;
    private AuditReportData? _lastReport;
    private CancellationTokenSource? _cts;

    public RunAuditViewModel(
        IAuditOrchestrator orchestrator,
        IExcelReportService excelService,
        ICareEvidenceExportService careEvidenceExportService,
        ICareEvidenceArtifactService careEvidenceArtifactService,
        IElasticAuditExportService elasticAuditExportService,
        IAuditSnapshotService snapshotService,
        IBestPracticesContentService bestPracticesContentService,
        IAuditLogCatalogCache auditLogCatalogCache,
        IGitHubUploadService gitHubUploadService,
        IOptionsMonitor<GitHubOptions> gitHubOptions,
        IOptionsMonitor<ElasticExportOptions> elasticOptions)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _excelService = excelService ?? throw new ArgumentNullException(nameof(excelService));
        _careEvidenceExportService = careEvidenceExportService ?? throw new ArgumentNullException(nameof(careEvidenceExportService));
        _careEvidenceArtifactService = careEvidenceArtifactService ?? throw new ArgumentNullException(nameof(careEvidenceArtifactService));
        _elasticAuditExportService = elasticAuditExportService ?? throw new ArgumentNullException(nameof(elasticAuditExportService));
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _bestPracticesContentService = bestPracticesContentService ?? throw new ArgumentNullException(nameof(bestPracticesContentService));
        _auditLogCatalogCache = auditLogCatalogCache ?? throw new ArgumentNullException(nameof(auditLogCatalogCache));
        _gitHubUploadService = gitHubUploadService ?? throw new ArgumentNullException(nameof(gitHubUploadService));
        _gitHubOptions = gitHubOptions ?? throw new ArgumentNullException(nameof(gitHubOptions));
        _elasticOptions = elasticOptions ?? throw new ArgumentNullException(nameof(elasticOptions));
        _pushToElasticSearch = _elasticOptions.CurrentValue.Enabled;

        // Refresh IsGitHubConfigured binding when settings change (e.g. after saving in Settings tab).
        _gitHubOptions.OnChange(_ => OnPropertyChanged(nameof(IsGitHubConfigured)));
        _elasticOptions.OnChange(_ => OnPropertyChanged(nameof(IsElasticConfigured)));

        StartCommand = new RelayCommand(StartAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        RefreshAuditCatalogCommand = new RelayCommand(RefreshAuditCatalog, () => !IsRunning && !IsLoadingAuditLogEntities);
        ExportCommand = new RelayCommand(ExportLastReport, () => !IsRunning && _lastReport is not null);

        _auditLogEntities.Add(AllCatalogEntitiesOption);
        RefreshBestPracticesStatus();
        LoadAuditCatalog(forceRefresh: false, suppressErrors: true, updateStatus: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Page size used when calling the Genesys Cloud paginated endpoints.
    /// Valid range: 1–500.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            var v = Math.Clamp(value, 1, 500);
            SetField(ref _pageSize, v);
        }
    }

    public bool IncludeInactive
    {
        get => _includeInactive;
        set => SetField(ref _includeInactive, value);
    }

    public int StaleFlowDays
    {
        get => _staleFlowDays;
        set => SetField(ref _staleFlowDays, Math.Max(1, value));
    }

    public int InactiveUserDays
    {
        get => _inactiveUserDays;
        set => SetField(ref _inactiveUserDays, Math.Max(1, value));
    }

    public bool RunExtensionAudit
    {
        get => _runExtensionAudit;
        set
        {
            if (SetField(ref _runExtensionAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunGroupAudit
    {
        get => _runGroupAudit;
        set
        {
            if (SetField(ref _runGroupAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunQueueAudit
    {
        get => _runQueueAudit;
        set
        {
            if (SetField(ref _runQueueAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunFlowAudit
    {
        get => _runFlowAudit;
        set
        {
            if (SetField(ref _runFlowAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunInactiveUserAudit
    {
        get => _runInactiveUserAudit;
        set
        {
            if (SetField(ref _runInactiveUserAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunDidAudit
    {
        get => _runDidAudit;
        set
        {
            if (SetField(ref _runDidAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunAuditLogs
    {
        get => _runAuditLogs;
        set
        {
            if (SetField(ref _runAuditLogs, value))
            {
                if (value && !_auditLogEntitiesLoaded)
                    LoadAuditCatalog(forceRefresh: false, suppressErrors: false, updateStatus: true);
                OnAuditSelectionChanged();
            }
        }
    }

    public bool RunOperationalEventLogs
    {
        get => _runOperationalEventLogs;
        set
        {
            if (SetField(ref _runOperationalEventLogs, value))
                OnAuditSelectionChanged();
        }
    }

    public int OperationalEventLookbackDays
    {
        get => _operationalEventLookbackDays;
        set => SetField(ref _operationalEventLookbackDays, Math.Max(1, value));
    }

    public bool RunOutboundEvents
    {
        get => _runOutboundEvents;
        set
        {
            if (SetField(ref _runOutboundEvents, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunUserTelephonyAudit
    {
        get => _runUserTelephonyAudit;
        set
        {
            if (SetField(ref _runUserTelephonyAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunQueueServiceabilityAudit
    {
        get => _runQueueServiceabilityAudit;
        set
        {
            if (SetField(ref _runQueueServiceabilityAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunFlowDependencyAudit
    {
        get => _runFlowDependencyAudit;
        set
        {
            if (SetField(ref _runFlowDependencyAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunSiteTopologyAudit
    {
        get => _runSiteTopologyAudit;
        set
        {
            if (SetField(ref _runSiteTopologyAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunStaleLicenseAudit
    {
        get => _runStaleLicenseAudit;
        set
        {
            if (SetField(ref _runStaleLicenseAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunLicenseOverProvisioningAudit
    {
        get => _runLicenseOverProvisioningAudit;
        set
        {
            if (SetField(ref _runLicenseOverProvisioningAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunRoleGroupOverlapAudit
    {
        get => _runRoleGroupOverlapAudit;
        set
        {
            if (SetField(ref _runRoleGroupOverlapAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunPromptHygieneAudit
    {
        get => _runPromptHygieneAudit;
        set
        {
            if (SetField(ref _runPromptHygieneAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunChangeAdjacencyAudit
    {
        get => _runChangeAdjacencyAudit;
        set
        {
            if (SetField(ref _runChangeAdjacencyAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunFlappingDetectionAudit
    {
        get => _runFlappingDetectionAudit;
        set
        {
            if (SetField(ref _runFlappingDetectionAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public bool RunHotSpotAudit
    {
        get => _runHotSpotAudit;
        set
        {
            if (SetField(ref _runHotSpotAudit, value))
                OnAuditSelectionChanged();
        }
    }

    public ObservableCollection<string> AuditLogEntities => _auditLogEntities;
    public ObservableCollection<string> WorkbookExportModes => _workbookExportModes;

    public string SelectedAuditLogEntity
    {
        get => _selectedAuditLogEntity;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? AllCatalogEntitiesOption : value;
            if (SetField(ref _selectedAuditLogEntity, normalized))
                PopulateServiceFilterDropdowns(normalized);
        }
    }

    /// <summary>Lookback window for audit-log queries. Range: 1–720 hours (30 days).</summary>
    public int AuditLogLookbackHours
    {
        get => _auditLogLookbackHours;
        set => SetField(ref _auditLogLookbackHours, Math.Clamp(value, 1, 720));
    }

    public ObservableCollection<string> AuditLogActions => _auditLogActions;
    public ObservableCollection<string> AuditLogEntityTypes => _auditLogEntityTypes;
    public ObservableCollection<string> AuditLogSortOrders => _auditLogSortOrders;

    public string SelectedAuditLogAction
    {
        get => _selectedAuditLogAction;
        set => SetField(ref _selectedAuditLogAction, string.IsNullOrWhiteSpace(value) ? AllActionsOption : value);
    }

    public string SelectedAuditLogEntityType
    {
        get => _selectedAuditLogEntityType;
        set => SetField(ref _selectedAuditLogEntityType, string.IsNullOrWhiteSpace(value) ? AllEntityTypesOption : value);
    }

    /// <summary>Optional user-ID filter (GUID). Leave blank to match all users.</summary>
    public string AuditLogUserIdFilter
    {
        get => _auditLogUserIdFilter;
        set => SetField(ref _auditLogUserIdFilter, value ?? string.Empty);
    }

    /// <summary>Optional entity-ID filter (GUID). Leave blank to match all entities.</summary>
    public string AuditLogEntityIdFilter
    {
        get => _auditLogEntityIdFilter;
        set => SetField(ref _auditLogEntityIdFilter, value ?? string.Empty);
    }

    /// <summary>"Descending" (newest first) or "Ascending" (oldest first).</summary>
    public string SelectedAuditLogSortOrder
    {
        get => _selectedAuditLogSortOrder;
        set => SetField(ref _selectedAuditLogSortOrder, value ?? "Descending");
    }

    public string SelectedWorkbookExportMode
    {
        get => _selectedWorkbookExportMode;
        set
        {
            var normalized = string.Equals(value, SeparateExportMode, StringComparison.OrdinalIgnoreCase)
                ? SeparateExportMode
                : ConsolidatedExportMode;
            SetField(ref _selectedWorkbookExportMode, normalized);
        }
    }

    /// <summary>
    /// When true (and GitHub is configured), the generated report is pushed to the
    /// configured GitHub repository after being saved locally.
    /// </summary>
    public bool PushToGitHub
    {
        get => _pushToGitHub;
        set => SetField(ref _pushToGitHub, value);
    }

    public bool PushToElasticSearch
    {
        get => _pushToElasticSearch;
        set => SetField(ref _pushToElasticSearch, value);
    }

    /// <summary>True when GitHub credentials are configured (appsettings.json or Settings tab).</summary>
    public bool IsGitHubConfigured => _gitHubOptions.CurrentValue.IsConfigured;
    public bool IsElasticConfigured => _elasticOptions.CurrentValue.IsConfigured;

    public bool IsLoadingAuditLogEntities
    {
        get => _isLoadingAuditLogEntities;
        private set
        {
            if (SetField(ref _isLoadingAuditLogEntities, value))
                RaiseCommandCanExecuteChanged();
        }
    }

    public bool IsAuditLogSelectionEnabled => RunAuditLogs && !IsLoadingAuditLogEntities;

    public bool SelectAllAudits
    {
        get =>
            RunExtensionAudit &&
            RunGroupAudit &&
            RunQueueAudit &&
            RunFlowAudit &&
            RunInactiveUserAudit &&
            RunDidAudit &&
            RunAuditLogs &&
            RunOperationalEventLogs &&
            RunOutboundEvents &&
            RunUserTelephonyAudit &&
            RunQueueServiceabilityAudit &&
            RunFlowDependencyAudit &&
            RunSiteTopologyAudit &&
            RunStaleLicenseAudit &&
            RunLicenseOverProvisioningAudit &&
            RunRoleGroupOverlapAudit &&
            RunPromptHygieneAudit &&
            RunChangeAdjacencyAudit &&
            RunFlappingDetectionAudit &&
            RunHotSpotAudit;
        set
        {
            RunExtensionAudit = value;
            RunGroupAudit = value;
            RunQueueAudit = value;
            RunFlowAudit = value;
            RunInactiveUserAudit = value;
            RunDidAudit = value;
            RunAuditLogs = value;
            RunOperationalEventLogs = value;
            RunOutboundEvents = value;
            RunUserTelephonyAudit = value;
            RunQueueServiceabilityAudit = value;
            RunFlowDependencyAudit = value;
            RunSiteTopologyAudit = value;
            RunStaleLicenseAudit = value;
            RunLicenseOverProvisioningAudit = value;
            RunRoleGroupOverlapAudit = value;
            RunPromptHygieneAudit = value;
            RunChangeAdjacencyAudit = value;
            RunFlappingDetectionAudit = value;
            RunHotSpotAudit = value;
        }
    }

    public bool HasAnyAuditSelected =>
        RunExtensionAudit || RunGroupAudit || RunQueueAudit || RunFlowAudit || RunInactiveUserAudit || RunDidAudit ||
        RunAuditLogs || RunOperationalEventLogs || RunOutboundEvents || RunUserTelephonyAudit || RunQueueServiceabilityAudit ||
        RunFlowDependencyAudit || RunSiteTopologyAudit || RunStaleLicenseAudit || RunLicenseOverProvisioningAudit ||
        RunRoleGroupOverlapAudit || RunPromptHygieneAudit || RunChangeAdjacencyAudit || RunFlappingDetectionAudit || RunHotSpotAudit;

    public string? LastExportPath
    {
        get => _lastExportPath;
        private set => SetField(ref _lastExportPath, value);
    }

    public bool HasExport => !string.IsNullOrWhiteSpace(LastExportPath);

    public bool HasReport => _lastReport is not null;
    public ObservableCollection<RunSummaryRow> LastRunSummary => _lastRunSummary;
    public ObservableCollection<BestPracticeGuidanceFinding> BestPracticeGuidance => _bestPracticeGuidance;
    public bool HasBestPracticeGuidance => _bestPracticeGuidance.Count > 0;

    public string BestPracticesStatusSummary
    {
        get => _bestPracticesStatusSummary;
        private set => SetField(ref _bestPracticesStatusSummary, value);
    }

    public string BestPracticesStatusDetails
    {
        get => _bestPracticesStatusDetails;
        private set => SetField(ref _bestPracticesStatusDetails, value);
    }

    public string UnmappedBestPracticeFindingTypesSummary
    {
        get => _unmappedBestPracticeFindingTypesSummary;
        private set => SetField(ref _unmappedBestPracticeFindingTypesSummary, value);
    }

    public bool HasUnmappedBestPracticeFindingTypes => !string.IsNullOrWhiteSpace(UnmappedBestPracticeFindingTypesSummary);

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                RaiseCommandCanExecuteChanged();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool CanStart => !IsRunning && HasAnyAuditSelected;
    public bool CanCancel => IsRunning;

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetField(ref _progressMessage, value);
    }

    public string ProgressConsoleText
    {
        get => _progressConsoleText;
        private set
        {
            if (SetField(ref _progressConsoleText, value))
                OnPropertyChanged(nameof(HasProgressConsoleOutput));
        }
    }

    public bool HasProgressConsoleOutput => !string.IsNullOrWhiteSpace(ProgressConsoleText);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshAuditCatalogCommand { get; }
    public ICommand ExportCommand { get; }

    private async void StartAsync()
    {
        if (IsRunning) return;

        ErrorMessage = null;
        ProgressPercent = 0;
        ProgressMessage = string.Empty;
        ClearProgressConsole();

        if (!HasAnyAuditSelected)
        {
            ErrorMessage = "Select at least one audit path.";
            StatusMessage = "No audit paths selected.";
            return;
        }

        IsRunning = true;
        StatusMessage = "Starting audit...";
        AppendProgressLine("Starting audit run.");
        AppendProgressLine($"Selected audits: {string.Join(", ", GetSelectedAuditNames())}");
        if (RunAuditLogs)
            AppendProgressLine(BuildAuditLogQuerySummary());

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var progress = new Progress<AuditProgress>(p =>
        {
            try
            {
                if (p.Percent is >= 0 and <= 100)
                    ProgressPercent = p.Percent;

                if (!string.IsNullOrWhiteSpace(p.Message))
                {
                    ProgressMessage = p.Message;
                    AppendProgressLine(p.Message);
                }

                if (!string.IsNullOrWhiteSpace(p.Status))
                {
                    StatusMessage = p.Status;
                    AppendProgressLine($"Status: {p.Status}");
                }
            }
            catch
            {
                // ignore progress update failures
            }
        });

        try
        {
            StatusMessage = "Running audit...";
            var report = await _orchestrator.RunAsync(new AuditRunOptions
            {
                PageSize = PageSize,
                IncludeInactiveUsers = IncludeInactive,
                StaleFlowThresholdDays = StaleFlowDays,
                InactiveUserThresholdDays = InactiveUserDays,
                RunExtensionAudit = RunExtensionAudit,
                RunGroupAudit = RunGroupAudit,
                RunQueueAudit = RunQueueAudit,
                RunFlowAudit = RunFlowAudit,
                RunInactiveUserAudit = RunInactiveUserAudit,
                RunDidAudit = RunDidAudit,
                RunUserTelephonyAudit = RunUserTelephonyAudit,
                RunQueueServiceabilityAudit = RunQueueServiceabilityAudit,
                RunFlowDependencyAudit = RunFlowDependencyAudit,
                RunAuditLogs = RunAuditLogs,
                AuditLogLookbackHours = AuditLogLookbackHours,
                AuditLogServiceNames = GetSelectedAuditLogServiceNames(),
                AuditLogFilters = BuildAuditLogFilters(),
                AuditLogSortField = "dateIssued",
                AuditLogSortOrder = string.Equals(SelectedAuditLogSortOrder, "Ascending", StringComparison.Ordinal) ? "ASC" : "DESC",
                RunOperationalEventLogs = RunOperationalEventLogs,
                OperationalEventLookbackDays = OperationalEventLookbackDays,
                RunOutboundEvents = RunOutboundEvents,
                RunStaleLicenseAudit = RunStaleLicenseAudit,
                RunLicenseOverProvisioningAudit = RunLicenseOverProvisioningAudit,
                RunRoleGroupOverlapAudit = RunRoleGroupOverlapAudit,
                RunSiteTopologyAudit = RunSiteTopologyAudit,
                RunPromptHygieneAudit = RunPromptHygieneAudit,
                RunChangeAdjacencyAudit = RunChangeAdjacencyAudit,
                RunFlappingDetectionAudit = RunFlappingDetectionAudit,
                RunHotSpotAudit = RunHotSpotAudit
            }, progress, ct).ConfigureAwait(true);

            _lastReport = report;
            BuildBestPracticeGuidance(report);
            BuildLastRunSummary(report);
            OnPropertyChanged(nameof(HasReport));
            RaiseCommandCanExecuteChanged();

            ProgressMessage = "Generating Excel report...";
            await SaveReportToFileAsync(report, ct).ConfigureAwait(true);

            ProgressPercent = 100;
            ProgressMessage = "Completed.";
            StatusMessage = "Completed.";
            AppendProgressLine("Audit run completed.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Audit cancelled.";
            ProgressMessage = "Cancelled.";
            AppendProgressLine("Audit run cancelled.");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Audit failed.";
            AppendProgressLine($"ERROR: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
            StatusMessage = "Cancelling...";
            AppendProgressLine("Cancellation requested.");
        }
        catch
        {
            // ignore
        }
    }

    private void RaiseCommandCanExecuteChanged()
    {
        if (StartCommand is RelayCommand s) s.RaiseCanExecuteChanged();
        if (CancelCommand is RelayCommand c) c.RaiseCanExecuteChanged();
        if (ExportCommand is RelayCommand e) e.RaiseCanExecuteChanged();
    }

    private void OnAuditSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectAllAudits));
        OnPropertyChanged(nameof(HasAnyAuditSelected));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(IsAuditLogSelectionEnabled));
        RaiseCommandCanExecuteChanged();
    }

    private IReadOnlyList<string> GetSelectedAuditLogServiceNames()
    {
        if (!RunAuditLogs)
            return [];

        if (string.IsNullOrWhiteSpace(SelectedAuditLogEntity) ||
            string.Equals(SelectedAuditLogEntity, AllCatalogEntitiesOption, StringComparison.Ordinal))
        {
            return [];
        }

        return [SelectedAuditLogEntity];
    }

    /// <summary>
    /// Builds the server-side filter list from the current UI selections.
    /// Only non-empty / non-"All" selections are included.
    /// </summary>
    private IReadOnlyList<AuditLogFilter> BuildAuditLogFilters()
    {
        if (!RunAuditLogs)
            return [];

        var filters = new List<AuditLogFilter>();

        if (!string.IsNullOrWhiteSpace(SelectedAuditLogAction) &&
            !string.Equals(SelectedAuditLogAction, AllActionsOption, StringComparison.Ordinal))
        {
            filters.Add(new AuditLogFilter("action", SelectedAuditLogAction));
        }

        if (!string.IsNullOrWhiteSpace(SelectedAuditLogEntityType) &&
            !string.Equals(SelectedAuditLogEntityType, AllEntityTypesOption, StringComparison.Ordinal))
        {
            filters.Add(new AuditLogFilter("entityType", SelectedAuditLogEntityType));
        }

        if (!string.IsNullOrWhiteSpace(AuditLogUserIdFilter))
            filters.Add(new AuditLogFilter("userId", AuditLogUserIdFilter.Trim()));

        if (!string.IsNullOrWhiteSpace(AuditLogEntityIdFilter))
            filters.Add(new AuditLogFilter("entityId", AuditLogEntityIdFilter.Trim()));

        return filters;
    }

    /// <summary>
    /// Repopulates the Action and Entity Type filter dropdowns based on the selected service.
    /// When "All Catalog Entities" is selected, the dropdowns are cleared.
    /// </summary>
    private void PopulateServiceFilterDropdowns(string selectedService)
    {
        _auditLogActions.Clear();
        _auditLogEntityTypes.Clear();
        SelectedAuditLogAction = AllActionsOption;
        SelectedAuditLogEntityType = AllEntityTypesOption;

        if (string.Equals(selectedService, AllCatalogEntitiesOption, StringComparison.Ordinal))
            return;

        // Find the service in the loaded catalog (if available).
        // We access the cache synchronously here — the catalog is already loaded at this point.
        // If not yet loaded, the dropdowns remain empty (acceptable UX: user can refresh).
        _auditLogActions.Add(AllActionsOption);
        _auditLogEntityTypes.Add(AllEntityTypesOption);

        // The catalog is populated asynchronously; find the matching service.
        // We need access to the cached data synchronously. Since the catalog is already loaded
        // by the time the user changes the selection, we call GetOrRefreshAsync with no-refresh.
        PopulateDropdownsFromCacheAsync(selectedService);
    }

    private async void PopulateDropdownsFromCacheAsync(string selectedService)
    {
        try
        {
            var catalog = await _auditLogCatalogCache
                .GetOrRefreshCatalogAsync(forceRefresh: false, CancellationToken.None)
                .ConfigureAwait(true);

            var info = catalog.FirstOrDefault(
                s => string.Equals(s.ServiceName, selectedService, StringComparison.OrdinalIgnoreCase));

            _auditLogActions.Clear();
            _auditLogActions.Add(AllActionsOption);
            if (info is not null)
            {
                foreach (var a in info.Actions)
                    _auditLogActions.Add(a);
            }

            _auditLogEntityTypes.Clear();
            _auditLogEntityTypes.Add(AllEntityTypesOption);
            if (info is not null)
            {
                foreach (var et in info.EntityTypes)
                    _auditLogEntityTypes.Add(et);
            }

            SelectedAuditLogAction = AllActionsOption;
            SelectedAuditLogEntityType = AllEntityTypesOption;
        }
        catch
        {
            // Best-effort — leave dropdowns with just the "All" option.
        }
    }

    private void RefreshAuditCatalog()
        => LoadAuditCatalog(forceRefresh: true, suppressErrors: false, updateStatus: true);

    private async void LoadAuditCatalog(bool forceRefresh, bool suppressErrors, bool updateStatus)
    {
        if (IsLoadingAuditLogEntities)
            return;

        IsLoadingAuditLogEntities = true;

        try
        {
            var catalog = await _auditLogCatalogCache
                .GetOrRefreshCatalogAsync(forceRefresh, CancellationToken.None)
                .ConfigureAwait(true);

            ApplyAuditLogCatalog(catalog, preserveSelection: true);
            _auditLogEntitiesLoaded = true;
            if (updateStatus)
                StatusMessage = $"Loaded {_auditLogEntities.Count - 1} audit-log catalog services.";
        }
        catch (Exception ex)
        {
            if (!suppressErrors)
            {
                ErrorMessage = $"Failed to load audit-log catalog: {ex.Message}";
                StatusMessage = "Failed to load audit-log catalog.";
            }
        }
        finally
        {
            IsLoadingAuditLogEntities = false;
        }
    }

    private async void ExportLastReport()
    {
        if (_lastReport is null || IsRunning)
            return;

        try
        {
            await SaveReportToFileAsync(_lastReport, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
            StatusMessage = "Export failed.";
        }
    }

    private async Task SaveReportToFileAsync(AuditReportData report, CancellationToken ct)
    {
        var outputDirectory = SelectOutputDirectory();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            StatusMessage = "Audit complete — export skipped.";
            return;
        }

        Directory.CreateDirectory(outputDirectory);

        const string snapshotPrefix = "GenesysCloudAudit";
        var previousSnapshot = await _snapshotService
            .LoadLatestAsync(outputDirectory, snapshotPrefix, ct)
            .ConfigureAwait(true);
        var snapshotComparison = await Task.Run(
            () => _snapshotService.Compare(report, previousSnapshot.Snapshot),
            ct).ConfigureAwait(true);
        report.FindingLifecycleFindings = snapshotComparison.LifecycleFindings;
        report.FindingLifecycleWasComputed = true;
        report.HistoricalDriftFindings = snapshotComparison.HistoricalDriftFindings;
        report.HistoricalDriftWasComputed = snapshotComparison.HistoricalDriftWasComputed;
        report.PreviousSnapshotGeneratedAtUtc = previousSnapshot.Snapshot?.GeneratedUtc;
        report.PreviousSnapshotPath = previousSnapshot.Path;
        var carePacket = await Task.Run(
            () => _careEvidenceExportService.BuildPacket(report),
            ct).ConfigureAwait(true);

        var datePrefix = DateTime.Now.ToString("yyyy-MM-dd");
        if (string.Equals(SelectedWorkbookExportMode, SeparateExportMode, StringComparison.Ordinal))
        {
            var generatedFiles = new List<string>();
            var auditScopes = BuildSeparateAuditScopes(report);
            for (var index = 0; index < auditScopes.Count; index++)
            {
                ct.ThrowIfCancellationRequested();

                var audit = auditScopes[index];
                StatusMessage = $"Exporting workbook {index + 1}/{auditScopes.Count}: {audit.AuditName}...";
                ProgressMessage = StatusMessage;
                AppendProgressLine(StatusMessage);

                var baseFileName = $"{datePrefix}_GenesysCloudAudit_{audit.AuditName}.xlsx";
                var fullPath = GetNextAvailableFilePath(outputDirectory, baseFileName);
                await _excelService.WriteAsync(fullPath, report, ct, audit.Scope).ConfigureAwait(true);

                generatedFiles.Add(fullPath);
                AppendProgressLine($"Saved {Path.GetFileName(fullPath)}");
            }

            var artifactBaseName = $"{datePrefix}_GenesysCloudAudit_Artifacts";
            var careJsonPath = GetNextAvailableFilePath(outputDirectory, $"{artifactBaseName}.care-evidence.json");
            await WriteArtifactAsync(
                careJsonPath,
                () => _careEvidenceArtifactService.BuildJson(carePacket),
                ct).ConfigureAwait(true);
            generatedFiles.Add(careJsonPath);

            var careHtmlPath = GetNextAvailableFilePath(outputDirectory, $"{artifactBaseName}.care-summary.html");
            await WriteArtifactAsync(
                careHtmlPath,
                () => _careEvidenceArtifactService.BuildHtml(report, carePacket),
                ct).ConfigureAwait(true);
            generatedFiles.Add(careHtmlPath);

            LastExportPath = outputDirectory;
            OnPropertyChanged(nameof(HasExport));
            StatusMessage = $"Saved {generatedFiles.Count} report(s) to {outputDirectory}";

            await TryPushToGitHubAsync(generatedFiles, ct).ConfigureAwait(true);
            await TryExportToElasticAsync(report, carePacket, snapshotComparison.Snapshot, ct).ConfigureAwait(true);
            await _snapshotService
                .SaveSnapshotAsync(snapshotComparison.Snapshot, outputDirectory, snapshotPrefix, ct)
                .ConfigureAwait(true);
            return;
        }

        StatusMessage = "Exporting consolidated workbook...";
        ProgressMessage = StatusMessage;
        AppendProgressLine(StatusMessage);
        var consolidatedBaseName = $"{datePrefix}_GenesysCloudAudit_Full.xlsx";
        var consolidatedPath = GetNextAvailableFilePath(outputDirectory, consolidatedBaseName);
        await _excelService.WriteAsync(consolidatedPath, report, ct, carePacket: carePacket).ConfigureAwait(true);

        var careJsonPathForWorkbook = Path.ChangeExtension(consolidatedPath, ".care-evidence.json");
        await WriteArtifactAsync(
            careJsonPathForWorkbook,
            () => _careEvidenceArtifactService.BuildJson(carePacket),
            ct).ConfigureAwait(true);

        var careHtmlPathForWorkbook = Path.ChangeExtension(consolidatedPath, ".care-summary.html");
        await WriteArtifactAsync(
            careHtmlPathForWorkbook,
            () => _careEvidenceArtifactService.BuildHtml(report, carePacket),
            ct).ConfigureAwait(true);

        LastExportPath = consolidatedPath;
        OnPropertyChanged(nameof(HasExport));
        StatusMessage = $"Saved: {Path.GetFileName(consolidatedPath)}";

        await TryPushToGitHubAsync(
            [
                consolidatedPath,
                careJsonPathForWorkbook,
                careHtmlPathForWorkbook
            ],
            ct).ConfigureAwait(true);
        await TryExportToElasticAsync(report, carePacket, snapshotComparison.Snapshot, ct).ConfigureAwait(true);
        await _snapshotService
            .SaveSnapshotAsync(snapshotComparison.Snapshot, outputDirectory, snapshotPrefix, ct)
            .ConfigureAwait(true);
    }

    private string? SelectOutputDirectory()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select Destination Folder for Audit Reports"
        };

        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    private static string GetNextAvailableFilePath(string directory, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
            return candidate;

        var suffix = 2;
        while (true)
        {
            candidate = Path.Combine(directory, $"{baseName}-{suffix}{ext}");
            if (!File.Exists(candidate))
                return candidate;
            suffix++;
        }
    }

    private IReadOnlyList<(string AuditName, ExcelWorkbookScopeOptions Scope)> BuildSeparateAuditScopes(AuditReportData report)
    {
        var scopes = new List<(string AuditName, ExcelWorkbookScopeOptions Scope)>();
        if (report.Options.RunExtensionAudit)
        {
            scopes.Add(("Extensions", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeExtensions = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunGroupAudit)
        {
            scopes.Add(("Groups", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeGroups = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunQueueAudit)
        {
            scopes.Add(("Queues", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeQueues = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunQueueServiceabilityAudit && !report.Options.RunQueueAudit)
        {
            scopes.Add(("QueueServiceability", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeQueues = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunFlowAudit)
        {
            scopes.Add(("Flows", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeFlows = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunFlowDependencyAudit && !report.Options.RunFlowAudit)
        {
            scopes.Add(("FlowDependency", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeFlows = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunInactiveUserAudit)
        {
            scopes.Add(("InactiveUsers", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeInactiveUsers = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunUserTelephonyAudit && !report.Options.RunExtensionAudit)
        {
            scopes.Add(("UserTelephony", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeExtensions = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunDidAudit)
        {
            scopes.Add(("DIDs", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeDids = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunStaleLicenseAudit)
        {
            scopes.Add(("StaleLicenses", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeStaleLicenses = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunLicenseOverProvisioningAudit)
        {
            scopes.Add(("LicenseOverProvisioning", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeLicenseOverProvisioning = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunRoleGroupOverlapAudit)
        {
            scopes.Add(("RoleGroupOverlap", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeRoleGroupOverlap = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunSiteTopologyAudit)
        {
            scopes.Add(("SiteTopology", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeSiteTopology = true,
                IncludeEdgePerformance = report.Options.RunOperationalEventLogs,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunPromptHygieneAudit)
        {
            scopes.Add(("PromptHygiene", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludePromptHygiene = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunAuditLogs)
        {
            scopes.Add(("AuditLogs", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeAuditLogs = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunChangeAdjacencyAudit && report.Options.RunAuditLogs)
        {
            scopes.Add(("ChangeAdjacency", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeChangeAdjacency = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunFlappingDetectionAudit && report.Options.RunAuditLogs)
        {
            scopes.Add(("FlappingDetection", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeFlappingDetection = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunOperationalEventLogs)
        {
            scopes.Add(("OperationalEvents", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeOperationalEvents = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunOutboundEvents)
        {
            scopes.Add(("OutboundEvents", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeOutboundEvents = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        if (report.Options.RunHotSpotAudit)
        {
            scopes.Add(("HotSpots", new ExcelWorkbookScopeOptions
            {
                IncludeSummary = true,
                IncludeHotSpot = true,
                IncludeBestPracticeGuidance = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        return scopes;
    }

    private static async Task WriteArtifactAsync(string path, Func<byte[]> contentFactory, CancellationToken ct)
    {
        var content = await Task.Run(contentFactory, ct).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, content, ct).ConfigureAwait(false);
    }

    private async Task TryPushToGitHubAsync(IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        if (!PushToGitHub || !_gitHubUploadService.IsConfigured || filePaths.Count == 0)
            return;

        StatusMessage = "Pushing report(s) to GitHub...";
        var pushedCount = 0;
        var lastUrl = string.Empty;
        foreach (var filePath in filePaths)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                var content = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(true);
                lastUrl = await _gitHubUploadService
                    .UploadAsync(fileName, content, ct)
                    .ConfigureAwait(true);
                pushedCount++;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"GitHub push failed for {Path.GetFileName(filePath)}: {ex.Message}";
                StatusMessage = $"Saved locally ({pushedCount}/{filePaths.Count} pushed to GitHub)";
                return;
            }
        }

        StatusMessage = filePaths.Count == 1
            ? $"Saved and pushed to GitHub: {lastUrl}"
            : $"Saved locally and pushed {pushedCount} report(s) to GitHub.";
    }

    private async Task TryExportToElasticAsync(
        AuditReportData report,
        CareEvidencePacket carePacket,
        AuditSnapshotPacket snapshot,
        CancellationToken ct)
    {
        if (!PushToElasticSearch)
            return;

        var elasticResult = await _elasticAuditExportService
            .ExportAsync(report, carePacket, snapshot, ct)
            .ConfigureAwait(true);

        if (elasticResult.Succeeded)
        {
            StatusMessage = $"{StatusMessage} Elastic: {elasticResult.DocumentsSucceeded}/{elasticResult.DocumentsAttempted} document(s) indexed.";
            return;
        }

        ErrorMessage = string.IsNullOrWhiteSpace(elasticResult.ResponseDetails)
            ? elasticResult.Message
            : $"{elasticResult.Message} {elasticResult.ResponseDetails}";
        StatusMessage = $"{StatusMessage} Elastic export failed.";
    }

    private void BuildLastRunSummary(AuditReportData report)
    {
        _lastRunSummary.Clear();

        var ext = report.ExtensionReport;
        var runFlags = new (string Name, bool Ran, int Count)[]
        {
            ("Extensions", report.Options.RunExtensionAudit,
                ext.DuplicateProfileExtensions.Count
                + ext.ProfileExtensionsNotAssigned.Count
                + ext.DuplicateAssignedExtensions.Count
                + ext.AssignedExtensionsMissingFromProfiles.Count
                + ext.InvalidProfileExtensions.Count
                + ext.InvalidAssignedExtensions.Count),
            ("Groups", report.Options.RunGroupAudit, report.GroupFindings.Count),
            ("Queues", report.Options.RunQueueAudit, report.QueueFindings.Count),
            ("Flows", report.Options.RunFlowAudit, report.FlowFindings.Count),
            ("Users with Stale Token", report.Options.RunInactiveUserAudit, report.InactiveUserFindings.Count),
            ("Users Missing Location", report.Options.RunInactiveUserAudit, report.NoLocationUserFindings.Count),
            ("DIDs", report.Options.RunDidAudit, report.DidFindings.Count),
            ("Audit Logs", report.Options.RunAuditLogs, report.AuditLogFindings.Count),
            ("Audit Log Signals", report.Options.RunAuditLogs, report.AuditLogSignalFindings.Count),
            ("Operational Event Logs", report.Options.RunOperationalEventLogs, report.OperationalEventFindings.Count),
            ("Edge Performance", report.Options.RunSiteTopologyAudit && report.Options.RunOperationalEventLogs, report.EdgePerformanceObservations.Count(o => o.IsAnomalous)),
            ("OutboundEvents", report.Options.RunOutboundEvents, report.OutboundEventFindings.Count),
            ("Best Practice Guidance", report.BestPracticeGuidanceWasComputed, report.BestPracticeGuidanceFindings.Count),
            ("Finding Lifecycle", report.FindingLifecycleWasComputed, report.FindingLifecycleFindings.Count),
            ("Historical Drift", report.HistoricalDriftWasComputed, report.HistoricalDriftFindings.Count)
        };

        foreach (var item in runFlags)
        {
            if (!item.Ran)
                continue;

            _lastRunSummary.Add(new RunSummaryRow(item.Name, item.Count));
        }
    }

    private void BuildBestPracticeGuidance(AuditReportData report)
    {
        _bestPracticeGuidance.Clear();
        foreach (var guidance in report.BestPracticeGuidanceFindings.Take(10))
            _bestPracticeGuidance.Add(guidance);

        UnmappedBestPracticeFindingTypesSummary = report.UnmappedBestPracticeFindingTypes.Count == 0
            ? string.Empty
            : $"Unmapped finding types: {string.Join(", ", report.UnmappedBestPracticeFindingTypes)}";

        RefreshBestPracticesStatus();
        OnPropertyChanged(nameof(HasBestPracticeGuidance));
        OnPropertyChanged(nameof(HasUnmappedBestPracticeFindingTypes));
    }

    private void RefreshBestPracticesStatus()
    {
        var status = _bestPracticesContentService.GetStatus();
        BestPracticesStatusSummary = status.Summary;
        BestPracticesStatusDetails = status.Messages.Count == 0
            ? "Catalog, mapping, and glossary content loaded successfully."
            : string.Join(Environment.NewLine, status.Messages.Take(4));
    }

    private void ApplyAuditLogCatalog(IReadOnlyList<AuditServiceInfo> catalog, bool preserveSelection)
    {
        var previousSelection = SelectedAuditLogEntity;

        _auditLogEntities.Clear();
        _auditLogEntities.Add(AllCatalogEntitiesOption);
        foreach (var service in catalog)
            _auditLogEntities.Add(service.ServiceName);

        var nextSelection = preserveSelection &&
                            !string.IsNullOrWhiteSpace(previousSelection) &&
                            _auditLogEntities.Contains(previousSelection)
            ? previousSelection
            : AllCatalogEntitiesOption;

        SelectedAuditLogEntity = nextSelection;
    }

    private void ClearProgressConsole()
    {
        _progressConsoleLines.Clear();
        ProgressConsoleText = string.Empty;
    }

    private void AppendProgressLine(string message)
    {
        var trimmed = message?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        _progressConsoleLines.Add($"[{DateTime.Now:HH:mm:ss}] {trimmed}");
        if (_progressConsoleLines.Count > 250)
            _progressConsoleLines.RemoveAt(0);

        ProgressConsoleText = string.Join(Environment.NewLine, _progressConsoleLines);
    }

    private IReadOnlyList<string> GetSelectedAuditNames()
    {
        var selected = new List<string>();

        if (RunExtensionAudit) selected.Add("Extensions");
        if (RunGroupAudit) selected.Add("Groups");
        if (RunQueueAudit) selected.Add("Queues");
        if (RunFlowAudit) selected.Add("Flows");
        if (RunInactiveUserAudit) selected.Add("Inactive Users");
        if (RunDidAudit) selected.Add("DIDs");
        if (RunUserTelephonyAudit) selected.Add("User Telephony Integrity");
        if (RunQueueServiceabilityAudit) selected.Add("Queue Serviceability");
        if (RunFlowDependencyAudit) selected.Add("IVR Flow Dependency");
        if (RunAuditLogs) selected.Add("Audit Logs");
        if (RunOperationalEventLogs) selected.Add("Operational Event Logs");
        if (RunOutboundEvents) selected.Add("Outbound Events");
        if (RunSiteTopologyAudit) selected.Add("Site Topology");
        if (RunStaleLicenseAudit) selected.Add("Stale License Usage");
        if (RunLicenseOverProvisioningAudit) selected.Add("License Over-Provisioning");
        if (RunRoleGroupOverlapAudit) selected.Add("Role / Group Overlap");
        if (RunPromptHygieneAudit) selected.Add("Prompt Hygiene");
        if (RunChangeAdjacencyAudit) selected.Add("Change Adjacency");
        if (RunFlappingDetectionAudit) selected.Add("Flapping Detection");
        if (RunHotSpotAudit) selected.Add("Hot Spot Ranking");

        return selected;
    }

    private string BuildAuditLogQuerySummary()
    {
        var parts = new List<string>
        {
            $"Audit Logs query: lookback={AuditLogLookbackHours}h",
            $"service={(string.Equals(SelectedAuditLogEntity, AllCatalogEntitiesOption, StringComparison.Ordinal) ? "all catalog services" : SelectedAuditLogEntity)}",
            $"sort={(string.Equals(SelectedAuditLogSortOrder, "Ascending", StringComparison.Ordinal) ? "ASC" : "DESC")}"
        };

        if (!string.Equals(SelectedAuditLogAction, AllActionsOption, StringComparison.Ordinal))
            parts.Add($"action={SelectedAuditLogAction}");

        if (!string.Equals(SelectedAuditLogEntityType, AllEntityTypesOption, StringComparison.Ordinal))
            parts.Add($"entityType={SelectedAuditLogEntityType}");

        if (!string.IsNullOrWhiteSpace(AuditLogUserIdFilter))
            parts.Add($"userId={AuditLogUserIdFilter.Trim()}");

        if (!string.IsNullOrWhiteSpace(AuditLogEntityIdFilter))
            parts.Add($"entityId={AuditLogEntityIdFilter.Trim()}");

        return string.Join(" | ", parts);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record RunSummaryRow(string AuditPath, int Items);

/// <summary>Simple synchronous command with CanExecute support.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
