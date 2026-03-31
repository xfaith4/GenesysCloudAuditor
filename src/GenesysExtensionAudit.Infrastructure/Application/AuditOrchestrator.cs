using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace GenesysExtensionAudit.Infrastructure.Application;

/// <summary>
/// Runs all audit categories and returns a combined <see cref="AuditReportData"/>.
/// Each phase reports progress and is independently cancellable.
/// </summary>
public sealed class AuditOrchestrator : IAuditOrchestrator
{
    private readonly IGenesysUsersClient _usersClient;
    private readonly IGenesysExtensionsClient _extensionsClient;
    private readonly IGenesysGroupsClient _groupsClient;
    private readonly IGenesysQueuesClient _queuesClient;
    private readonly IGenesysQueueMembersClient _queueMembersClient;
    private readonly IGenesysFlowsClient _flowsClient;
    private readonly IGenesysIvrsClient _ivrsClient;
    private readonly IGenesysDidsClient _didsClient;
    private readonly IGenesysAuditLogsClient _auditLogsClient;
    private readonly IGenesysOperationalEventsClient _operationalEventsClient;
    private readonly IGenesysOutboundEventsClient _outboundEventsClient;
    private readonly IGenesysLicenseUsersClient _licenseUsersClient;
    private readonly IGenesysUserRolesClient _userRolesClient;
    private readonly IGenesysSitesClient _sitesClient;
    private readonly IGenesysEdgesClient _edgesClient;
    private readonly IGenesysTrunksClient _trunksClient;
    private readonly IGenesysPromptsClient _promptsClient;
    private readonly IPaginator _paginator;
    private readonly GenesysRegionOptions _region;
    private readonly ILogger<AuditOrchestrator> _logger;

    public AuditOrchestrator(
        IGenesysUsersClient usersClient,
        IGenesysExtensionsClient extensionsClient,
        IGenesysGroupsClient groupsClient,
        IGenesysQueuesClient queuesClient,
        IGenesysQueueMembersClient queueMembersClient,
        IGenesysFlowsClient flowsClient,
        IGenesysIvrsClient ivrsClient,
        IGenesysDidsClient didsClient,
        IGenesysAuditLogsClient auditLogsClient,
        IGenesysOperationalEventsClient operationalEventsClient,
        IGenesysOutboundEventsClient outboundEventsClient,
        IGenesysLicenseUsersClient licenseUsersClient,
        IGenesysUserRolesClient userRolesClient,
        IGenesysSitesClient sitesClient,
        IGenesysEdgesClient edgesClient,
        IGenesysTrunksClient trunksClient,
        IGenesysPromptsClient promptsClient,
        IPaginator paginator,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<AuditOrchestrator> logger)
    {
        _usersClient = usersClient ?? throw new ArgumentNullException(nameof(usersClient));
        _extensionsClient = extensionsClient ?? throw new ArgumentNullException(nameof(extensionsClient));
        _groupsClient = groupsClient ?? throw new ArgumentNullException(nameof(groupsClient));
        _queuesClient = queuesClient ?? throw new ArgumentNullException(nameof(queuesClient));
        _queueMembersClient = queueMembersClient ?? throw new ArgumentNullException(nameof(queueMembersClient));
        _flowsClient = flowsClient ?? throw new ArgumentNullException(nameof(flowsClient));
        _ivrsClient = ivrsClient ?? throw new ArgumentNullException(nameof(ivrsClient));
        _didsClient = didsClient ?? throw new ArgumentNullException(nameof(didsClient));
        _auditLogsClient = auditLogsClient ?? throw new ArgumentNullException(nameof(auditLogsClient));
        _operationalEventsClient = operationalEventsClient ?? throw new ArgumentNullException(nameof(operationalEventsClient));
        _outboundEventsClient = outboundEventsClient ?? throw new ArgumentNullException(nameof(outboundEventsClient));
        _licenseUsersClient = licenseUsersClient ?? throw new ArgumentNullException(nameof(licenseUsersClient));
        _userRolesClient = userRolesClient ?? throw new ArgumentNullException(nameof(userRolesClient));
        _sitesClient = sitesClient ?? throw new ArgumentNullException(nameof(sitesClient));
        _edgesClient = edgesClient ?? throw new ArgumentNullException(nameof(edgesClient));
        _trunksClient = trunksClient ?? throw new ArgumentNullException(nameof(trunksClient));
        _promptsClient = promptsClient ?? throw new ArgumentNullException(nameof(promptsClient));
        _paginator = paginator ?? throw new ArgumentNullException(nameof(paginator));
        _region = regionOptions?.Value ?? throw new ArgumentNullException(nameof(regionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuditReportData> RunAsync(
        AuditRunOptions options,
        IProgress<AuditProgress> progress,
        CancellationToken ct)
    {
        var runStartedUtc = DateTimeOffset.UtcNow;
        var ps = Math.Clamp(options.PageSize, 1, 500);
        var runAny =
            options.RunExtensionAudit ||
            options.RunGroupAudit ||
            options.RunQueueAudit ||
            options.RunQueueServiceabilityAudit ||
            options.RunFlowAudit ||
            options.RunFlowDependencyAudit ||
            options.RunInactiveUserAudit ||
            options.RunDidAudit ||
            options.RunUserTelephonyAudit ||
            options.RunAuditLogs ||
            options.RunOperationalEventLogs ||
            options.RunOutboundEvents ||
            options.RunStaleLicenseAudit ||
            options.RunLicenseOverProvisioningAudit ||
            options.RunRoleGroupOverlapAudit ||
            options.RunSiteTopologyAudit ||
            options.RunPromptHygieneAudit;

        if (!runAny)
            throw new InvalidOperationException("At least one audit path must be selected.");

        _logger.LogInformation(
            "Audit started. PageSize={PageSize} IncludeInactive={IncludeInactive} StaleFlowDays={StaleFlowDays} InactiveUserDays={InactiveUserDays} " +
            "RunExtension={RunExtension} RunGroups={RunGroups} RunQueues={RunQueues} RunFlows={RunFlows} RunInactiveUsers={RunInactiveUsers} RunDids={RunDids} " +
            "RunUserTelephony={RunUserTelephony} RunQueueServiceability={RunQueueServiceability} RunFlowDependency={RunFlowDependency} " +
            "RunAuditLogs={RunAuditLogs} RunOperationalEvents={RunOperationalEvents} RunOutboundEvents={RunOutboundEvents} " +
            "RunStaleLicense={RunStaleLicense} StaleLicenseDays={StaleLicenseDays} RunLicenseOverProvisioning={RunLicenseOverProvisioning} " +
            "RunRoleGroupOverlap={RunRoleGroupOverlap} RoleGroupOverlapMaxUsers={RoleGroupOverlapMaxUsers} RunSiteTopology={RunSiteTopology} RunPromptHygiene={RunPromptHygiene} " +
            "RunChangeAdjacency={RunChangeAdjacency} RunFlappingDetection={RunFlappingDetection} RunHotSpot={RunHotSpot}",
            ps, options.IncludeInactiveUsers, options.StaleFlowThresholdDays, options.InactiveUserThresholdDays,
            options.RunExtensionAudit, options.RunGroupAudit, options.RunQueueAudit, options.RunFlowAudit,
            options.RunInactiveUserAudit, options.RunDidAudit,
            options.RunUserTelephonyAudit, options.RunQueueServiceabilityAudit, options.RunFlowDependencyAudit,
            options.RunAuditLogs, options.RunOperationalEventLogs, options.RunOutboundEvents,
            options.RunStaleLicenseAudit, options.StaleLicenseThresholdDays, options.RunLicenseOverProvisioningAudit,
            options.RunRoleGroupOverlapAudit, options.RoleGroupOverlapMaxUsersToCheck, options.RunSiteTopologyAudit,
            options.RunPromptHygieneAudit,
            options.RunChangeAdjacencyAudit, options.RunFlappingDetectionAudit, options.RunHotSpotAudit);

        var needsUsers = options.RunExtensionAudit || options.RunInactiveUserAudit || options.RunDidAudit
                         || options.RunUserTelephonyAudit || options.RunQueueServiceabilityAudit
                         || options.RunStaleLicenseAudit || options.RunLicenseOverProvisioningAudit
                         || options.RunRoleGroupOverlapAudit;
        var needsExtensions = options.RunExtensionAudit || options.RunUserTelephonyAudit;
        var needsGroups = options.RunGroupAudit;
        var needsQueues = options.RunQueueAudit || options.RunQueueServiceabilityAudit;
        var needsFlows = options.RunFlowAudit || options.RunFlowDependencyAudit;
        var needsDids = options.RunDidAudit || options.RunUserTelephonyAudit;
        var needsOperationalEvents = options.RunOperationalEventLogs;
        var needsOutboundEvents = options.RunOutboundEvents;
        var needsLicenseUsers = options.RunStaleLicenseAudit || options.RunLicenseOverProvisioningAudit;

        IReadOnlyList<GenesysUserDto> userDtos = [];
        IReadOnlyList<EdgeExtensionEntityDto> extDtos = [];
        IReadOnlyList<GroupDto> groupDtos = [];
        IReadOnlyList<QueueDto> queueDtos = [];
        IReadOnlyList<FlowDto> flowDtos = [];
        IReadOnlyList<DidDto> didDtos = [];
        IReadOnlyList<AuditLogFinding> auditLogFindings = [];
        IReadOnlyList<OperationalEventFinding> operationalEventFindings = [];
        IReadOnlyList<OutboundEventFinding> outboundEventFindings = [];
        IReadOnlyList<NoLocationUserFinding> noLocationUserFindings = [];
        IReadOnlyList<UserTelephonyIntegrityFinding> userTelephonyIntegrityFindings = [];
        IReadOnlyList<QueueServiceabilityFinding> queueServiceabilityFindings = [];
        IReadOnlyList<IvrDto> ivrDtos = [];
        IReadOnlyList<IvrFlowBindingFinding> ivrFlowBindingFindings = [];
        IReadOnlyList<LicenseUserDto> licenseUserDtos = [];
        IReadOnlyList<StaleLicenseFinding> staleLicenseFindings = [];
        IReadOnlyList<LicenseOverProvisioningFinding> licenseOverProvisioningFindings = [];
        IReadOnlyList<RoleGroupOverlapFinding> roleGroupOverlapFindings = [];
        IReadOnlyList<SiteDto> siteDtos = [];
        IReadOnlyList<EdgeDto> edgeDtos = [];
        IReadOnlyList<TrunkDto> trunkDtos = [];
        IReadOnlyList<SiteTopologyFinding> siteTopologyFindings = [];
        IReadOnlyList<PromptDto> promptDtos = [];
        IReadOnlyList<PromptHygieneFinding> promptHygieneFindings = [];
        IReadOnlyList<ChangeAdjacencyFinding> changeAdjacencyFindings = [];
        IReadOnlyList<FlappingFinding> flappingDetectionFindings = [];
        IReadOnlyList<HotSpotFinding> hotSpotFindings = [];
        IReadOnlyList<AuditRelationshipSnapshot> relationshipSnapshots = [];

        if (needsUsers)
        {
            Report(progress, 0, "Fetching users...");
            userDtos = await _paginator.FetchAllAsync(
                pn => _usersClient.GetUsersPageAsync(pn, ps, options.IncludeInactiveUsers, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} users", userDtos.Count);
        }

        if (needsExtensions)
        {
            Report(progress, 10, "Fetching extensions...");
            extDtos = await _paginator.FetchAllAsync(
                pn => _extensionsClient.GetExtensionsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} extensions", extDtos.Count);
        }

        if (needsGroups)
        {
            Report(progress, 20, "Fetching groups...");
            groupDtos = await _paginator.FetchAllAsync(
                pn => _groupsClient.GetGroupsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} groups", groupDtos.Count);
        }

        if (needsQueues)
        {
            Report(progress, 30, "Fetching queues...");
            queueDtos = await _paginator.FetchAllAsync(
                pn => _queuesClient.GetQueuesPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} queues", queueDtos.Count);
        }

        if (needsFlows)
        {
            Report(progress, 40, "Fetching Architect flows...");
            flowDtos = await _paginator.FetchAllAsync(
                pn => _flowsClient.GetFlowsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} flows", flowDtos.Count);
        }

        if (options.RunFlowDependencyAudit)
        {
            Report(progress, 48, "Fetching IVR configurations...");
            ivrDtos = await _paginator.FetchAllAsync(
                pn => _ivrsClient.GetIvrsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} IVRs", ivrDtos.Count);
        }

        if (options.RunPromptHygieneAudit)
        {
            Report(progress, 49, "Fetching architect prompts...");
            promptDtos = await _paginator.FetchAllAsync(
                pn => _promptsClient.GetPromptsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} prompts", promptDtos.Count);
        }

        if (needsDids)
        {
            Report(progress, 50, "Fetching DIDs...");
            didDtos = await _paginator.FetchAllAsync(
                pn => _didsClient.GetDidsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} DIDs", didDtos.Count);
        }

        if (options.RunAuditLogs)
        {
            Report(progress, 55, "Fetching audit logs service mappings...");
            var serviceMappings = await _auditLogsClient.GetServiceMappingsAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} audit service mappings", serviceMappings.Count);

            var now = DateTimeOffset.UtcNow;
            var lookbackHours = Math.Max(1, options.AuditLogLookbackHours);
            var interval = $"{now.AddHours(-lookbackHours):o}/{now:o}";

            // Build server-side filters from options (ANDed together).
            List<AuditLogFilterDto>? filterDtos = null;
            if (options.AuditLogFilters.Count > 0)
            {
                filterDtos = options.AuditLogFilters
                    .Where(f => !string.IsNullOrWhiteSpace(f.Property) && !string.IsNullOrWhiteSpace(f.Value))
                    .Select(f => new AuditLogFilterDto { Property = f.Property.Trim(), Value = f.Value.Trim() })
                    .ToList();
                if (filterDtos.Count == 0) filterDtos = null;
            }

            // Build sort (omit when using API defaults to keep the body clean).
            List<AuditLogSortDto>? sortDtos = null;
            if (!string.IsNullOrWhiteSpace(options.AuditLogSortField))
            {
                var sortOrder = string.IsNullOrWhiteSpace(options.AuditLogSortOrder)
                    ? "DESC"
                    : options.AuditLogSortOrder.Trim().ToUpperInvariant();
                sortDtos = [new AuditLogSortDto { Name = options.AuditLogSortField.Trim(), SortOrder = sortOrder }];
            }

            var submit = new AuditLogsSubmitRequestDto
            {
                Interval = interval,
                ServiceName = options.AuditLogServiceNames.Count > 0
                    ? options.AuditLogServiceNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : serviceMappings.ToList(),
                Action = [],
                Filters = filterDtos,
                Sort = sortDtos
            };

            Report(progress, 58, "Submitting audit logs transaction...");
            var transactionId = await _auditLogsClient.SubmitAuditQueryAsync(submit, ct).ConfigureAwait(false);

            const int maxPolls = 60;
            const int pollIntervalSeconds = 2;
            string state = "RUNNING";
            for (var i = 1; i <= maxPolls; i++)
            {
                ct.ThrowIfCancellationRequested();
                var status = await _auditLogsClient.GetAuditQueryStatusAsync(transactionId, ct).ConfigureAwait(false);
                state = (status.State ?? string.Empty).Trim().ToUpperInvariant();

                if (state == "FULFILLED")
                    break;
                if (state is "FAILED" or "CANCELLED")
                    throw new InvalidOperationException($"Audit transaction ended in state '{state}'.");

                await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct).ConfigureAwait(false);
            }

            if (state != "FULFILLED")
                throw new TimeoutException($"Audit transaction did not complete within {maxPolls} polls.");

            Report(progress, 59, "Fetching audit logs results...");
            var records = new List<JsonElement>();
            string? nextUri = null;
            do
            {
                var page = await _auditLogsClient
                    .GetAuditQueryResultsPageAsync(transactionId, nextUri, ct)
                    .ConfigureAwait(false);

                if (page.Results is { Count: > 0 })
                    records.AddRange(page.Results);

                nextUri = page.NextUri;
            } while (!string.IsNullOrWhiteSpace(nextUri));

            auditLogFindings = AnalyzeAuditLogs(records);
            _logger.LogInformation("Fetched {Count} audit log records", auditLogFindings.Count);
        }

        if (needsOperationalEvents)
        {
            Report(progress, 60, "Fetching operational events...");
            var now = DateTimeOffset.UtcNow;
            var lookbackDays = Math.Max(1, options.OperationalEventLookbackDays);
            var interval = $"{now.AddDays(-lookbackDays):o}/{now:o}";

            var request = new OperationalEventsQueryRequestDto
            {
                Interval = interval,
                SortOrder = "DESC"
            };

            var records = new List<OperationalEventDto>();
            string? afterCursor = null;
            var boundedPageSize = Math.Clamp(ps, 1, 200);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var page = await _operationalEventsClient
                    .QueryEventsAsync(request, boundedPageSize, afterCursor, ct)
                    .ConfigureAwait(false);

                if (page.Entities is { Count: > 0 })
                    records.AddRange(page.Entities);

                var nextAfter = ExtractCursorValue(page.NextUri, "after");
                if (string.IsNullOrWhiteSpace(nextAfter) ||
                    string.Equals(nextAfter, afterCursor, StringComparison.Ordinal))
                {
                    break;
                }

                afterCursor = nextAfter;
            }

            operationalEventFindings = AnalyzeOperationalEvents(records);
            _logger.LogInformation("Fetched {Count} operational event records", operationalEventFindings.Count);
        }

        if (needsOutboundEvents)
        {
            Report(progress, 62, "Fetching outbound events...");
            var outboundDtos = await _paginator.FetchAllAsync(
                pn => _outboundEventsClient.GetOutboundEventsPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);

            outboundEventFindings = AnalyzeOutboundEvents(outboundDtos);
            _logger.LogInformation("Fetched {Count} outbound event records", outboundEventFindings.Count);
        }

        if (needsLicenseUsers)
        {
            Report(progress, 64, "Fetching user license assignments...");
            licenseUserDtos = await _paginator.FetchAllAsync(
                pn => _licenseUsersClient.GetLicenseUsersPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} license user records", licenseUserDtos.Count);
        }

        if (options.RunSiteTopologyAudit)
        {
            Report(progress, 67, "Fetching telephony sites...");
            siteDtos = await _paginator.FetchAllAsync(
                pn => _sitesClient.GetSitesPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} sites", siteDtos.Count);

            Report(progress, 68, "Fetching edges...");
            edgeDtos = await _paginator.FetchAllAsync(
                pn => _edgesClient.GetEdgesPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} edges", edgeDtos.Count);

            Report(progress, 69, "Fetching trunks...");
            trunkDtos = await _paginator.FetchAllAsync(
                pn => _trunksClient.GetTrunksPageAsync(pn, ps, ct), ct)
                .ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} trunks", trunkDtos.Count);
        }

        Report(progress, 70, "Running selected audit paths...");
        var extensionReport = options.RunExtensionAudit
            ? RunExtensionAudit(userDtos, extDtos, options)
            : new AuditEngine.AuditReport();
        var groupFindings = options.RunGroupAudit
            ? AnalyzeGroups(groupDtos)
            : [];
        var queueFindings = options.RunQueueAudit
            ? AnalyzeQueues(queueDtos)
            : [];
        var flowFindings = options.RunFlowAudit
            ? AnalyzeFlows(flowDtos, options.StaleFlowThresholdDays)
            : [];
        var didFindings = options.RunDidAudit
            ? AnalyzeDids(didDtos, userDtos)
            : [];
        var inactiveUserFindings = options.RunInactiveUserAudit
            ? AnalyzeUserActivity(userDtos, options.InactiveUserThresholdDays)
            : [];
        noLocationUserFindings = options.RunInactiveUserAudit
            ? AnalyzeUsersMissingLocation(userDtos)
            : [];

        // Phase 1.2 — User telephony integrity (uses already-fetched users/extensions/DIDs)
        if (options.RunUserTelephonyAudit)
        {
            Report(progress, 75, "Analyzing user telephony integrity...");
            userTelephonyIntegrityFindings = AnalyzeUserTelephonyIntegrity(userDtos, extDtos, didDtos);
            _logger.LogInformation("User telephony integrity check complete. Findings={Count}", userTelephonyIntegrityFindings.Count);
        }

        // Phase 1.3 — Queue serviceability (requires member fetch per queue)
        if (options.RunQueueServiceabilityAudit && queueDtos.Count > 0)
        {
            Report(progress, 80, "Analyzing queue serviceability (fetching members)...");
            queueServiceabilityFindings = await AnalyzeQueueServiceabilityAsync(
                queueDtos, userDtos, options, ct).ConfigureAwait(false);
            _logger.LogInformation("Queue serviceability check complete. Findings={Count}", queueServiceabilityFindings.Count);
        }

        // Phase 1.4 — IVR flow dependency (IVR → flow binding integrity)
        if (options.RunFlowDependencyAudit && ivrDtos.Count > 0)
        {
            Report(progress, 83, "Analyzing IVR flow dependency bindings...");
            ivrFlowBindingFindings = AnalyzeIvrFlowBindings(ivrDtos, flowDtos, options.StaleFlowThresholdDays);
            _logger.LogInformation("IVR flow dependency check complete. Findings={Count}", ivrFlowBindingFindings.Count);
        }

        // Phase 1 Identity & License Hygiene
        var analyzer = new LicenseHygieneAnalyzer();

        if (options.RunStaleLicenseAudit || options.RunLicenseOverProvisioningAudit)
        {
            var userRecords = BuildUserRecords(userDtos);
            var licenseAssignments = BuildLicenseAssignments(licenseUserDtos);

            if (options.RunStaleLicenseAudit)
            {
                Report(progress, 85, "Analyzing stale license usage...");
                staleLicenseFindings = analyzer.AnalyzeStaleLicenses(
                    userRecords, licenseAssignments, options.StaleLicenseThresholdDays);
                _logger.LogInformation("Stale license check complete. Findings={Count}", staleLicenseFindings.Count);
            }

            if (options.RunLicenseOverProvisioningAudit)
            {
                Report(progress, 87, "Analyzing license over-provisioning...");
                licenseOverProvisioningFindings = analyzer.AnalyzeLicenseOverProvisioning(
                    userRecords, licenseAssignments, options.StaleLicenseThresholdDays);
                _logger.LogInformation("License over-provisioning check complete. Findings={Count}", licenseOverProvisioningFindings.Count);
            }
        }

        if (options.RunRoleGroupOverlapAudit && userDtos.Count > 0)
        {
            Report(progress, 88, "Analyzing role & group overlap (fetching user roles)...");
            roleGroupOverlapFindings = await AnalyzeRoleGroupOverlapAsync(
                userDtos, options, ct).ConfigureAwait(false);
            _logger.LogInformation("Role & group overlap check complete. Findings={Count}", roleGroupOverlapFindings.Count);
        }

        // Phase 1.5 — Site–edge–trunk topology integrity
        if (options.RunSiteTopologyAudit && (siteDtos.Count > 0 || edgeDtos.Count > 0 || trunkDtos.Count > 0))
        {
            Report(progress, 89, "Analyzing site–edge–trunk topology...");
            siteTopologyFindings = AnalyzeSiteEdgeTrunkTopology(siteDtos, edgeDtos, trunkDtos);
            _logger.LogInformation("Site topology check complete. Findings={Count}", siteTopologyFindings.Count);
        }

        // Phase 2 — Architect Prompt Hygiene
        if (options.RunPromptHygieneAudit && promptDtos.Count > 0)
        {
            Report(progress, 90, "Analyzing architect prompt hygiene...");
            promptHygieneFindings = AnalyzePromptHygiene(promptDtos);
            _logger.LogInformation("Prompt hygiene check complete. Findings={Count}", promptHygieneFindings.Count);
        }

        relationshipSnapshots = BuildRelationshipSnapshots(
            options,
            userDtos,
            extDtos,
            didDtos,
            ivrDtos,
            siteDtos,
            edgeDtos,
            trunkDtos);

        Report(progress, 92, "Composing report...");

        var partialReport = new AuditReportData
        {
            GeneratedAt = DateTimeOffset.Now,
            RunStartedAtUtc = runStartedUtc,
            RunCompletedAtUtc = DateTimeOffset.UtcNow,
            OrgRegion = _region.Region,
            Options = options,
            ExtensionReport = extensionReport,
            GroupFindings = groupFindings,
            QueueFindings = queueFindings,
            FlowFindings = flowFindings,
            InactiveUserFindings = inactiveUserFindings,
            NoLocationUserFindings = noLocationUserFindings,
            DidFindings = didFindings,
            AuditLogFindings = auditLogFindings,
            OperationalEventFindings = operationalEventFindings,
            OutboundEventFindings = outboundEventFindings,
            UserTelephonyIntegrityFindings = userTelephonyIntegrityFindings,
            QueueServiceabilityFindings = queueServiceabilityFindings,
            IvrFlowBindingFindings = ivrFlowBindingFindings,
            StaleLicenseFindings = staleLicenseFindings,
            LicenseOverProvisioningFindings = licenseOverProvisioningFindings,
            RoleGroupOverlapFindings = roleGroupOverlapFindings,
            SiteTopologyFindings = siteTopologyFindings,
            PromptHygieneFindings = promptHygieneFindings,
            RelationshipSnapshots = relationshipSnapshots
        };

        // Phase 2.1 — Change adjacency marker (requires audit logs + at least one other finding)
        if (options.RunChangeAdjacencyAudit && auditLogFindings.Count > 0)
        {
            Report(progress, 94, "Analyzing change adjacency...");
            var findingIndex = ActiveFindingIndex.Build(partialReport);
            changeAdjacencyFindings = new ChangeAdjacencyAnalyzer().Analyze(
                auditLogFindings, findingIndex, options.ChangeAdjacencyWindowMinutes);
            _logger.LogInformation("Change adjacency check complete. Findings={Count}", changeAdjacencyFindings.Count);
        }

        // Phase 2.2 — Flapping and instability detection (requires audit logs)
        if (options.RunFlappingDetectionAudit && auditLogFindings.Count > 0)
        {
            Report(progress, 96, "Analyzing flapping and instability...");
            flappingDetectionFindings = new FlappingDetectionAnalyzer().Analyze(
                auditLogFindings,
                options.FlappingDetectionWindowMinutes,
                options.FlappingDetectionMinChanges);
            _logger.LogInformation("Flapping detection complete. Findings={Count}", flappingDetectionFindings.Count);
        }

        // Phase 2.3 — Hot spot ranking (aggregates all findings — runs last)
        if (options.RunHotSpotAudit)
        {
            // Build the report snapshot including Phase 2.1 and 2.2 results so they feed into hot-spot ranking
            var reportWithPhase2 = new AuditReportData
            {
                GeneratedAt = partialReport.GeneratedAt,
                RunStartedAtUtc = partialReport.RunStartedAtUtc,
                RunCompletedAtUtc = partialReport.RunCompletedAtUtc,
                OrgRegion = partialReport.OrgRegion,
                Options = partialReport.Options,
                ExtensionReport = partialReport.ExtensionReport,
                GroupFindings = partialReport.GroupFindings,
                QueueFindings = partialReport.QueueFindings,
                FlowFindings = partialReport.FlowFindings,
                InactiveUserFindings = partialReport.InactiveUserFindings,
                NoLocationUserFindings = partialReport.NoLocationUserFindings,
                DidFindings = partialReport.DidFindings,
                AuditLogFindings = partialReport.AuditLogFindings,
                OperationalEventFindings = partialReport.OperationalEventFindings,
                OutboundEventFindings = partialReport.OutboundEventFindings,
                UserTelephonyIntegrityFindings = partialReport.UserTelephonyIntegrityFindings,
                QueueServiceabilityFindings = partialReport.QueueServiceabilityFindings,
                IvrFlowBindingFindings = partialReport.IvrFlowBindingFindings,
                StaleLicenseFindings = partialReport.StaleLicenseFindings,
                LicenseOverProvisioningFindings = partialReport.LicenseOverProvisioningFindings,
                RoleGroupOverlapFindings = partialReport.RoleGroupOverlapFindings,
                SiteTopologyFindings = partialReport.SiteTopologyFindings,
                PromptHygieneFindings = partialReport.PromptHygieneFindings,
                RelationshipSnapshots = partialReport.RelationshipSnapshots,
                ChangeAdjacencyFindings = changeAdjacencyFindings,
                FlappingDetectionFindings = flappingDetectionFindings
            };
            Report(progress, 98, "Ranking hot spots...");
            hotSpotFindings = new HotSpotAnalyzer().Analyze(reportWithPhase2, options.HotSpotMinDistinctDomains);
            _logger.LogInformation("Hot spot ranking complete. Findings={Count}", hotSpotFindings.Count);
        }

        var totalFindings = extensionReport.DuplicateProfileExtensions.Count
            + extensionReport.DuplicateAssignedExtensions.Count
            + extensionReport.ProfileExtensionsNotAssigned.Count
            + extensionReport.AssignedExtensionsMissingFromProfiles.Count
            + extensionReport.ExtensionAssignedToWrongEntity.Count
            + extensionReport.InvalidProfileExtensions.Count
            + extensionReport.InvalidAssignedExtensions.Count
            + groupFindings.Count + queueFindings.Count
            + flowFindings.Count + inactiveUserFindings.Count
            + noLocationUserFindings.Count
            + didFindings.Count + auditLogFindings.Count
            + operationalEventFindings.Count + outboundEventFindings.Count
            + userTelephonyIntegrityFindings.Count
            + queueServiceabilityFindings.Count
            + ivrFlowBindingFindings.Count
            + staleLicenseFindings.Count
            + licenseOverProvisioningFindings.Count
            + roleGroupOverlapFindings.Count
            + siteTopologyFindings.Count
            + promptHygieneFindings.Count
            + changeAdjacencyFindings.Count
            + flappingDetectionFindings.Count
            + hotSpotFindings.Count;

        _logger.LogInformation(
            "Audit complete. TotalFindings={TotalFindings} Groups={Groups} Queues={Queues} Flows={Flows} StaleTokenUsers={StaleTokenUsers} NoLocationUsers={NoLocationUsers} DIDs={DIDs} " +
            "UserTelephonyIntegrity={UserTelephonyIntegrity} QueueServiceability={QueueServiceability} IvrFlowBindings={IvrFlowBindings} " +
            "OperationalEvents={OperationalEvents} OutboundEvents={OutboundEvents} " +
            "StaleLicenses={StaleLicenses} LicenseOverProvisioning={LicenseOverProvisioning} RoleGroupOverlap={RoleGroupOverlap} SiteTopology={SiteTopology} PromptHygiene={PromptHygiene} ChangeAdjacency={ChangeAdjacency} " +
            "FlappingDetection={FlappingDetection} HotSpot={HotSpot}",
            totalFindings, groupFindings.Count, queueFindings.Count,
            flowFindings.Count, inactiveUserFindings.Count, noLocationUserFindings.Count, didFindings.Count,
            userTelephonyIntegrityFindings.Count, queueServiceabilityFindings.Count, ivrFlowBindingFindings.Count,
            operationalEventFindings.Count, outboundEventFindings.Count,
            staleLicenseFindings.Count, licenseOverProvisioningFindings.Count, roleGroupOverlapFindings.Count, siteTopologyFindings.Count,
            promptHygieneFindings.Count, changeAdjacencyFindings.Count,
            flappingDetectionFindings.Count, hotSpotFindings.Count);

        Report(progress, 100,
            $"Complete — {totalFindings} total findings across all checks.",
            status: "Audit completed successfully.");

        return new AuditReportData
        {
            GeneratedAt = DateTimeOffset.Now,
            RunStartedAtUtc = runStartedUtc,
            RunCompletedAtUtc = DateTimeOffset.UtcNow,
            OrgRegion = _region.Region,
            Options = options,
            ExtensionReport = extensionReport,
            GroupFindings = groupFindings,
            QueueFindings = queueFindings,
            FlowFindings = flowFindings,
            InactiveUserFindings = inactiveUserFindings,
            NoLocationUserFindings = noLocationUserFindings,
            DidFindings = didFindings,
            AuditLogFindings = auditLogFindings,
            OperationalEventFindings = operationalEventFindings,
            OutboundEventFindings = outboundEventFindings,
            UserTelephonyIntegrityFindings = userTelephonyIntegrityFindings,
            QueueServiceabilityFindings = queueServiceabilityFindings,
            IvrFlowBindingFindings = ivrFlowBindingFindings,
            StaleLicenseFindings = staleLicenseFindings,
            LicenseOverProvisioningFindings = licenseOverProvisioningFindings,
            RoleGroupOverlapFindings = roleGroupOverlapFindings,
            SiteTopologyFindings = siteTopologyFindings,
            PromptHygieneFindings = promptHygieneFindings,
            ChangeAdjacencyFindings = changeAdjacencyFindings,
            FlappingDetectionFindings = flappingDetectionFindings,
            HotSpotFindings = hotSpotFindings,
            RelationshipSnapshots = relationshipSnapshots
        };
    }

    // ─── Extension audit (delegates to AuditEngine) ───────────────────────

    private static AuditEngine.AuditReport RunExtensionAudit(
        IReadOnlyList<GenesysUserDto> users,
        IReadOnlyList<EdgeExtensionEntityDto> extensions,
        AuditRunOptions options)
    {
        var engine = new AuditEngine();

        var userProfiles = users
            .Where(u => u.Id is not null)
            .Select(u => new AuditEngine.UserProfileRecord(
                UserId: u.Id!,
                UserName: u.Name,
                State: u.State,
                WorkPhoneExtensionRaw: ExtractWorkPhoneExtension(u)))
            .ToList();

        var assignments = extensions
            .Where(e => e.Id is not null)
            .Select(e => new AuditEngine.ExtensionAssignmentRecord(
                AssignmentId: e.Id!,
                ExtensionRaw: e.Extension,
                TargetType: e.AssignedTo?.Type,
                TargetId: e.AssignedTo?.Id))
            .ToList();

        return engine.Run(userProfiles, assignments, new AuditEngine.AuditEngineOptions
        {
            IncludeInactiveUsers = options.IncludeInactiveUsers,
            ComputeDuplicateAssignedExtensions = true,
            ComputeAssignedButMissingFromProfiles = true
        });
    }

    // ─── Group analysis ───────────────────────────────────────────────────

    private static IReadOnlyList<GroupFinding> AnalyzeGroups(IReadOnlyList<GroupDto> groups)
    {
        var findings = new List<GroupFinding>();

        foreach (var g in groups)
        {
            if (g.Id is null) continue;

            var memberCount = g.MemberCount ?? 0;
            if (memberCount == 0)
            {
                findings.Add(new GroupFinding(
                    GroupId: g.Id,
                    GroupName: g.Name,
                    Type: g.Type,
                    State: g.State,
                    MemberCount: memberCount,
                    DateModified: g.DateModified,
                    Issue: "Empty group — no members"));
            }
            else if (memberCount == 1)
            {
                findings.Add(new GroupFinding(
                    GroupId: g.Id,
                    GroupName: g.Name,
                    Type: g.Type,
                    State: g.State,
                    MemberCount: memberCount,
                    DateModified: g.DateModified,
                    Issue: "Single-member group — review if intentional"));
            }
        }

        return findings.OrderBy(f => f.MemberCount).ThenBy(f => f.GroupName).ToList();
    }

    // ─── Queue analysis ───────────────────────────────────────────────────

    private static IReadOnlyList<QueueFinding> AnalyzeQueues(IReadOnlyList<QueueDto> queues)
    {
        var findings = new List<QueueFinding>();

        // Empty queues
        foreach (var q in queues.Where(q => q.Id is not null && (q.MemberCount ?? 0) == 0))
        {
            findings.Add(new QueueFinding(
                QueueId: q.Id!,
                QueueName: q.Name,
                Description: q.Description,
                MemberCount: q.MemberCount ?? 0,
                Issue: "Empty queue — no members"));
        }

        // Duplicate queue names (case-insensitive)
        var byName = queues
            .Where(q => q.Name is not null)
            .GroupBy(q => q.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in byName)
        {
            foreach (var q in group.Where(q => q.Id is not null))
            {
                findings.Add(new QueueFinding(
                    QueueId: q.Id!,
                    QueueName: q.Name,
                    Description: q.Description,
                    MemberCount: q.MemberCount ?? 0,
                    Issue: $"Duplicate queue name (case-insensitive match): \"{group.Key}\""));
            }
        }

        return findings.OrderBy(f => f.Issue).ThenBy(f => f.QueueName).ToList();
    }

    // ─── Flow analysis ────────────────────────────────────────────────────

    private static IReadOnlyList<FlowFinding> AnalyzeFlows(
        IReadOnlyList<FlowDto> flows, int thresholdDays)
    {
        var findings = new List<FlowFinding>();
        var cutoff = DateTime.UtcNow.AddDays(-thresholdDays);

        foreach (var f in flows)
        {
            if (f.Id is null) continue;

            // Draft / never published
            if (f.PublishedVersion is null)
            {
                findings.Add(new FlowFinding(
                    FlowId: f.Id,
                    FlowName: f.Name,
                    FlowType: f.Type,
                    IsPublished: false,
                    PublishedDate: null,
                    DateModified: f.DateModified,
                    DaysSincePublished: null,
                    Issue: "Never published (draft)"));
                continue;
            }

            var publishedDate = f.PublishedVersion.PublishedDate;
            if (publishedDate.HasValue && publishedDate.Value < cutoff)
            {
                var days = (int)(DateTime.UtcNow - publishedDate.Value).TotalDays;
                findings.Add(new FlowFinding(
                    FlowId: f.Id,
                    FlowName: f.Name,
                    FlowType: f.Type,
                    IsPublished: true,
                    PublishedDate: publishedDate.Value,
                    DateModified: f.DateModified,
                    DaysSincePublished: days,
                    Issue: $"Not republished in {days} days (threshold: {thresholdDays})"));
            }
        }

        return findings
            .OrderByDescending(f => f.DaysSincePublished ?? int.MaxValue)
            .ThenBy(f => f.FlowName)
            .ToList();
    }

    // ─── User activity analysis ───────────────────────────────────────────

    private static IReadOnlyList<InactiveUserFinding> AnalyzeUserActivity(
        IReadOnlyList<GenesysUserDto> users, int thresholdDays)
    {
        var findings = new List<InactiveUserFinding>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-thresholdDays);

        foreach (var u in users.Where(u => u.Id is not null))
        {
            var tokenLastIssued = GetTokenLastIssuedDate(u);
            if (tokenLastIssued is null || tokenLastIssued >= cutoff)
                continue;

            var days = (int)(DateTimeOffset.UtcNow - tokenLastIssued.Value).TotalDays;
            var issue = $"Token last issued {days} days ago (threshold: {thresholdDays})";

            findings.Add(new InactiveUserFinding(
                UserId: u.Id!,
                UserName: u.Name,
                Email: u.Email,
                State: u.State,
                TokenLastIssuedDate: tokenLastIssued,
                DaysSinceLogin: days,
                Issue: issue));
        }

        return findings
            .OrderByDescending(f => f.DaysSinceLogin ?? int.MaxValue)
            .ThenBy(f => f.UserName)
            .ToList();
    }

    private static IReadOnlyList<NoLocationUserFinding> AnalyzeUsersMissingLocation(
        IReadOnlyList<GenesysUserDto> users)
    {
        var findings = new List<NoLocationUserFinding>();

        foreach (var u in users.Where(u => u.Id is not null))
        {
            var locations = u.Locations ?? [];
            var nonEmptyLocations = locations
                .Where(l => l is not null
                    && (!string.IsNullOrWhiteSpace(l.Id) || !string.IsNullOrWhiteSpace(l.Name)))
                .ToList();

            if (nonEmptyLocations.Count > 0)
                continue;

            findings.Add(new NoLocationUserFinding(
                UserId: u.Id!,
                UserName: u.Name,
                Email: u.Email,
                State: u.State,
                LocationCount: 0,
                Issue: "No location set on user account"));
        }

        return findings
            .OrderBy(f => f.UserName)
            .ToList();
    }

    // ─── DID analysis ─────────────────────────────────────────────────────

    private static IReadOnlyList<DidFinding> AnalyzeDids(
        IReadOnlyList<DidDto> dids,
        IReadOnlyList<GenesysUserDto> users)
    {
        var findings = new List<DidFinding>();

        var userById = users
            .Where(u => u.Id is not null)
            .ToDictionary(u => u.Id!, u => u, StringComparer.OrdinalIgnoreCase);

        // Build set of work-phone numbers from all user profile phone fields (Work, Work2, etc.).
        var userPhoneNumbers = users
            .SelectMany(GetAllWorkPhoneContactInfo)
            .Where(ci => !string.IsNullOrWhiteSpace(ci.Address))
            .Select(ci => NormalizePhoneNumber(ci.Address!))
            .Where(n => n is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        foreach (var did in dids)
        {
            if (did.Id is null || string.IsNullOrWhiteSpace(did.PhoneNumber)) continue;

            var normalizedNumber = NormalizePhoneNumber(did.PhoneNumber);

            // DID in pool but not assigned to any entity
            if (did.Owner is null || string.IsNullOrWhiteSpace(did.Owner.Id))
            {
                findings.Add(new DidFinding(
                    DidId: did.Id,
                    PhoneNumber: did.PhoneNumber,
                    PoolId: did.DidPool?.Id,
                    OwnerType: null,
                    OwnerId: null,
                    OwnerName: null,
                    Issue: "DID in pool has no assigned owner"));
                continue;
            }

            // DID assigned to a user — verify user exists and is active
            if (string.Equals(did.Owner.Type, "User", StringComparison.OrdinalIgnoreCase))
            {
                if (!userById.TryGetValue(did.Owner.Id, out var owner))
                {
                    findings.Add(new DidFinding(
                        DidId: did.Id,
                        PhoneNumber: did.PhoneNumber,
                        PoolId: did.DidPool?.Id,
                        OwnerType: did.Owner.Type,
                        OwnerId: did.Owner.Id,
                        OwnerName: null,
                        Issue: "DID assigned to user not found in user list"));
                }
                else if (string.Equals(owner.State, "inactive", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new DidFinding(
                        DidId: did.Id,
                        PhoneNumber: did.PhoneNumber,
                        PoolId: did.DidPool?.Id,
                        OwnerType: did.Owner.Type,
                        OwnerId: did.Owner.Id,
                        OwnerName: owner.Name,
                        Issue: "DID assigned to inactive user"));
                }
            }

            // DID number not appearing on any user profile contact info
            if (normalizedNumber is not null && !userPhoneNumbers.Contains(normalizedNumber))
            {
                findings.Add(new DidFinding(
                    DidId: did.Id,
                    PhoneNumber: did.PhoneNumber,
                    PoolId: did.DidPool?.Id,
                    OwnerType: did.Owner?.Type,
                    OwnerId: did.Owner?.Id,
                    OwnerName: did.Owner?.Id is not null && userById.TryGetValue(did.Owner.Id, out var u) ? u.Name : null,
                    Issue: "DID number not found on any user profile"));
            }
        }

        return findings.OrderBy(f => f.Issue).ThenBy(f => f.PhoneNumber).ToList();
    }

    // ─── Phase 1.4 — IVR flow dependency ─────────────────────────────────────

    /// <summary>
    /// Cross-references IVR binding slots against the known flow list to detect:
    /// <list type="bullet">
    ///   <item>IVR binds to a flow that is in draft (never published) — callers reach a broken entry point.</item>
    ///   <item>IVR binds to a flow that is stale (last published &gt;N days ago) — routing may be outdated.</item>
    ///   <item>IVR binds to a flow ID not found in the flow list — the flow was likely deleted.</item>
    ///   <item>IVR has DNIS numbers but no open-hours flow binding — all inbound calls have no route.</item>
    /// </list>
    /// Uses only already-fetched IVR and flow data — no additional API calls.
    /// </summary>
    private static IReadOnlyList<IvrFlowBindingFinding> AnalyzeIvrFlowBindings(
        IReadOnlyList<IvrDto> ivrs,
        IReadOnlyList<FlowDto> flows,
        int staleFlowThresholdDays)
    {
        var findings = new List<IvrFlowBindingFinding>();

        // Build fast lookup of flow ID → FlowDto
        var flowById = flows
            .Where(f => f.Id is not null)
            .ToDictionary(f => f.Id!, f => f, StringComparer.OrdinalIgnoreCase);

        var staleCutoff = DateTime.UtcNow.AddDays(-staleFlowThresholdDays);

        foreach (var ivr in ivrs)
        {
            if (ivr.Id is null) continue;

            var dnis = (IReadOnlyList<string>)(ivr.Dnis?.Where(d => !string.IsNullOrWhiteSpace(d)).ToList()
                       ?? []);

            // Check each binding slot
            var slots = new[]
            {
                ("OpenHours",    ivr.OpenHoursFlow),
                ("ClosedHours",  ivr.ClosedHoursFlow),
                ("HolidayHours", ivr.HolidayHoursFlow),
            };

            bool hasAnyBinding = slots.Any(s => s.Item2?.Id is not null);

            // Check 1a: IVR has DNIS but no schedule group — time-based routing cannot function
            if (dnis.Count > 0 && ivr.ScheduleGroup?.Id is null && hasAnyBinding)
            {
                findings.Add(new IvrFlowBindingFinding(
                    IvrId: ivr.Id,
                    IvrName: ivr.Name,
                    Dnis: dnis,
                    BindingSlot: "ScheduleGroup",
                    BoundFlowId: null,
                    BoundFlowName: null,
                    FlowDaysSincePublished: null,
                    FindingCode: IvrBindingCode.NoScheduleGroup,
                    Issue: $"IVR '{ivr.Name ?? ivr.Id}' has {dnis.Count} DNIS number(s) and flow bindings but no schedule group. " +
                           "Without a schedule group the IVR cannot determine which hours flow to invoke at any given time.",
                    Severity: FindingSeverity.High,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Assign a schedule group to this IVR to control which flow applies during open, closed, and holiday hours."));
            }

            // Check 1b: IVR has DNIS but no open-hours flow at all
            if (dnis.Count > 0 && ivr.OpenHoursFlow?.Id is null)
            {
                findings.Add(new IvrFlowBindingFinding(
                    IvrId: ivr.Id,
                    IvrName: ivr.Name,
                    Dnis: dnis,
                    BindingSlot: "OpenHours",
                    BoundFlowId: null,
                    BoundFlowName: null,
                    FlowDaysSincePublished: null,
                    FindingCode: IvrBindingCode.NoOpenHoursFlow,
                    Issue: $"IVR '{ivr.Name ?? ivr.Id}' has {dnis.Count} DNIS number(s) but no open-hours flow binding. " +
                           "Inbound calls during open hours have no route.",
                    Severity: FindingSeverity.Critical,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Assign an active, published Architect flow to the open-hours slot of this IVR."));
            }

            // Check 2–4: for each populated binding slot, verify the bound flow's health
            foreach (var (slot, flowRef) in slots)
            {
                if (flowRef?.Id is null) continue;

                if (!flowById.TryGetValue(flowRef.Id, out var flow))
                {
                    // Flow ID referenced by IVR does not exist in our flow list
                    findings.Add(new IvrFlowBindingFinding(
                        IvrId: ivr.Id,
                        IvrName: ivr.Name,
                        Dnis: dnis,
                        BindingSlot: slot,
                        BoundFlowId: flowRef.Id,
                        BoundFlowName: flowRef.Name,
                        FlowDaysSincePublished: null,
                        FindingCode: IvrBindingCode.FlowNotFound,
                        Issue: $"IVR '{ivr.Name ?? ivr.Id}' {slot} slot references flow '{flowRef.Name ?? flowRef.Id}' " +
                               "which was not found in the Architect flow list. The flow may have been deleted.",
                        Severity: FindingSeverity.Critical,
                        Category: FindingCategory.LocalConfigFix,
                        RecommendedAction: "Re-bind this IVR slot to an existing, published flow, or restore the deleted flow."));
                    continue;
                }

                // Flow exists — check published state
                if (flow.PublishedVersion is null)
                {
                    findings.Add(new IvrFlowBindingFinding(
                        IvrId: ivr.Id,
                        IvrName: ivr.Name,
                        Dnis: dnis,
                        BindingSlot: slot,
                        BoundFlowId: flow.Id,
                        BoundFlowName: flow.Name,
                        FlowDaysSincePublished: null,
                        FindingCode: IvrBindingCode.FlowIsDraft,
                        Issue: $"IVR '{ivr.Name ?? ivr.Id}' {slot} slot is bound to flow '{flow.Name ?? flow.Id}' " +
                               "which has never been published (draft). Callers reaching this slot will experience an error.",
                        Severity: FindingSeverity.Critical,
                        Category: FindingCategory.LocalConfigFix,
                        RecommendedAction: "Publish the flow or bind the IVR slot to an already-published flow."));
                    continue;
                }

                // Flow is published — check if it's stale
                var publishedDate = flow.PublishedVersion.PublishedDate;
                if (publishedDate.HasValue && publishedDate.Value < staleCutoff)
                {
                    var days = (int)(DateTime.UtcNow - publishedDate.Value).TotalDays;
                    findings.Add(new IvrFlowBindingFinding(
                        IvrId: ivr.Id,
                        IvrName: ivr.Name,
                        Dnis: dnis,
                        BindingSlot: slot,
                        BoundFlowId: flow.Id,
                        BoundFlowName: flow.Name,
                        FlowDaysSincePublished: days,
                        FindingCode: IvrBindingCode.FlowIsStale,
                        Issue: $"IVR '{ivr.Name ?? ivr.Id}' {slot} slot is bound to flow '{flow.Name ?? flow.Id}' " +
                               $"which has not been republished in {days} days (threshold: {staleFlowThresholdDays}). " +
                               "Routing behaviour may not reflect the current intended configuration.",
                        Severity: FindingSeverity.Medium,
                        Category: FindingCategory.ChangeReviewRequired,
                        RecommendedAction: "Review the flow and republish if the configuration is still current, or update routing to a newer flow version."));
                }
            }
        }

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.IvrName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.BindingSlot, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ─── Phase 1.5 — Site–edge–trunk topology ────────────────────────────────

    private static IReadOnlyList<SiteTopologyFinding> AnalyzeSiteEdgeTrunkTopology(
        IReadOnlyList<SiteDto> sites,
        IReadOnlyList<EdgeDto> edges,
        IReadOnlyList<TrunkDto> trunks)
        => new SiteTopologyAnalyzer().Analyze(sites, edges, trunks);

    private static IReadOnlyList<AuditRelationshipSnapshot> BuildRelationshipSnapshots(
        AuditRunOptions options,
        IReadOnlyList<GenesysUserDto> users,
        IReadOnlyList<EdgeExtensionEntityDto> extensions,
        IReadOnlyList<DidDto> dids,
        IReadOnlyList<IvrDto> ivrs,
        IReadOnlyList<SiteDto> sites,
        IReadOnlyList<EdgeDto> edges,
        IReadOnlyList<TrunkDto> trunks)
    {
        var snapshots = new List<AuditRelationshipSnapshot>();

        if (options.RunUserTelephonyAudit)
            snapshots.AddRange(CaptureTelephonyRelationshipSnapshots(users, extensions, dids));

        if (options.RunFlowDependencyAudit)
            snapshots.AddRange(CaptureRoutingRelationshipSnapshots(ivrs));

        if (options.RunSiteTopologyAudit)
            snapshots.AddRange(CaptureTopologyRelationshipSnapshots(sites, edges, trunks));

        return snapshots
            .OrderBy(s => s.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.ObjectName ?? s.ObjectId ?? s.RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<AuditRelationshipSnapshot> CaptureTelephonyRelationshipSnapshots(
        IReadOnlyList<GenesysUserDto> users,
        IReadOnlyList<EdgeExtensionEntityDto> extensions,
        IReadOnlyList<DidDto> dids)
    {
        var snapshots = new List<AuditRelationshipSnapshot>();
        var didsByUserId = dids
            .Where(d => d.Id is not null
                && string.Equals(d.Owner?.Type, "USER", StringComparison.OrdinalIgnoreCase)
                && d.Owner?.Id is not null)
            .GroupBy(d => d.Owner!.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var user in users.Where(u => u.Id is not null))
        {
            var profileExtension = ExtractWorkPhoneExtension(user);
            didsByUserId.TryGetValue(user.Id!, out var ownedDids);
            var locationIds = (user.Locations ?? [])
                .Where(l => l.Id is not null)
                .Select(l => l.Id!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var locationDisplay = (user.Locations ?? [])
                .Select(l => FormatNameId(l.Name, l.Id))
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "(none)")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var didNumbersCanonical = ownedDids is not null
                ? ownedDids
                    .Select(d => NormalizePhoneNumber(d.PhoneNumber ?? string.Empty) ?? NormalizeSnapshotValue(d.PhoneNumber))
                    .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            var didNumbersDisplay = ownedDids is not null
                ? ownedDids
                    .Select(d => d.PhoneNumber)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            snapshots.Add(new AuditRelationshipSnapshot(
                Domain: "Telephony Ownership",
                RelationshipType: "UserTelephonyBinding",
                RelationshipKey: $"telephony-user|{user.Id}",
                ObjectType: "User",
                ObjectId: user.Id,
                ObjectName: user.Name,
                NormalizedValue: $"profileExt={NormalizeSnapshotValue(profileExtension)};station={NormalizeSnapshotValue(user.Station?.Id)};dids={JoinCanonical(didNumbersCanonical)};locations={JoinCanonical(locationIds)}",
                DisplayValue: $"Profile Extension={DisplayValue(profileExtension)}; Station={FormatNameId(user.Station?.Name, user.Station?.Id)}; DIDs={JoinDisplay(didNumbersDisplay)}; Locations={JoinDisplay(locationDisplay)}"));
        }

        foreach (var extension in extensions.Where(e => e.Id is not null))
        {
            snapshots.Add(new AuditRelationshipSnapshot(
                Domain: "Telephony Ownership",
                RelationshipType: "ExtensionAssignment",
                RelationshipKey: $"telephony-extension|{extension.Id}",
                ObjectType: "Extension",
                ObjectId: extension.Id,
                ObjectName: extension.Extension,
                NormalizedValue: $"extension={NormalizeSnapshotValue(extension.Extension)};assignedTo={NormalizeSnapshotValue(extension.AssignedTo?.Type)}:{NormalizeSnapshotValue(extension.AssignedTo?.Id)}",
                DisplayValue: $"Extension={DisplayValue(extension.Extension)}; Assigned To={FormatOwner(extension.AssignedTo?.Type, extension.AssignedTo?.Id)}"));
        }

        foreach (var did in dids.Where(d => d.Id is not null))
        {
            var normalizedNumber = NormalizePhoneNumber(did.PhoneNumber ?? string.Empty) ?? NormalizeSnapshotValue(did.PhoneNumber);
            snapshots.Add(new AuditRelationshipSnapshot(
                Domain: "Telephony Ownership",
                RelationshipType: "DidOwnership",
                RelationshipKey: $"telephony-did|{did.Id}",
                ObjectType: "Did",
                ObjectId: did.Id,
                ObjectName: did.PhoneNumber,
                NormalizedValue: $"number={NormalizeSnapshotValue(normalizedNumber)};owner={NormalizeSnapshotValue(did.Owner?.Type)}:{NormalizeSnapshotValue(did.Owner?.Id)};pool={NormalizeSnapshotValue(did.DidPool?.Id)}",
                DisplayValue: $"Number={DisplayValue(did.PhoneNumber)}; Owner={FormatOwner(did.Owner?.Type, did.Owner?.Id)}; Pool={DisplayValue(did.DidPool?.Id)}"));
        }

        return snapshots;
    }

    private static IReadOnlyList<AuditRelationshipSnapshot> CaptureRoutingRelationshipSnapshots(IReadOnlyList<IvrDto> ivrs)
        => ivrs
            .Where(ivr => ivr.Id is not null)
            .Select(ivr =>
            {
                var normalizedDnis = (ivr.Dnis ?? [])
                    .Select(d => NormalizePhoneNumber(d) ?? NormalizeSnapshotValue(d))
                    .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var displayDnis = (ivr.Dnis ?? [])
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => d!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new AuditRelationshipSnapshot(
                    Domain: "Routing Bindings",
                    RelationshipType: "IvrRoutingBinding",
                    RelationshipKey: $"routing-ivr|{ivr.Id}",
                    ObjectType: "IVR",
                    ObjectId: ivr.Id,
                    ObjectName: ivr.Name,
                    NormalizedValue: $"dnis={JoinCanonical(normalizedDnis)};schedule={NormalizeSnapshotValue(ivr.ScheduleGroup?.Id)};open={NormalizeSnapshotValue(ivr.OpenHoursFlow?.Id)};closed={NormalizeSnapshotValue(ivr.ClosedHoursFlow?.Id)};holiday={NormalizeSnapshotValue(ivr.HolidayHoursFlow?.Id)}",
                    DisplayValue: $"DNIS={JoinDisplay(displayDnis)}; Schedule Group={FormatNameId(ivr.ScheduleGroup?.Name, ivr.ScheduleGroup?.Id)}; Open={FormatNameId(ivr.OpenHoursFlow?.Name, ivr.OpenHoursFlow?.Id)}; Closed={FormatNameId(ivr.ClosedHoursFlow?.Name, ivr.ClosedHoursFlow?.Id)}; Holiday={FormatNameId(ivr.HolidayHoursFlow?.Name, ivr.HolidayHoursFlow?.Id)}");
            })
            .ToList();

    private static IReadOnlyList<AuditRelationshipSnapshot> CaptureTopologyRelationshipSnapshots(
        IReadOnlyList<SiteDto> sites,
        IReadOnlyList<EdgeDto> edges,
        IReadOnlyList<TrunkDto> trunks)
    {
        var snapshots = new List<AuditRelationshipSnapshot>();

        foreach (var site in sites.Where(s => s.Id is not null))
        {
            var primaryEdgeIds = (site.PrimaryEdges ?? [])
                .Where(e => e.Id is not null)
                .Select(e => e.Id!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var secondaryEdgeIds = (site.SecondaryEdges ?? [])
                .Where(e => e.Id is not null)
                .Select(e => e.Id!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var primaryDisplay = (site.PrimaryEdges ?? [])
                .Select(e => FormatNameId(e.Name, e.Id))
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "(none)")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var secondaryDisplay = (site.SecondaryEdges ?? [])
                .Select(e => FormatNameId(e.Name, e.Id))
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "(none)")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            snapshots.Add(new AuditRelationshipSnapshot(
                Domain: "Topology Relationships",
                RelationshipType: "SiteEdgeMembership",
                RelationshipKey: $"topology-site|{site.Id}",
                ObjectType: "Site",
                ObjectId: site.Id,
                ObjectName: site.Name,
                NormalizedValue: $"location={NormalizeSnapshotValue(site.Location?.Id)};primary={JoinCanonical(primaryEdgeIds)};secondary={JoinCanonical(secondaryEdgeIds)}",
                DisplayValue: $"Location={FormatNameId(site.Location?.Name, site.Location?.Id)}; Primary Edges={JoinDisplay(primaryDisplay)}; Secondary Edges={JoinDisplay(secondaryDisplay)}"));
        }

        foreach (var edge in edges.Where(e => e.Id is not null))
        {
            snapshots.Add(new AuditRelationshipSnapshot(
                Domain: "Topology Relationships",
                RelationshipType: "EdgeSiteBinding",
                RelationshipKey: $"topology-edge|{edge.Id}",
                ObjectType: "Edge",
                ObjectId: edge.Id,
                ObjectName: edge.Name,
                NormalizedValue: $"site={NormalizeSnapshotValue(edge.Site?.Id)};online={NormalizeSnapshotValue(edge.OnlineStatus)};status={NormalizeSnapshotValue(edge.StatusCode)}",
                DisplayValue: $"Site={FormatNameId(edge.Site?.Name, edge.Site?.Id)}; Online={DisplayValue(edge.OnlineStatus)}; Status={DisplayValue(edge.StatusCode)}"));
        }

        foreach (var trunk in trunks.Where(t => t.Id is not null))
        {
            snapshots.Add(new AuditRelationshipSnapshot(
                Domain: "Topology Relationships",
                RelationshipType: "TrunkEdgeBinding",
                RelationshipKey: $"topology-trunk|{trunk.Id}",
                ObjectType: "Trunk",
                ObjectId: trunk.Id,
                ObjectName: trunk.Name,
                NormalizedValue: $"edge={NormalizeSnapshotValue(trunk.Edge?.Id)};state={NormalizeSnapshotValue(trunk.TrunkState)};inService={NormalizeSnapshotValue(trunk.InService?.ToString())};enabled={NormalizeSnapshotValue(trunk.Enabled?.ToString())}",
                DisplayValue: $"Edge={FormatNameId(trunk.Edge?.Name, trunk.Edge?.Id)}; State={DisplayValue(trunk.TrunkState)}; In Service={DisplayBool(trunk.InService)}; Enabled={DisplayBool(trunk.Enabled)}"));
        }

        return snapshots;
    }

    // ─── Phase 1.2 — User telephony integrity ────────────────────────────────

    /// <summary>
    /// Correlates each user's profile extension, station assignment, and DID ownership
    /// to detect contradictions that indicate misconfiguration or platform sync issues.
    /// Uses only already-fetched data — no additional API calls.
    /// </summary>
    private static IReadOnlyList<UserTelephonyIntegrityFinding> AnalyzeUserTelephonyIntegrity(
        IReadOnlyList<GenesysUserDto> users,
        IReadOnlyList<EdgeExtensionEntityDto> extensions,
        IReadOnlyList<DidDto> dids)
    {
        var findings = new List<UserTelephonyIntegrityFinding>();

        // Build lookup: normalized extension key → user IDs that own the assignment
        var assignedExtToUserIds = extensions
            .Where(e => e.Id is not null
                && string.Equals(e.AssignedTo?.Type, "USER", StringComparison.OrdinalIgnoreCase)
                && e.AssignedTo?.Id is not null)
            .GroupBy(e => e.Extension?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key, g => g.Select(e => e.AssignedTo!.Id!).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        // Build lookup: user ID → DIDs assigned to that user
        var didsByUserId = dids
            .Where(d => d.Owner is not null
                && string.Equals(d.Owner.Type, "User", StringComparison.OrdinalIgnoreCase)
                && d.Owner.Id is not null)
            .GroupBy(d => d.Owner!.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var user in users)
        {
            if (user.Id is null) continue;

            var profileExt = ExtractWorkPhoneExtension(user);
            var stationId = user.Station?.Id;
            var stationName = user.Station?.Name;
            var hasProfileExt = !string.IsNullOrWhiteSpace(profileExt);
            var hasStation = !string.IsNullOrWhiteSpace(stationId);

            // Check 1: User has a work-phone extension on profile but no station assignment.
            // Indicates telephony provisioning is incomplete or the station was removed
            // after the extension was configured.
            if (hasProfileExt && !hasStation)
            {
                findings.Add(new UserTelephonyIntegrityFinding(
                    UserId: user.Id,
                    UserName: user.Name,
                    Email: user.Email,
                    UserState: user.State,
                    ProfileExtensionRaw: profileExt,
                    StationId: null,
                    StationName: null,
                    RelatedDidNumber: null,
                    FindingCode: TelephonyIntegrityCode.ExtensionWithoutStation,
                    Issue: $"User has work-phone extension '{profileExt}' on profile but no station is assigned. " +
                           "Telephony provisioning appears incomplete.",
                    Severity: FindingSeverity.High,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Assign a station to this user or remove the extension from the profile if telephony is no longer needed."));
            }

            // Check 2: User has a station assigned but no work-phone extension on their profile.
            // A station without a mapped extension number means the station cannot be reached.
            if (hasStation && !hasProfileExt)
            {
                findings.Add(new UserTelephonyIntegrityFinding(
                    UserId: user.Id,
                    UserName: user.Name,
                    Email: user.Email,
                    UserState: user.State,
                    ProfileExtensionRaw: null,
                    StationId: stationId,
                    StationName: stationName,
                    RelatedDidNumber: null,
                    FindingCode: TelephonyIntegrityCode.StationWithoutExtension,
                    Issue: $"User has station '{stationName ?? stationId}' assigned but no work-phone extension on the profile. " +
                           "The station cannot be dialled by extension.",
                    Severity: FindingSeverity.Medium,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Add a work-phone extension to this user's profile contact info to complete the telephony configuration."));
            }

            // Check 3: A DID is assigned (by owner user ID) to this user, but the DID's phone number
            // does not appear on any of the user's profile contact info fields.
            // This means the DID assignment and the user's identity disagree — either the DID
            // was moved without updating the profile, or the profile was updated without re-assigning the DID.
            if (didsByUserId.TryGetValue(user.Id, out var userDids))
            {
                var profileNumbers = GetAllWorkPhoneContactInfo(user)
                    .Select(ci => NormalizePhoneNumber(ci.Address ?? string.Empty))
                    .Where(n => n is not null)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

                foreach (var did in userDids)
                {
                    if (string.IsNullOrWhiteSpace(did.PhoneNumber)) continue;
                    var normalized = NormalizePhoneNumber(did.PhoneNumber);
                    if (normalized is null) continue;

                    if (!profileNumbers.Contains(normalized))
                    {
                        findings.Add(new UserTelephonyIntegrityFinding(
                            UserId: user.Id,
                            UserName: user.Name,
                            Email: user.Email,
                            UserState: user.State,
                            ProfileExtensionRaw: profileExt,
                            StationId: stationId,
                            StationName: stationName,
                            RelatedDidNumber: did.PhoneNumber,
                            FindingCode: TelephonyIntegrityCode.DidOwnerExtensionMismatch,
                            Issue: $"DID {did.PhoneNumber} is assigned to this user in the telephony system, " +
                                   "but that number does not appear on the user's profile contact info. " +
                                   "The DID assignment and user profile are out of sync.",
                            Severity: FindingSeverity.High,
                            Category: FindingCategory.LocalConfigFix,
                            RecommendedAction: "Add the DID number to the user's profile contact info, or re-assign the DID to the correct user."));
                    }
                }
            }
        }

        return findings
            .OrderBy(f => f.FindingCode)
            .ThenBy(f => f.UserName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ─── Phase 1.3 — Queue serviceability ────────────────────────────────────

    /// <summary>
    /// For each non-empty queue (within the configured member cap), fetches the first page
    /// of joined members and cross-references them against the user active-state lookup.
    /// Queues where all checked members are inactive or unresolvable are flagged as
    /// non-serviceable.
    /// </summary>
    private async Task<IReadOnlyList<QueueServiceabilityFinding>> AnalyzeQueueServiceabilityAsync(
        IReadOnlyList<QueueDto> queues,
        IReadOnlyList<GenesysUserDto> users,
        AuditRunOptions options,
        CancellationToken ct)
    {
        var findings = new List<QueueServiceabilityFinding>();

        // Build user lookup for fast cross-reference
        var userById = users
            .Where(u => u.Id is not null)
            .ToDictionary(u => u.Id!, u => u, StringComparer.OrdinalIgnoreCase);

        var memberPageSize = Math.Clamp(options.QueueServiceabilityMemberPageSize, 1, 100);
        var maxMembersToCheck = options.QueueServiceabilityMaxMembersToCheck;

        // Separate queues that exceed the configured size cap — emit a warning finding for each
        // rather than silently skipping, so operators know large queues were not examined.
        var nonEmptyQueues = queues
            .Where(q => q.Id is not null && (q.MemberCount ?? 0) > 0)
            .ToList();

        var oversizedQueues = maxMembersToCheck > 0
            ? nonEmptyQueues.Where(q => (q.MemberCount ?? 0) > maxMembersToCheck).ToList()
            : [];

        foreach (var q in oversizedQueues)
        {
            findings.Add(new QueueServiceabilityFinding(
                QueueId: q.Id!,
                QueueName: q.Name,
                TotalMembersOnRecord: q.MemberCount ?? 0,
                MembersChecked: 0,
                ActiveMemberCount: 0,
                InactiveMemberCount: 0,
                UnresolvableMemberCount: 0,
                FindingCode: QueueServiceabilityCode.TooLargeToCheck,
                Issue: $"Queue '{q.Name ?? q.Id}' has {q.MemberCount ?? 0} members which exceeds the configured " +
                       $"check cap of {maxMembersToCheck}. Serviceability was not verified — investigate manually " +
                       "or raise QueueServiceabilityMaxMembersToCheck.",
                Severity: FindingSeverity.Info,
                Category: FindingCategory.MonitorRerun,
                RecommendedAction: "Review queue membership manually, or increase QueueServiceabilityMaxMembersToCheck to include this queue in future audits."));
        }

        var candidateQueues = maxMembersToCheck > 0
            ? nonEmptyQueues.Where(q => (q.MemberCount ?? 0) <= maxMembersToCheck).ToList()
            : nonEmptyQueues;

        _logger.LogInformation(
            "Queue serviceability: checking {Count} queues (of {Total} total); {Oversized} oversized queues flagged as informational",
            candidateQueues.Count, queues.Count, oversizedQueues.Count);

        // Fetch members for candidate queues with bounded concurrency to respect rate limits
        var semaphore = new SemaphoreSlim(5, 5);

        var tasks = candidateQueues.Select(async queue =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                var page = await _queueMembersClient
                    .GetQueueMembersPageAsync(queue.Id!, 1, memberPageSize, ct)
                    .ConfigureAwait(false);

                var members = page.Entities ?? [];
                if (members.Count == 0) return null;

                int active = 0, inactive = 0, unresolvable = 0;

                foreach (var member in members)
                {
                    // The user ID is the top-level "id" field on the member entity
                    var userId = member.Id
                        ?? member.User?.Id;

                    if (userId is null)
                    {
                        unresolvable++;
                        continue;
                    }

                    if (!userById.TryGetValue(userId, out var userDto))
                    {
                        // Not in our user list — either fetched inactive-excluded or genuinely gone
                        unresolvable++;
                        continue;
                    }

                    if (string.Equals(userDto.State, "inactive", StringComparison.OrdinalIgnoreCase))
                        inactive++;
                    else
                        active++;
                }

                var membersChecked = members.Count;
                var allNonServiceable = active == 0;

                if (!allNonServiceable) return null;

                string findingCode;
                string issue;

                if (inactive > 0 && unresolvable == 0)
                {
                    findingCode = QueueServiceabilityCode.AllInactive;
                    issue = $"All {membersChecked} checked member(s) are inactive. Queue cannot service work.";
                }
                else if (unresolvable > 0 && inactive == 0)
                {
                    findingCode = QueueServiceabilityCode.AllUnresolvable;
                    issue = $"None of the {membersChecked} checked member(s) could be resolved to an active user. " +
                            "Queue serviceability is unknown. Members may be excluded because IncludeInactiveUsers=false.";
                }
                else
                {
                    findingCode = QueueServiceabilityCode.MixedDegraded;
                    issue = $"Checked {membersChecked} member(s): {inactive} inactive, {unresolvable} unresolvable, 0 active. Queue cannot service work.";
                }

                var severity = inactive + unresolvable == membersChecked
                    ? FindingSeverity.High
                    : FindingSeverity.Medium;

                return new QueueServiceabilityFinding(
                    QueueId: queue.Id!,
                    QueueName: queue.Name,
                    TotalMembersOnRecord: queue.MemberCount ?? membersChecked,
                    MembersChecked: membersChecked,
                    ActiveMemberCount: active,
                    InactiveMemberCount: inactive,
                    UnresolvableMemberCount: unresolvable,
                    FindingCode: findingCode,
                    Issue: issue,
                    Severity: severity,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Review queue membership. Remove inactive users and add active, appropriately-skilled agents.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch members for queue {QueueId}. Skipping.", queue.Id);
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        findings.AddRange(results.Where(f => f is not null)!);

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.InactiveMemberCount)
            .ThenBy(f => f.QueueName)
            .ToList();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static string? ExtractWorkPhoneExtension(GenesysUserDto user)
    {
        if (user.PrimaryContactInfo is null or { Count: 0 }) return null;

        var candidates = user.PrimaryContactInfo
            .Where(ci => string.Equals(ci.MediaType, "PHONE", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ci => string.Equals(ci.Type, "work", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

        foreach (var ci in candidates)
        {
            if (!string.IsNullOrWhiteSpace(ci.Extension))
                return ci.Extension.Trim();
        }

        return null;
    }

    private static IEnumerable<GenesysPrimaryContactInfoDto> GetAllWorkPhoneContactInfo(GenesysUserDto user)
    {
        static bool IsWorkPhone(GenesysPrimaryContactInfoDto ci)
            => string.Equals(ci.MediaType, "PHONE", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(ci.Type)
               && ci.Type.StartsWith("work", StringComparison.OrdinalIgnoreCase);

        foreach (var ci in user.PrimaryContactInfo ?? [])
        {
            if (IsWorkPhone(ci))
                yield return ci;
        }

        foreach (var ci in user.Addresses ?? [])
        {
            if (IsWorkPhone(ci))
                yield return ci;
        }
    }

    private static string NormalizeSnapshotValue(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? "-" : raw.Trim();

    private static string DisplayValue(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? "(none)" : raw.Trim();

    private static string JoinCanonical(IEnumerable<string> values)
    {
        var list = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count == 0 ? "-" : string.Join("|", list);
    }

    private static string JoinDisplay(IEnumerable<string> values)
    {
        var list = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    private static string FormatNameId(string? name, string? id)
    {
        var trimmedName = name?.Trim();
        var trimmedId = id?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedName) && !string.IsNullOrWhiteSpace(trimmedId))
            return $"{trimmedName} ({trimmedId})";
        if (!string.IsNullOrWhiteSpace(trimmedName))
            return trimmedName;
        if (!string.IsNullOrWhiteSpace(trimmedId))
            return trimmedId;
        return "(none)";
    }

    private static string FormatOwner(string? ownerType, string? ownerId)
    {
        var type = string.IsNullOrWhiteSpace(ownerType) ? "Unassigned" : ownerType.Trim().ToUpperInvariant();
        var id = string.IsNullOrWhiteSpace(ownerId) ? null : ownerId.Trim();
        return id is null ? type : $"{type}:{id}";
    }

    private static string DisplayBool(bool? value)
        => value.HasValue ? (value.Value ? "True" : "False") : "(unknown)";

    private static string? NormalizePhoneNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Keep only digits for comparison
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : null;
    }

    private static DateTimeOffset? GetTokenLastIssuedDate(GenesysUserDto user)
        => user.TokenLastIssuedDate ?? user.TokenLastIssuedDateLegacy;

    private static IReadOnlyList<AuditLogFinding> AnalyzeAuditLogs(IReadOnlyList<JsonElement> records)
    {
        var findings = new List<AuditLogFinding>(records.Count);

        foreach (var record in records)
        {
            if (record.ValueKind != JsonValueKind.Object)
                continue;

            var map = record.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

            map.TryGetValue("id", out var idValue);

            // When expand=user was requested the user details appear in a nested "user" object.
            string? userId = GetString(map, "userId");
            string? userName = GetString(map, "userName");
            string? userEmail = GetString(map, "userEmail");
            if (map.TryGetValue("user", out var userEl) && userEl.ValueKind == JsonValueKind.Object)
            {
                var userMap = userEl.EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
                userId ??= GetString(userMap, "id");
                userName ??= GetString(userMap, "name", "displayName");
                userEmail ??= GetString(userMap, "email");
            }

            findings.Add(new AuditLogFinding(
                AuditId: AsString(idValue),
                TimestampUtc: ParseTimestamp(map),
                ServiceName: GetString(map, "serviceName"),
                Action: GetString(map, "action"),
                UserName: userName,
                UserEmail: userEmail,
                EntityType: GetString(map, "entityType", "targetType"),
                EntityName: GetString(map, "entityName", "targetName"),
                UserId: userId,
                ClientId: GetString(map, "clientId", "oauthClientId"),
                EntityId: GetNestedId(map, "entity") ?? GetString(map, "entityId", "targetId"),
                CorrelationId: GetString(map, "correlationId"),
                Level: GetString(map, "level")));
        }

        return findings
            .OrderByDescending(f => f.TimestampUtc ?? DateTimeOffset.MinValue)
            .ToList();

        static string? GetString(Dictionary<string, JsonElement> map, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (map.TryGetValue(key, out var value))
                {
                    var text = AsString(value);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        static DateTimeOffset? ParseTimestamp(Dictionary<string, JsonElement> map)
        {
            var raw = GetString(map, "dateIssued", "timestamp", "eventTime", "dateCreated");
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return DateTimeOffset.TryParse(raw, out var ts) ? ts : null;
        }

        static string? AsString(JsonElement element)
            => element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

        // Extract the "id" field from a named nested object (e.g. "entity": {"id": "..."}).
        // The Genesys Cloud audit log API places entity identity inside a nested object.
        static string? GetNestedId(Dictionary<string, JsonElement> map, string objectKey)
        {
            if (!map.TryGetValue(objectKey, out var nested) ||
                nested.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in nested.EnumerateObject())
            {
                if (string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase))
                    return AsString(prop.Value);
            }

            return null;
        }
    }

    private static IReadOnlyList<OperationalEventFinding> AnalyzeOperationalEvents(
        IReadOnlyList<OperationalEventDto> records)
    {
        return records
            .Select(r => new OperationalEventFinding(
                TimestampUtc: r.DateCreated,
                EventDefinitionId: r.EventDefinition?.Id,
                EventDefinitionName: r.EventDefinition?.Name,
                EntityId: r.EntityId,
                EntityName: r.EntityName,
                CurrentValue: r.CurrentValue,
                PreviousValue: r.PreviousValue,
                ErrorCode: r.ErrorCode,
                ConversationId: r.Conversation?.Id))
            .OrderByDescending(f => f.TimestampUtc ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static IReadOnlyList<OutboundEventFinding> AnalyzeOutboundEvents(
        IReadOnlyList<OutboundEventDto> records)
    {
        return records
            .Select(r => new OutboundEventFinding(
                TimestampUtc: r.Timestamp,
                EventId: r.Id,
                Name: r.Name,
                Category: r.Category,
                Level: r.Level,
                Code: r.EventMessage?.Code,
                Message: r.EventMessage?.Message,
                CorrelationId: r.CorrelationId))
            .OrderByDescending(f => f.TimestampUtc ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static string? ExtractCursorValue(string? uriOrPath, string key)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath) || string.IsNullOrWhiteSpace(key))
            return null;

        var query = string.Empty;
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            query = absolute.Query;
        }
        else
        {
            var idx = uriOrPath.IndexOf('?', StringComparison.Ordinal);
            if (idx >= 0 && idx < uriOrPath.Length - 1)
                query = uriOrPath[(idx + 1)..];
        }

        if (string.IsNullOrWhiteSpace(query))
            return null;

        var trimmedQuery = query.TrimStart('?');
        var segments = trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[1]);
        }

        return null;
    }

    // ─── Phase 2 — Architect Prompt Hygiene ──────────────────────────────────

    /// <summary>
    /// Flags architect prompts that cannot produce audio for any caller.
    /// Two cases are detected:
    /// <list type="bullet">
    ///   <item>PROMPT_NO_RESOURCES — no language resource slots configured at all.</item>
    ///   <item>PROMPT_NO_PLAYABLE_MEDIA — every configured language slot has neither a media file nor a TTS string.</item>
    /// </list>
    /// System prompts are included in the check because misconfigured system prompts
    /// override Genesys defaults and can cause silent failures.
    /// </summary>
    private static IReadOnlyList<PromptHygieneFinding> AnalyzePromptHygiene(
        IReadOnlyList<PromptDto> prompts)
        => new PromptHygieneAnalyzer().Analyze(prompts);

    private static void Report(IProgress<AuditProgress> progress, int percent, string message, string? status = null)
    {
        progress.Report(new AuditProgress
        {
            Percent = percent,
            Message = message,
            Status = status
        });
    }

    // ─── Phase 1 Identity & License Hygiene — helpers ────────────────────────

    /// <summary>
    /// Maps fetched GenesysUserDto list to the minimal UserRecord input required by
    /// <see cref="LicenseHygieneAnalyzer"/>.
    /// </summary>
    private static IReadOnlyList<LicenseHygieneAnalyzer.UserRecord> BuildUserRecords(
        IReadOnlyList<GenesysUserDto> users)
    {
        return users
            .Where(u => u.Id is not null)
            .Select(u => new LicenseHygieneAnalyzer.UserRecord(
                UserId: u.Id!,
                UserName: u.Name,
                Email: u.Email,
                State: u.State,
                TokenLastIssuedDate: GetTokenLastIssuedDate(u)))
            .ToList();
    }

    /// <summary>
    /// Maps fetched LicenseUserDto list to the minimal LicenseAssignment input required by
    /// <see cref="LicenseHygieneAnalyzer"/>.
    /// </summary>
    private static IReadOnlyList<LicenseHygieneAnalyzer.LicenseAssignment> BuildLicenseAssignments(
        IReadOnlyList<LicenseUserDto> licenseUsers)
    {
        return licenseUsers
            .Where(lu => lu.Id is not null)
            .Select(lu => new LicenseHygieneAnalyzer.LicenseAssignment(
                UserId: lu.Id!,
                Licenses: (IReadOnlyList<string>)(lu.Licenses ?? [])))
            .ToList();
    }

    /// <summary>
    /// Fetches role subjects for a sample of users (bounded by
    /// <see cref="AuditRunOptions.RoleGroupOverlapMaxUsersToCheck"/>) and identifies cases
    /// where a direct role assignment is already covered by a group-inherited role
    /// in the same division.
    /// </summary>
    private async Task<IReadOnlyList<RoleGroupOverlapFinding>> AnalyzeRoleGroupOverlapAsync(
        IReadOnlyList<GenesysUserDto> users,
        AuditRunOptions options,
        CancellationToken ct)
    {
        var maxUsers = options.RoleGroupOverlapMaxUsersToCheck;
        var candidates = maxUsers > 0
            ? users.Where(u => u.Id is not null).Take(maxUsers).ToList()
            : users.Where(u => u.Id is not null).ToList();

        _logger.LogInformation(
            "Role & group overlap: checking {Count} users (of {Total} total)",
            candidates.Count, users.Count);

        // Build a user metadata lookup for enriching findings
        var userMeta = users
            .Where(u => u.Id is not null)
            .ToDictionary(
                u => u.Id!,
                u => (Name: u.Name, Email: u.Email, State: u.State),
                StringComparer.OrdinalIgnoreCase);

        var semaphore = new SemaphoreSlim(5, 5);
        var subjectTasks = candidates.Select(async user =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                var rolesResponse = await _userRolesClient
                    .GetUserRolesAsync(user.Id!, ct)
                    .ConfigureAwait(false);

                return BuildUserRoleSubjects(user.Id!, rolesResponse);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to fetch roles for user {UserId}. Skipping.", user.Id);
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(subjectTasks).ConfigureAwait(false);
        var validSubjects = results.Where(s => s is not null).Select(s => s!).ToList();

        var analyzer = new LicenseHygieneAnalyzer();
        return analyzer.AnalyzeRoleGroupOverlap(validSubjects, userMeta);
    }

    /// <summary>
    /// Converts the raw API response for a single user's roles into the
    /// <see cref="LicenseHygieneAnalyzer.UserRoleSubjects"/> domain model.
    /// </summary>
    private static LicenseHygieneAnalyzer.UserRoleSubjects BuildUserRoleSubjects(
        string userId,
        UserRolesResponseDto response)
    {
        var directGrants = new List<LicenseHygieneAnalyzer.RoleGrant>();
        var groupSubjects = new List<LicenseHygieneAnalyzer.GroupRoleSubject>();

        foreach (var subject in response.Entities ?? [])
        {
            if (subject.Id is null) continue;

            var grants = (subject.Grants ?? [])
                .Where(g => g.Role?.Id is not null)
                .Select(g => new LicenseHygieneAnalyzer.RoleGrant(
                    RoleId: g.Role!.Id!,
                    RoleName: g.Role.Name,
                    DivisionId: g.Division?.Id,
                    DivisionName: g.Division?.Name))
                .ToList();

            if (string.Equals(subject.Type, "USER", StringComparison.OrdinalIgnoreCase))
            {
                directGrants.AddRange(grants);
            }
            else if (string.Equals(subject.Type, "GROUP", StringComparison.OrdinalIgnoreCase))
            {
                groupSubjects.Add(new LicenseHygieneAnalyzer.GroupRoleSubject(
                    GroupId: subject.Id,
                    GroupName: subject.Name,
                    Grants: grants));
            }
        }

        return new LicenseHygieneAnalyzer.UserRoleSubjects(
            UserId: userId,
            DirectGrants: directGrants,
            GroupSubjects: groupSubjects);
    }
}
