using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GenesysExtensionAudit.Application;
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
    private readonly IAuditLogCatalogCache _auditLogCatalogCache;
    private readonly IGitHubUploadService _gitHubUploadService;
    private readonly IOptionsMonitor<GitHubOptions> _gitHubOptions;
    private readonly IOptionsMonitor<ElasticExportOptions> _elasticOptions;
    private readonly ObservableCollection<string> _auditLogEntities = [];
    private readonly ObservableCollection<string> _auditLogActions = [];
    private readonly ObservableCollection<string> _auditLogEntityTypes = [];
    private readonly ObservableCollection<string> _auditLogSortOrders = ["Descending", "Ascending"];
    private readonly ObservableCollection<RunSummaryRow> _lastRunSummary = [];
    private readonly ObservableCollection<string> _workbookExportModes = [ConsolidatedExportMode, SeparateExportMode];

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
    private bool _isRunning;
    private int _progressPercent;
    private string _progressMessage = string.Empty;
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
                    LoadAuditCatalog(forceRefresh: false);
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
        get => RunExtensionAudit && RunGroupAudit && RunQueueAudit && RunFlowAudit && RunInactiveUserAudit && RunDidAudit && RunAuditLogs && RunOperationalEventLogs && RunOutboundEvents;
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
        }
    }

    public bool HasAnyAuditSelected =>
        RunExtensionAudit || RunGroupAudit || RunQueueAudit || RunFlowAudit || RunInactiveUserAudit || RunDidAudit || RunAuditLogs || RunOperationalEventLogs || RunOutboundEvents;

    public string? LastExportPath
    {
        get => _lastExportPath;
        private set => SetField(ref _lastExportPath, value);
    }

    public bool HasExport => !string.IsNullOrWhiteSpace(LastExportPath);

    public bool HasReport => _lastReport is not null;
    public ObservableCollection<RunSummaryRow> LastRunSummary => _lastRunSummary;

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

        if (!HasAnyAuditSelected)
        {
            ErrorMessage = "Select at least one audit path.";
            StatusMessage = "No audit paths selected.";
            return;
        }

        IsRunning = true;
        StatusMessage = "Starting audit...";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var progress = new Progress<AuditProgress>(p =>
        {
            try
            {
                if (p.Percent is >= 0 and <= 100)
                    ProgressPercent = p.Percent;

                if (!string.IsNullOrWhiteSpace(p.Message))
                    ProgressMessage = p.Message;

                if (!string.IsNullOrWhiteSpace(p.Status))
                    StatusMessage = p.Status;
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
                RunAuditLogs = RunAuditLogs,
                AuditLogLookbackHours = AuditLogLookbackHours,
                AuditLogServiceNames = GetSelectedAuditLogServiceNames(),
                AuditLogFilters = BuildAuditLogFilters(),
                AuditLogSortField = "dateIssued",
                AuditLogSortOrder = string.Equals(SelectedAuditLogSortOrder, "Ascending", StringComparison.Ordinal) ? "ASC" : "DESC",
                RunOperationalEventLogs = RunOperationalEventLogs,
                OperationalEventLookbackDays = OperationalEventLookbackDays,
                RunOutboundEvents = RunOutboundEvents
            }, progress, ct).ConfigureAwait(true);

            _lastReport = report;
            BuildLastRunSummary(report);
            OnPropertyChanged(nameof(HasReport));
            RaiseCommandCanExecuteChanged();

            ProgressMessage = "Generating Excel report...";
            await SaveReportToFileAsync(report, ct).ConfigureAwait(true);

            ProgressPercent = 100;
            ProgressMessage = "Completed.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Audit cancelled.";
            ProgressMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Audit failed.";
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
        => LoadAuditCatalog(forceRefresh: true);

    private async void LoadAuditCatalog(bool forceRefresh)
    {
        if (IsLoadingAuditLogEntities)
            return;

        IsLoadingAuditLogEntities = true;

        try
        {
            var catalog = await _auditLogCatalogCache
                .GetOrRefreshCatalogAsync(forceRefresh, CancellationToken.None)
                .ConfigureAwait(true);

            _auditLogEntities.Clear();
            _auditLogEntities.Add(AllCatalogEntitiesOption);
            foreach (var svc in catalog)
                _auditLogEntities.Add(svc.ServiceName);

            SelectedAuditLogEntity = AllCatalogEntitiesOption;
            _auditLogEntitiesLoaded = true;
            StatusMessage = $"Loaded {_auditLogEntities.Count - 1} audit-log catalog services.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load audit-log catalog: {ex.Message}";
            StatusMessage = "Failed to load audit-log catalog.";
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
        var snapshotComparison = _snapshotService.Compare(report, previousSnapshot.Snapshot);
        report.FindingLifecycleFindings = snapshotComparison.LifecycleFindings;
        report.FindingLifecycleWasComputed = true;
        report.HistoricalDriftFindings = snapshotComparison.HistoricalDriftFindings;
        report.HistoricalDriftWasComputed = snapshotComparison.HistoricalDriftWasComputed;
        report.PreviousSnapshotGeneratedAtUtc = previousSnapshot.Snapshot?.GeneratedUtc;
        report.PreviousSnapshotPath = previousSnapshot.Path;
        var carePacket = _careEvidenceExportService.BuildPacket(report);

        var datePrefix = DateTime.Now.ToString("yyyy-MM-dd");
        if (string.Equals(SelectedWorkbookExportMode, SeparateExportMode, StringComparison.Ordinal))
        {
            var generatedFiles = new List<string>();
            var generatedPayloads = new List<(string FileName, byte[] Content)>();
            foreach (var audit in BuildSeparateAuditScopes(report))
            {
                ct.ThrowIfCancellationRequested();

                var xlsx = await _excelService.GenerateAsync(report, ct, audit.Scope).ConfigureAwait(true);
                var baseFileName = $"{datePrefix}_GenesysCloudAudit_{audit.AuditName}.xlsx";
                var fullPath = GetNextAvailableFilePath(outputDirectory, baseFileName);

                await File.WriteAllBytesAsync(fullPath, xlsx, ct).ConfigureAwait(true);
                generatedFiles.Add(fullPath);
                generatedPayloads.Add((Path.GetFileName(fullPath), xlsx));
            }

            var artifactBaseName = $"{datePrefix}_GenesysCloudAudit_Artifacts";
            var careJsonPath = GetNextAvailableFilePath(outputDirectory, $"{artifactBaseName}.care-evidence.json");
            var careJson = _careEvidenceArtifactService.BuildJson(carePacket);
            await File.WriteAllBytesAsync(careJsonPath, careJson, ct).ConfigureAwait(true);
            generatedFiles.Add(careJsonPath);
            generatedPayloads.Add((Path.GetFileName(careJsonPath), careJson));

            var careHtmlPath = GetNextAvailableFilePath(outputDirectory, $"{artifactBaseName}.care-summary.html");
            var careHtml = _careEvidenceArtifactService.BuildHtml(report, carePacket);
            await File.WriteAllBytesAsync(careHtmlPath, careHtml, ct).ConfigureAwait(true);
            generatedFiles.Add(careHtmlPath);
            generatedPayloads.Add((Path.GetFileName(careHtmlPath), careHtml));

            LastExportPath = outputDirectory;
            OnPropertyChanged(nameof(HasExport));
            StatusMessage = $"Saved {generatedFiles.Count} report(s) to {outputDirectory}";

            await TryPushToGitHubAsync(generatedPayloads, ct).ConfigureAwait(true);
            await TryExportToElasticAsync(report, carePacket, snapshotComparison.Snapshot, ct).ConfigureAwait(true);
            await _snapshotService
                .SaveSnapshotAsync(snapshotComparison.Snapshot, outputDirectory, snapshotPrefix, ct)
                .ConfigureAwait(true);
            return;
        }

        var consolidatedXlsx = await _excelService.GenerateAsync(report, ct, carePacket: carePacket).ConfigureAwait(true);
        var consolidatedBaseName = $"{datePrefix}_GenesysCloudAudit_Full.xlsx";
        var consolidatedPath = GetNextAvailableFilePath(outputDirectory, consolidatedBaseName);
        await File.WriteAllBytesAsync(consolidatedPath, consolidatedXlsx, ct).ConfigureAwait(true);

        var careJsonPathForWorkbook = Path.ChangeExtension(consolidatedPath, ".care-evidence.json");
        var careJsonForWorkbook = _careEvidenceArtifactService.BuildJson(carePacket);
        await File.WriteAllBytesAsync(careJsonPathForWorkbook, careJsonForWorkbook, ct).ConfigureAwait(true);

        var careHtmlPathForWorkbook = Path.ChangeExtension(consolidatedPath, ".care-summary.html");
        var careHtmlForWorkbook = _careEvidenceArtifactService.BuildHtml(report, carePacket);
        await File.WriteAllBytesAsync(careHtmlPathForWorkbook, careHtmlForWorkbook, ct).ConfigureAwait(true);

        LastExportPath = consolidatedPath;
        OnPropertyChanged(nameof(HasExport));
        StatusMessage = $"Saved: {Path.GetFileName(consolidatedPath)}";

        await TryPushToGitHubAsync(
            [
                (Path.GetFileName(consolidatedPath), consolidatedXlsx),
                (Path.GetFileName(careJsonPathForWorkbook), careJsonForWorkbook),
                (Path.GetFileName(careHtmlPathForWorkbook), careHtmlForWorkbook)
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
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            }));
        }

        return scopes;
    }

    private async Task TryPushToGitHubAsync(IReadOnlyList<(string FileName, byte[] Content)> files, CancellationToken ct)
    {
        if (!PushToGitHub || !_gitHubUploadService.IsConfigured || files.Count == 0)
            return;

        StatusMessage = "Pushing report(s) to GitHub...";
        var pushedCount = 0;
        var lastUrl = string.Empty;
        foreach (var file in files)
        {
            try
            {
                lastUrl = await _gitHubUploadService
                    .UploadAsync(file.FileName, file.Content, ct)
                    .ConfigureAwait(true);
                pushedCount++;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"GitHub push failed for {file.FileName}: {ex.Message}";
                StatusMessage = $"Saved locally ({pushedCount}/{files.Count} pushed to GitHub)";
                return;
            }
        }

        StatusMessage = files.Count == 1
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
            ("Operational Event Logs", report.Options.RunOperationalEventLogs, report.OperationalEventFindings.Count),
            ("OutboundEvents", report.Options.RunOutboundEvents, report.OutboundEventFindings.Count),
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
