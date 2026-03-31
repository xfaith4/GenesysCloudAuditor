using ClosedXML.Excel;
using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Services;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

public interface IExcelReportService
{
    Task<byte[]> GenerateAsync(AuditReportData report, CancellationToken ct, ExcelWorkbookScopeOptions? scopeOptions = null, CareEvidencePacket? carePacket = null);
}

public sealed class ExcelWorkbookScopeOptions
{
    public bool IncludeSummary { get; init; } = true;
    public bool IncludeExtensions { get; init; } = true;
    public bool IncludeGroups { get; init; } = true;
    public bool IncludeQueues { get; init; } = true;
    public bool IncludeFlows { get; init; } = true;
    public bool IncludeInactiveUsers { get; init; } = true;
    public bool IncludeDids { get; init; } = true;
    public bool IncludeAuditLogs { get; init; } = true;
    public bool IncludeOperationalEvents { get; init; } = true;
    public bool IncludeOutboundEvents { get; init; } = true;
    public bool IncludeStaleLicenses { get; init; } = true;
    public bool IncludeLicenseOverProvisioning { get; init; } = true;
    public bool IncludeRoleGroupOverlap { get; init; } = true;
    public bool IncludeSiteTopology { get; init; } = true;
    public bool IncludePromptHygiene { get; init; } = true;
    public bool IncludeChangeAdjacency { get; init; } = true;
    public bool IncludeFlappingDetection { get; init; } = true;
    public bool IncludeHotSpot { get; init; } = true;
    public bool IncludeFindingLifecycle { get; init; } = true;
    public bool IncludeHistoricalDrift { get; init; } = true;
}

/// <summary>
/// Generates a single Excel workbook containing all audit findings.
/// Each category gets its own worksheet with a consistent, professional layout:
///   Row 1 — title band (merged, bold, colored)
///   Row 2 — generated timestamp + org region + finding count
///   Row 3 — column headers (frozen, auto-filter, bold, white-on-dark)
///   Row 4+ — data rows (alternating background)
/// </summary>
public sealed class ExcelReportService : IExcelReportService
{
    // Brand palette
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#1F3864");     // dark navy
    private static readonly XLColor HeaderFg = XLColor.FromHtml("#FFFFFF");
    private static readonly XLColor TitleBg = XLColor.FromHtml("#2E75B6");      // brand blue
    private static readonly XLColor AltRowBg = XLColor.FromHtml("#EBF3FB");     // light blue tint
    private static readonly XLColor SeverityCritical = XLColor.FromHtml("#FFCCCC");
    private static readonly XLColor SeverityWarning = XLColor.FromHtml("#FFF2CC");
    private static readonly XLColor SeverityInfo = XLColor.FromHtml("#E2F0D9");

    public Task<byte[]> GenerateAsync(AuditReportData report, CancellationToken ct, ExcelWorkbookScopeOptions? scopeOptions = null, CareEvidencePacket? carePacket = null)
    {
        ct.ThrowIfCancellationRequested();
        scopeOptions ??= new ExcelWorkbookScopeOptions();

        using var wb = new XLWorkbook();

        if (scopeOptions.IncludeSummary)
            WriteSummarySheet(wb, report);

        // Care Case Summary is always written first (after overview summary) when a packet is provided.
        // Intentionally not gated by scope options — escalation candidates should always be visible.
        if (carePacket is not null)
            WriteCareCaseSummarySheet(wb, report, carePacket);

        if (scopeOptions.IncludeExtensions)
        {
            WriteExtDuplicatesProfileSheet(wb, report);
            WriteExtOwnershipMismatchSheet(wb, report);
            WriteExtAssignVsProfileSheet(wb, report);
            WriteUserTelephonyIntegritySheet(wb, report);
            WriteInvalidExtensionsSheet(wb, report);
        }

        if (scopeOptions.IncludeDids)
            WriteDidMismatchSheet(wb, report);

        if (scopeOptions.IncludeFlows)
        {
            WriteStaleFlowsSheet(wb, report);
            WriteIvrFlowBindingsSheet(wb, report);
        }

        if (scopeOptions.IncludeQueues)
        {
            WriteEmptyQueuesSheet(wb, report);
            WriteQueueServiceabilitySheet(wb, report);
        }

        if (scopeOptions.IncludeGroups)
            WriteEmptyGroupsSheet(wb, report);

        if (scopeOptions.IncludeInactiveUsers)
        {
            WriteStaleTokenUsersSheet(wb, report);
            WriteUsersMissingLocationSheet(wb, report);
        }

        if (scopeOptions.IncludeAuditLogs)
            WriteAuditLogsSheet(wb, report);

        if (scopeOptions.IncludeOperationalEvents)
            WriteOperationalEventsSheet(wb, report);

        if (scopeOptions.IncludeOutboundEvents)
            WriteOutboundEventsSheet(wb, report);

        if (scopeOptions.IncludeStaleLicenses)
            WriteStaleLicensesSheet(wb, report);

        if (scopeOptions.IncludeLicenseOverProvisioning)
            WriteLicenseOverProvisioningSheet(wb, report);

        if (scopeOptions.IncludeRoleGroupOverlap)
            WriteRoleGroupOverlapSheet(wb, report);

        if (scopeOptions.IncludeSiteTopology)
            WriteSiteTopologySheet(wb, report);

        if (scopeOptions.IncludePromptHygiene)
            WritePromptHygieneSheet(wb, report);

        if (scopeOptions.IncludeChangeAdjacency)
            WriteChangeAdjacencySheet(wb, report);

        if (scopeOptions.IncludeFlappingDetection)
            WriteFlappingDetectionSheet(wb, report);

        if (scopeOptions.IncludeHotSpot)
            WriteHotSpotSheet(wb, report);

        if (scopeOptions.IncludeFindingLifecycle && report.FindingLifecycleWasComputed)
            WriteFindingLifecycleSheet(wb, report);

        if (scopeOptions.IncludeHistoricalDrift && report.HistoricalDriftWasComputed)
            WriteHistoricalDriftSheet(wb, report);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    // ─── Summary ────────────────────────────────────────────────────────────

    private static void WriteSummarySheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Summary");

        var er = report.ExtensionReport;

        var rows = new[]
        {
            ("Ext_Duplicates_Profile", "Extension Duplicates (Profile)", report.Options.RunExtensionAudit, er.DuplicateProfileExtensions.Count, "Critical", "Multiple users share the same work-phone extension"),
            ("Ext_Ownership_Mismatch", "Extension Ownership Mismatches", report.Options.RunExtensionAudit, er.ExtensionAssignedToWrongEntity.Count, "Critical", "Extension on user profile is assigned to a different entity in the telephony system (platform bug)"),
            ("Ext_Assign_vs_Profile", "Extension Assignment vs Profile Mismatches", report.Options.RunExtensionAudit, er.ProfileExtensionsNotAssigned.Count + er.AssignedExtensionsMissingFromProfiles.Count, "Warning", "Extensions in assignments not on profiles, or on profiles not in assignments"),
            ("Invalid_Extensions", "Invalid Extension Values", report.Options.RunExtensionAudit, er.InvalidProfileExtensions.Count + er.InvalidAssignedExtensions.Count, "Warning", "Profile/assignment extension values that failed normalization"),
            ("Empty_Groups", "Empty/Single-Member Groups", report.Options.RunGroupAudit, report.GroupFindings.Count, "Warning", "Groups with zero or one member"),
            ("Empty_Queues", "Empty/Duplicate Queues", report.Options.RunQueueAudit, report.QueueFindings.Count, "Warning", "Queues with zero members or duplicate names"),
            ("Stale_Flows", "Stale/Unpublished Flows", report.Options.RunFlowAudit, report.FlowFindings.Count, "Warning", $"Flows not republished in {report.Options.StaleFlowThresholdDays}+ days or never published"),
            ("Stale_Tokens", "Users with Stale Token", report.Options.RunInactiveUserAudit, report.InactiveUserFindings.Count, "Warning", $"Users with token last-issued older than {report.Options.InactiveUserThresholdDays} days"),
            ("Users_No_Location", "Users Missing Location", report.Options.RunInactiveUserAudit, report.NoLocationUserFindings.Count, "Warning", "Users with no location configured on their account"),
            ("DID_Mismatches", "DID Mismatches", report.Options.RunDidAudit, report.DidFindings.Count, "Warning", "DIDs unassigned, orphaned, or assigned to inactive users"),
            ("IVR_Flow_Bindings", "IVR Flow Bindings", report.Options.RunFlowDependencyAudit, report.IvrFlowBindingFindings.Count, "Critical", "IVR entry points bound to draft, stale, or deleted flows — callers may be unable to connect"),
            ("User_Telephony_Integrity", "User Telephony Integrity", report.Options.RunUserTelephonyAudit, report.UserTelephonyIntegrityFindings.Count, "High", "User extension / station / DID ownership contradictions across API surfaces"),
            ("Queue_Serviceability", "Queue Serviceability", report.Options.RunQueueServiceabilityAudit, report.QueueServiceabilityFindings.Count, "High", "Queues with zero active or resolvable members — cannot service work"),
            ("Audit_Logs", "Audit Logs Events", report.Options.RunAuditLogs, report.AuditLogFindings.Count, "Info", "Audit transaction events returned from Genesys audit logs query"),
            ("Operational_Events", "Operational Event Logs", report.Options.RunOperationalEventLogs, report.OperationalEventFindings.Count, "Info", $"Operational events from last {report.Options.OperationalEventLookbackDays} day(s)"),
            ("Outbound_Events", "Outbound Events", report.Options.RunOutboundEvents, report.OutboundEventFindings.Count, "Info", "Outbound event logs"),
            ("Stale_Licenses", "Stale License Usage", report.Options.RunStaleLicenseAudit, report.StaleLicenseFindings.Count, "Warning", $"Licensed users who have not logged in for >{report.Options.StaleLicenseThresholdDays} days — potential license waste"),
            ("License_Over_Provisioning", "License Over-Provisioning", report.Options.RunLicenseOverProvisioningAudit, report.LicenseOverProvisioningFindings.Count, "Warning", "Users on CX3/WEM/Outbound tier with no recent login — consider downgrading tier"),
            ("Role_Group_Overlap", "Role & Group Overlap", report.Options.RunRoleGroupOverlapAudit, report.RoleGroupOverlapFindings.Count, "Warning", "Direct role assignments that are already covered by a group-inherited role in the same division"),
            ("Site_Topology", "Site–Edge–Trunk Topology", report.Options.RunSiteTopologyAudit, report.SiteTopologyFindings.Count, "Critical", "Sites with no active edges, offline edges, orphaned edge–site bindings, or trunks that are down/out-of-service"),
            ("Prompt_Hygiene", "Architect Prompt Hygiene", report.Options.RunPromptHygieneAudit, report.PromptHygieneFindings.Count, "Warning", "Prompts with no language resources or all resources missing both audio and TTS — callers will hear silence"),
            ("Finding_Lifecycle", "Finding Lifecycle", report.FindingLifecycleWasComputed, report.FindingLifecycleFindings.Count, "Info", "New, recurrent, and resolved findings compared to the previous saved snapshot"),
            ("Historical_Drift", "Historical Drift", report.HistoricalDriftWasComputed, report.HistoricalDriftFindings.Count, "High", "Material telephony, routing, and topology relationship changes compared to the previous saved snapshot"),
        };

        var totalFindings = rows.Where(r => r.Item3).Sum(r => r.Item4);
        var duration = report.RunCompletedAtUtc > report.RunStartedAtUtc
            ? report.RunCompletedAtUtc - report.RunStartedAtUtc
            : TimeSpan.Zero;

        string[] headers = ["Sheet", "Audit", "Performed", "Items", "Severity", "Description"];
        WriteSheetHeader(ws, "Genesys Cloud Audit — Executive Summary",
            report, totalFindings, headers);

        int row = 4;
        foreach (var (sheet, check, performed, count, severity, desc) in rows)
        {
            ws.Cell(row, 1).Value = sheet;
            ws.Cell(row, 2).Value = check;
            ws.Cell(row, 3).Value = performed ? "Yes" : "No";
            ws.Cell(row, 4).Value = count;
            ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 5).Value = severity;
            ws.Cell(row, 6).Value = desc;

            // Color-code severity + row
            var rowRange = ws.Range(row, 1, row, 6);
            var severityCell = ws.Cell(row, 5);
            if (severity is "Critical" or "High")
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (severity == "Warning")
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;
            else
                severityCell.Style.Fill.BackgroundColor = SeverityInfo;

            if (!performed)
                ws.Cell(row, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");

            if (row % 2 == 0)
            {
                foreach (var cell in rowRange.Cells().Where(c => c.Address.ColumnNumber != 5))
                    cell.Style.Fill.BackgroundColor = AltRowBg;
            }

            row++;
        }

        // Run metadata block
        ws.Cell(row + 1, 1).Value = "Run Start (UTC)";
        ws.Cell(row + 1, 2).Value = report.RunStartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(row + 2, 1).Value = "Run End (UTC)";
        ws.Cell(row + 2, 2).Value = report.RunCompletedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(row + 3, 1).Value = "Total Duration";
        ws.Cell(row + 3, 2).Value = duration.ToString(@"hh\:mm\:ss");
        ws.Range(row + 1, 1, row + 3, 1).Style.Font.Bold = true;

        ws.Column(1).Width = 24;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 10;
        ws.Column(5).Width = 12;
        AdjustColumns(ws, 6, minWidth: 10, maxWidth: 80);
    }

    // ─── Extension Duplicates (Profile) ────────────────────────────────────

    private static void WriteExtDuplicatesProfileSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Ext_Duplicates_Profile");
        var findings = report.ExtensionReport.DuplicateProfileExtensions;

        string[] headers = ["Extension", "User Name", "User ID", "State", "Extension (Raw)"];
        WriteSheetHeader(ws, "Duplicate Extensions — User Profiles", report, findings.Count, headers);

        int row = 4;
        foreach (var finding in findings)
        {
            foreach (var user in finding.Users)
            {
                WriteRow(ws, row, finding.ExtensionKey, user.UserName, user.UserId, user.State, user.ExtensionRaw);
                ApplyAltRow(ws, row, 5);
                row++;
            }
        }

        AdjustColumns(ws, 5);
    }

    // ─── Extension Ownership Mismatches ────────────────────────────────────────

    /// <summary>
    /// Reports users whose profile extension exists in the telephony assignment list but is
    /// assigned to a different entity — the primary known platform bug.
    /// </summary>
    private static void WriteExtOwnershipMismatchSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Ext_Ownership_Mismatch");
        var findings = report.ExtensionReport.ExtensionAssignedToWrongEntity;

        string[] headers = ["Extension Key", "User Name", "User ID", "User State", "Extension (Raw)", "Assigned To Type", "Assigned To ID"];
        WriteSheetHeader(ws, "Extension Ownership Mismatches — Profile vs Assignment", report, findings.Count, headers);

        int row = 4;
        foreach (var finding in findings)
        {
            foreach (var assignment in finding.ActualAssignments)
            {
                WriteRow(ws, row,
                    finding.ExtensionKey,
                    finding.User.UserName,
                    finding.User.UserId,
                    finding.User.State,
                    finding.User.ExtensionRaw,
                    assignment.TargetType,
                    assignment.TargetId);
                ApplyAltRow(ws, row, 7);
                ws.Cell(row, 1).Style.Fill.BackgroundColor = SeverityCritical;
                row++;
            }
        }

        AdjustColumns(ws, 7);
    }

    // ─── Extension Assignment vs Profile ────────────────────────────────────

    private static void WriteExtAssignVsProfileSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Ext_Assign_vs_Profile");
        var er = report.ExtensionReport;
        var totalCount = er.ProfileExtensionsNotAssigned.Count + er.AssignedExtensionsMissingFromProfiles.Count;

        string[] headers = ["Extension Key", "Issue Type", "Assignment ID", "User Name", "User ID", "Target Type"];
        WriteSheetHeader(ws, "Extension Assignment vs Profile Mismatches", report, totalCount, headers);

        int row = 4;
        foreach (var finding in er.ProfileExtensionsNotAssigned)
        {
            foreach (var user in finding.Users)
            {
                WriteRow(ws, row, finding.ExtensionKey, "On profile, not in assignments", "", user.UserName, user.UserId, "");
                ApplyAltRow(ws, row, 6);
                ws.Cell(row, 3).Style.Fill.BackgroundColor = SeverityWarning;
                row++;
            }
        }

        foreach (var finding in er.AssignedExtensionsMissingFromProfiles)
        {
            foreach (var a in finding.Assignments)
            {
                WriteRow(ws, row, finding.ExtensionKey, "In assignments, not on any profile", a.AssignmentId, "", "", a.TargetType);
                ApplyAltRow(ws, row, 6);
                ws.Cell(row, 3).Style.Fill.BackgroundColor = SeverityInfo;
                row++;
            }
        }

        AdjustColumns(ws, 6);
    }

    // ─── DID Mismatches ─────────────────────────────────────────────────────

    private static void WriteDidMismatchSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("DID_Mismatches");
        var findings = report.DidFindings;

        string[] headers = ["Phone Number", "Pool ID", "Owner Type", "Owner ID", "Owner Name", "Issue"];
        WriteSheetHeader(ws, "DID Mismatches", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            WriteRow(ws, row, f.PhoneNumber, f.PoolId, f.OwnerType, f.OwnerId, f.OwnerName, f.Issue);
            ApplyAltRow(ws, row, 6);
            row++;
        }

        AdjustColumns(ws, 6);
    }

    // ─── Audit Logs ──────────────────────────────────────────────────────────

    private static void WriteAuditLogsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Audit_Logs");
        var findings = report.AuditLogFindings;

        string[] headers =
        [
            "Timestamp (UTC)", "Service", "Action", "Level",
            "User Name", "User Email", "User ID", "Client ID",
            "Entity Type", "Entity Name", "Entity ID",
            "Correlation ID", "Audit ID"
        ];
        WriteSheetHeader(ws, "Audit Logs Events", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            WriteRow(
                ws,
                row,
                f.TimestampUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
                f.ServiceName,
                f.Action,
                f.Level,
                f.UserName,
                f.UserEmail,
                f.UserId,
                f.ClientId,
                f.EntityType,
                f.EntityName,
                f.EntityId,
                f.CorrelationId,
                f.AuditId);
            ApplyAltRow(ws, row, 13);
            row++;
        }

        AdjustColumns(ws, 13);
    }

    private static void WriteOperationalEventsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Operational_Events");
        var findings = report.OperationalEventFindings;

        string[] headers =
        [
            "Timestamp (UTC)", "Event Definition", "Event Definition ID", "Entity Name", "Entity ID",
            "Current Value", "Previous Value", "Error Code", "Conversation ID"
        ];

        WriteSheetHeader(
            ws,
            $"Operational Events (last {report.Options.OperationalEventLookbackDays} day(s))",
            report,
            findings.Count,
            headers);

        var row = 4;
        foreach (var f in findings)
        {
            WriteRow(
                ws,
                row,
                f.TimestampUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
                f.EventDefinitionName,
                f.EventDefinitionId,
                f.EntityName,
                f.EntityId,
                f.CurrentValue,
                f.PreviousValue,
                f.ErrorCode,
                f.ConversationId);
            ApplyAltRow(ws, row, 9);
            row++;
        }

        AdjustColumns(ws, 9);
    }

    private static void WriteOutboundEventsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Outbound_Events");
        var findings = report.OutboundEventFindings;

        string[] headers =
        [
            "Timestamp (UTC)", "Name", "Event ID", "Category", "Level", "Code", "Message", "Correlation ID"
        ];

        WriteSheetHeader(ws, "Outbound Events", report, findings.Count, headers);

        var row = 4;
        foreach (var f in findings)
        {
            WriteRow(
                ws,
                row,
                f.TimestampUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
                f.Name,
                f.EventId,
                f.Category,
                f.Level,
                f.Code,
                f.Message,
                f.CorrelationId);
            ApplyAltRow(ws, row, 8);
            row++;
        }

        AdjustColumns(ws, 8);
    }

    // ─── Empty Groups ───────────────────────────────────────────────────────

    private static void WriteEmptyGroupsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Empty_Groups");
        var findings = report.GroupFindings;

        string[] headers = ["Group Name", "Group ID", "Type", "State", "Members", "Last Modified", "Issue"];
        WriteSheetHeader(ws, "Groups — Empty or Single-Member", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.GroupName;
            ws.Cell(row, 2).Value = f.GroupId;
            ws.Cell(row, 3).Value = f.Type;
            ws.Cell(row, 4).Value = f.State;
            ws.Cell(row, 5).Value = f.MemberCount;
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Value = f.DateModified?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 7).Value = f.Issue;
            ApplyAltRow(ws, row, 7);
            if (f.MemberCount == 0)
                ws.Cell(row, 5).Style.Fill.BackgroundColor = SeverityCritical;
            row++;
        }

        AdjustColumns(ws, 7);
    }

    // ─── Empty Queues ───────────────────────────────────────────────────────

    private static void WriteEmptyQueuesSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Empty_Queues");
        var findings = report.QueueFindings;

        string[] headers = ["Queue Name", "Queue ID", "Description", "Members", "Issue"];
        WriteSheetHeader(ws, "Queues — Empty or Duplicate Names", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.QueueName;
            ws.Cell(row, 2).Value = f.QueueId;
            ws.Cell(row, 3).Value = f.Description;
            ws.Cell(row, 4).Value = f.MemberCount;
            ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 5).Value = f.Issue;
            ApplyAltRow(ws, row, 5);
            if (f.MemberCount == 0)
                ws.Cell(row, 4).Style.Fill.BackgroundColor = SeverityCritical;
            row++;
        }

        AdjustColumns(ws, 5);
    }

    // ─── Stale Flows ─────────────────────────────────────────────────────────

    private static void WriteStaleFlowsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Stale_Flows");
        var findings = report.FlowFindings;

        string[] headers = ["Flow Name", "Flow ID", "Type", "Published Date", "Days Since Published", "Last Modified", "Issue"];
        WriteSheetHeader(ws, $"Architect Flows — Stale (>{report.Options.StaleFlowThresholdDays} days) or Never Published",
            report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FlowName;
            ws.Cell(row, 2).Value = f.FlowId;
            ws.Cell(row, 3).Value = f.FlowType;
            ws.Cell(row, 4).Value = f.PublishedDate?.ToString("yyyy-MM-dd") ?? "Never";
            ws.Cell(row, 5).Value = f.DaysSincePublished.HasValue ? f.DaysSincePublished.Value : "N/A";
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Value = f.DateModified?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 7).Value = f.Issue;
            ApplyAltRow(ws, row, 7);
            if (!f.IsPublished)
                ws.Cell(row, 4).Style.Fill.BackgroundColor = SeverityWarning;
            row++;
        }

        AdjustColumns(ws, 7);
    }

    // ─── Inactive Users ──────────────────────────────────────────────────────

    private static void WriteStaleTokenUsersSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Stale_Tokens");
        var findings = report.InactiveUserFindings;

        string[] headers = ["User Name", "User ID", "Email", "State", "Token Last Issued (UTC)", "Days Since Issued", "Issue"];
        WriteSheetHeader(ws, $"Users — Token Last Issued Older Than {report.Options.InactiveUserThresholdDays} Days",
            report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.UserName;
            ws.Cell(row, 2).Value = f.UserId;
            ws.Cell(row, 3).Value = f.Email;
            ws.Cell(row, 4).Value = f.State;
            ws.Cell(row, 5).Value = f.TokenLastIssuedDate?.ToString("yyyy-MM-dd HH:mm") ?? "";
            ws.Cell(row, 6).Value = f.DaysSinceLogin.HasValue ? f.DaysSinceLogin.Value : "N/A";
            ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 7).Value = f.Issue;
            ApplyAltRow(ws, row, 7);
            row++;
        }

        AdjustColumns(ws, 7);
    }

    private static void WriteUsersMissingLocationSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Users_No_Location");
        var findings = report.NoLocationUserFindings;

        string[] headers = ["User Name", "User ID", "Email", "State", "Location Count", "Issue"];
        WriteSheetHeader(ws, "Users — Missing Location", report, findings.Count, headers);

        var row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.UserName;
            ws.Cell(row, 2).Value = f.UserId;
            ws.Cell(row, 3).Value = f.Email;
            ws.Cell(row, 4).Value = f.State;
            ws.Cell(row, 5).Value = f.LocationCount;
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Value = f.Issue;
            ApplyAltRow(ws, row, 6);
            row++;
        }

        AdjustColumns(ws, 6);
    }

    // ─── Invalid Extensions ──────────────────────────────────────────────────

    private static void WriteInvalidExtensionsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Invalid_Extensions");
        var er = report.ExtensionReport;
        var totalCount = er.InvalidProfileExtensions.Count + er.InvalidAssignedExtensions.Count;

        string[] headers = ["Source", "ID", "Name/Description", "Extension (Raw)", "Problem", "Notes"];
        WriteSheetHeader(ws, "Invalid Extension Values", report, totalCount, headers);

        int row = 4;
        foreach (var f in er.InvalidProfileExtensions)
        {
            WriteRow(ws, row, "Profile", f.UserId, f.UserName, f.ExtensionRaw, f.Status.ToString(), f.Notes);
            ApplyAltRow(ws, row, 6);
            row++;
        }

        foreach (var f in er.InvalidAssignedExtensions)
        {
            WriteRow(ws, row, "Assignment", f.AssignmentId, "", f.ExtensionRaw, f.Status.ToString(), f.Notes);
            ApplyAltRow(ws, row, 6);
            row++;
        }

        AdjustColumns(ws, 6);
    }

    // ─── Care Case Summary (Phase 3.3) ──────────────────────────────────────

    private static readonly XLColor CareTitleBg = XLColor.FromHtml("#833C00");    // dark amber-red
    private static readonly XLColor CareHeaderBg = XLColor.FromHtml("#C55A11");   // orange-red
    private static readonly XLColor CareAltRowBg = XLColor.FromHtml("#FCE4D6");   // light salmon tint

    private static void WriteCareCaseSummarySheet(IXLWorkbook wb, AuditReportData report, CareEvidencePacket packet)
    {
        var ws = wb.Worksheets.Add("Care_Case_Summary");
        var candidates = packet.EscalationCandidates;

        // ── Title ──────────────────────────────────────────────────────────────
        ws.Row(1).Height = 22;
        var titleCell = ws.Cell(1, 1);
        titleCell.Value = "Genesys Care Escalation Candidates";
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 14;
        titleCell.Style.Font.FontColor = XLColor.White;
        titleCell.Style.Fill.BackgroundColor = CareTitleBg;
        ws.Range(1, 1, 1, 20).Merge();

        // ── Subtitle (summary stats + timestamp) ──────────────────────────────
        ws.Row(2).Height = 15;
        ws.Cell(2, 1).Value =
            $"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm} UTC  |  " +
            $"Region: {report.OrgRegion}  |  " +
            $"Escalation Candidates: {packet.Summary.EscalationCandidateCount}  |  " +
            $"Ready: {packet.Summary.ReadyForCareCount}  Needs Review: {packet.Summary.NeedsReviewCount}  Monitor: {packet.Summary.MonitorCount}  |  " +
            $"Total Findings This Run: {packet.Summary.TotalFindingsInRun}  |  " +
            $"Critical: {packet.Summary.CriticalCount}  High: {packet.Summary.HighCount}  Medium: {packet.Summary.MediumCount}";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontSize = 10;
        ws.Range(2, 1, 2, 20).Merge();

        // ── Column headers ────────────────────────────────────────────────────
        ws.Row(3).Height = 18;
        string[] headers =
        [
            "Domain", "Finding Code", "Severity", "Support Readiness",
            "Readiness Score", "Confidence", "Blast Radius", "Suspected Owner",
            "Probable Cause", "Affected Object", "Affected Object ID", "Related Objects",
            "API Surfaces", "Recent Change Context", "Qualification Notes", "Evidence Summary",
            "Suggested Case Text", "Recommended Action", "Workbook Sheet", "Category"
        ];

        for (int c = 1; c <= headers.Length; c++)
        {
            var cell = ws.Cell(3, c);
            cell.Value = headers[c - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = CareHeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        ws.SheetView.FreezeRows(3);
        ws.RangeUsed()?.SetAutoFilter();

        // ── Data rows ─────────────────────────────────────────────────────────
        int row = 4;
        foreach (var c in candidates)
        {
            ws.Cell(row, 1).Value = c.Domain;
            ws.Cell(row, 2).Value = c.FindingCode;
            ws.Cell(row, 3).Value = c.Severity;
            ws.Cell(row, 4).Value = c.SupportReadiness;
            ws.Cell(row, 5).Value = c.SupportReadinessScore;
            ws.Cell(row, 6).Value = c.Confidence;
            ws.Cell(row, 7).Value = c.BlastRadius;
            ws.Cell(row, 8).Value = c.SuspectedOwner;
            ws.Cell(row, 9).Value = c.ProbableCauseCategory;

            // Severity cell colour
            ws.Cell(row, 3).Style.Fill.BackgroundColor = c.Severity switch
            {
                "Critical" => SeverityCritical,
                "High" => SeverityWarning,
                _ => XLColor.NoColor
            };

            ws.Cell(row, 4).Style.Fill.BackgroundColor = c.SupportReadiness switch
            {
                "Ready" => SeverityCritical,
                "NeedsReview" => SeverityWarning,
                _ => SeverityInfo
            };

            ws.Cell(row, 10).Value = c.AffectedObjectName ?? c.AffectedObjectId;
            ws.Cell(row, 11).Value = c.AffectedObjectId;
            ws.Cell(row, 12).Value = c.RelatedObjectNames.Count > 0
                ? string.Join(", ", c.RelatedObjectNames)
                : string.Join(", ", c.RelatedObjectIds);
            ws.Cell(row, 13).Value = string.Join("; ", c.ApiSurfaces);
            ws.Cell(row, 14).Value = c.RecentChangeContext;
            ws.Cell(row, 15).Value = string.Join(" | ", c.QualificationNotes);
            ws.Cell(row, 16).Value = c.EvidenceSummary;
            ws.Cell(row, 17).Value = c.SuggestedCaseText;
            ws.Cell(row, 18).Value = c.RecommendedAction;
            ws.Cell(row, 19).Value = c.WorkbookSheet;
            ws.Cell(row, 20).Value = c.Category;

            // Alternate row background using Care palette
            if (row % 2 == 0)
            {
                ws.Range(row, 1, row, 20)
                  .Cells()
                  .Where(cell => cell.Style.Fill.BackgroundColor == XLColor.NoColor)
                  .ToList()
                  .ForEach(cell => cell.Style.Fill.BackgroundColor = CareAltRowBg);
            }

            row++;
        }

        // ── Column widths ─────────────────────────────────────────────────────
        ws.Column(1).Width = 24;
        ws.Column(2).Width = 24;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 18;
        ws.Column(5).Width = 14;
        ws.Column(6).Width = 12;
        ws.Column(7).Width = 34;
        ws.Column(8).Width = 24;
        ws.Column(9).Width = 28;
        ws.Column(10).Width = 28;
        ws.Column(11).Width = 38;
        ws.Column(12).Width = 30;
        ws.Column(13).Width = 54;
        ws.Column(14).Width = 48;
        ws.Column(15).Width = 56;
        ws.Column(16).Width = 60;
        ws.Column(17).Width = 78;
        ws.Column(18).Width = 60;
        ws.Column(19).Width = 24;
        ws.Column(20).Width = 24;

        // Wrap long text columns
        foreach (int col in new[] { 7, 9, 13, 14, 15, 16, 17, 18 })
            ws.Column(col).Style.Alignment.WrapText = true;

        ws.Row(3).Style.Alignment.WrapText = false;
    }

    // ─── IVR Flow Bindings (Phase 1.4) ──────────────────────────────────────

    private static void WriteIvrFlowBindingsSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("IVR_Flow_Bindings");
        var findings = report.IvrFlowBindingFindings;

        string[] headers =
        [
            "Finding Code", "IVR Name", "IVR ID", "Binding Slot", "DNIS Count", "DNIS Numbers",
            "Bound Flow Name", "Bound Flow ID", "Days Since Published",
            "Severity", "Category", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "IVR Flow Dependency — Entry Point Binding Integrity", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FindingCode;
            ws.Cell(row, 2).Value = f.IvrName;
            ws.Cell(row, 3).Value = f.IvrId;
            ws.Cell(row, 4).Value = f.BindingSlot;
            ws.Cell(row, 5).Value = f.Dnis.Count;
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Value = f.Dnis.Count > 0 ? string.Join(", ", f.Dnis) : "";
            ws.Cell(row, 7).Value = f.BoundFlowName;
            ws.Cell(row, 8).Value = f.BoundFlowId;
            if (f.FlowDaysSincePublished.HasValue)
                ws.Cell(row, 9).Value = f.FlowDaysSincePublished.Value;
            else
                ws.Cell(row, 9).Value = f.BoundFlowId is null ? "N/A — no binding" : "N/A — never published";
            ws.Cell(row, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 10).Value = f.Severity.ToString();
            ws.Cell(row, 11).Value = f.Category.ToString();
            ws.Cell(row, 12).Value = f.Issue;
            ws.Cell(row, 13).Value = f.RecommendedAction;

            var severityCell = ws.Cell(row, 10);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;

            ApplyAltRow(ws, row, 13);
            row++;
        }

        AdjustColumns(ws, 13);
    }

    // ─── User Telephony Integrity (Phase 1.2) ───────────────────────────────

    private static void WriteUserTelephonyIntegritySheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("User_Telephony_Integrity");
        var findings = report.UserTelephonyIntegrityFindings;

        string[] headers =
        [
            "Finding Code", "User Name", "User ID", "Email", "State",
            "Profile Extension", "Station ID", "Station Name",
            "Related DID", "Severity", "Category", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "User Telephony Integrity — Cross-Endpoint Contradictions", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FindingCode;
            ws.Cell(row, 2).Value = f.UserName;
            ws.Cell(row, 3).Value = f.UserId;
            ws.Cell(row, 4).Value = f.Email;
            ws.Cell(row, 5).Value = f.UserState;
            ws.Cell(row, 6).Value = f.ProfileExtensionRaw;
            ws.Cell(row, 7).Value = f.StationId;
            ws.Cell(row, 8).Value = f.StationName;
            ws.Cell(row, 9).Value = f.RelatedDidNumber;
            ws.Cell(row, 10).Value = f.Severity.ToString();
            ws.Cell(row, 11).Value = f.Category.ToString();
            ws.Cell(row, 12).Value = f.Issue;
            ws.Cell(row, 13).Value = f.RecommendedAction;

            var severityCell = ws.Cell(row, 10);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;

            ApplyAltRow(ws, row, 13);
            row++;
        }

        AdjustColumns(ws, 13);
    }

    // ─── Queue Serviceability (Phase 1.3) ───────────────────────────────────

    private static void WriteQueueServiceabilitySheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Queue_Serviceability");
        var findings = report.QueueServiceabilityFindings;

        string[] headers =
        [
            "Finding Code", "Queue Name", "Queue ID", "Members (Record)", "Members Checked",
            "Active", "Inactive", "Unresolvable", "Severity", "Category", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "Queue Serviceability — Non-Serviceable Queue Membership", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FindingCode;
            ws.Cell(row, 2).Value = f.QueueName;
            ws.Cell(row, 3).Value = f.QueueId;
            ws.Cell(row, 4).Value = f.TotalMembersOnRecord;
            ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 5).Value = f.MembersChecked;
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Value = f.ActiveMemberCount;
            ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 7).Value = f.InactiveMemberCount;
            ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 8).Value = f.UnresolvableMemberCount;
            ws.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 9).Value = f.Severity.ToString();
            ws.Cell(row, 10).Value = f.Category.ToString();
            ws.Cell(row, 11).Value = f.Issue;
            ws.Cell(row, 12).Value = f.RecommendedAction;

            var severityCell = ws.Cell(row, 9);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;

            if (f.ActiveMemberCount == 0 && f.MembersChecked > 0)
                ws.Cell(row, 6).Style.Fill.BackgroundColor = SeverityCritical;

            ApplyAltRow(ws, row, 12);
            row++;
        }

        AdjustColumns(ws, 12);
    }

    // ─── Stale Licenses (Phase 1 Audit 1) ───────────────────────────────────

    private static void WriteStaleLicensesSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Stale_Licenses");
        var findings = report.StaleLicenseFindings;

        string[] headers =
        [
            "User Name", "User ID", "Email", "State",
            "Assigned Licenses", "Token Last Issued (UTC)", "Days Since Login", "Issue"
        ];
        WriteSheetHeader(
            ws,
            $"Stale License Usage — Licensed Users with No Login in >{report.Options.StaleLicenseThresholdDays} Days",
            report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.UserName;
            ws.Cell(row, 2).Value = f.UserId;
            ws.Cell(row, 3).Value = f.Email;
            ws.Cell(row, 4).Value = f.State;
            ws.Cell(row, 5).Value = f.AssignedLicenses.Count > 0 ? string.Join(", ", f.AssignedLicenses) : "";
            ws.Cell(row, 6).Value = f.TokenLastIssuedDate?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
            if (f.DaysSinceLogin.HasValue)
                ws.Cell(row, 7).Value = f.DaysSinceLogin.Value;
            else
                ws.Cell(row, 7).Value = "N/A";
            ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 8).Value = f.Issue;
            ApplyAltRow(ws, row, 8);
            ws.Cell(row, 7).Style.Fill.BackgroundColor = SeverityWarning;
            row++;
        }

        AdjustColumns(ws, 8);
    }

    // ─── License Over-Provisioning (Phase 1 Audit 2) ────────────────────────

    private static void WriteLicenseOverProvisioningSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("License_Over_Provisioning");
        var findings = report.LicenseOverProvisioningFindings;

        string[] headers =
        [
            "User Name", "User ID", "Email", "State",
            "All Licenses", "Premium Licenses (Over-Provisioned)",
            "Token Last Issued (UTC)", "Days Since Login", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "License Over-Provisioning — CX3/WEM/Outbound Licenses with No Recent Usage",
            report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.UserName;
            ws.Cell(row, 2).Value = f.UserId;
            ws.Cell(row, 3).Value = f.Email;
            ws.Cell(row, 4).Value = f.State;
            ws.Cell(row, 5).Value = f.AllAssignedLicenses.Count > 0 ? string.Join(", ", f.AllAssignedLicenses) : "";
            ws.Cell(row, 6).Value = f.OverProvisionedLicenses.Count > 0 ? string.Join(", ", f.OverProvisionedLicenses) : "";
            ws.Cell(row, 7).Value = f.TokenLastIssuedDate?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
            if (f.DaysSinceLogin.HasValue)
                ws.Cell(row, 8).Value = f.DaysSinceLogin.Value;
            else
                ws.Cell(row, 8).Value = "N/A";
            ws.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 9).Value = f.Issue;
            ws.Cell(row, 10).Value = f.RecommendedAction;
            ApplyAltRow(ws, row, 10);
            ws.Cell(row, 6).Style.Fill.BackgroundColor = SeverityWarning;
            row++;
        }

        AdjustColumns(ws, 10);
    }

    // ─── Role & Group Overlap (Phase 1 Audit 3) ─────────────────────────────

    private static void WriteRoleGroupOverlapSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Role_Group_Overlap");
        var findings = report.RoleGroupOverlapFindings;

        string[] headers =
        [
            "User Name", "User ID", "Email", "State",
            "Role Name", "Role ID", "Division Name", "Division ID",
            "Covering Group Name", "Covering Group ID",
            "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "Role & Group Overlap — Redundant Direct Role Assignments",
            report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.UserName;
            ws.Cell(row, 2).Value = f.UserId;
            ws.Cell(row, 3).Value = f.Email;
            ws.Cell(row, 4).Value = f.UserState;
            ws.Cell(row, 5).Value = f.RoleName;
            ws.Cell(row, 6).Value = f.RoleId;
            ws.Cell(row, 7).Value = f.DivisionName;
            ws.Cell(row, 8).Value = f.DivisionId;
            ws.Cell(row, 9).Value = f.GroupName;
            ws.Cell(row, 10).Value = f.GroupId;
            ws.Cell(row, 11).Value = f.Issue;
            ws.Cell(row, 12).Value = f.RecommendedAction;
            ApplyAltRow(ws, row, 12);
            ws.Cell(row, 5).Style.Fill.BackgroundColor = SeverityWarning;
            row++;
        }

        AdjustColumns(ws, 12);
    }

    // ─── Architect Prompt Hygiene (Phase 2) ─────────────────────────────────

    private static void WritePromptHygieneSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Prompt_Hygiene");
        var findings = report.PromptHygieneFindings;

        string[] headers =
        [
            "Finding Code", "Prompt Name", "Prompt ID", "Description",
            "System Prompt", "Resource Count", "Affected Languages",
            "Severity", "Category", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "Architect Prompt Hygiene — Prompts with No Usable Audio", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FindingCode;
            ws.Cell(row, 2).Value = f.PromptName;
            ws.Cell(row, 3).Value = f.PromptId;
            ws.Cell(row, 4).Value = f.Description;
            ws.Cell(row, 5).Value = f.IsSystemPrompt ? "Yes" : "No";
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Value = f.ResourceCount;
            ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 7).Value = f.AffectedLanguages;
            ws.Cell(row, 8).Value = f.Severity.ToString();
            ws.Cell(row, 9).Value = f.Category.ToString();
            ws.Cell(row, 10).Value = f.Issue;
            ws.Cell(row, 11).Value = f.RecommendedAction;

            var severityCell = ws.Cell(row, 8);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;

            ApplyAltRow(ws, row, 11);
            row++;
        }

        AdjustColumns(ws, 11);
    }

    // ─── Site–Edge–Trunk Topology (Phase 1.5) ───────────────────────────────

    private static void WriteSiteTopologySheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Site_Topology");
        var findings = report.SiteTopologyFindings;

        string[] headers =
        [
            "Finding Code", "Object Type", "Object Name", "Object ID",
            "Site Name", "Site ID", "Edge Name", "Edge ID",
            "Trunk State", "Severity", "Category", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "Site–Edge–Trunk Topology — Infrastructure Integrity", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FindingCode;
            ws.Cell(row, 2).Value = f.ObjectType;
            ws.Cell(row, 3).Value = f.ObjectName;
            ws.Cell(row, 4).Value = f.ObjectId;
            ws.Cell(row, 5).Value = f.SiteName;
            ws.Cell(row, 6).Value = f.SiteId;
            ws.Cell(row, 7).Value = f.EdgeName;
            ws.Cell(row, 8).Value = f.EdgeId;
            ws.Cell(row, 9).Value = f.TrunkState;
            ws.Cell(row, 10).Value = f.Severity.ToString();
            ws.Cell(row, 11).Value = f.Category.ToString();
            ws.Cell(row, 12).Value = f.Issue;
            ws.Cell(row, 13).Value = f.RecommendedAction;

            var severityCell = ws.Cell(row, 10);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;

            ApplyAltRow(ws, row, 13);
            row++;
        }

        AdjustColumns(ws, 13);
    }

    // ─── Change Adjacency (Phase 2.1) ───────────────────────────────────────

    private static void WriteChangeAdjacencySheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Change_Adjacency");
        var findings = report.ChangeAdjacencyFindings;

        string[] headers =
        [
            "Finding Code", "Object Type", "Object Name", "Object ID",
            "Change Timestamp (UTC)", "Change Action", "Changed By", "Change Count",
            "Related Finding Type", "Severity", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "Change Adjacency — Config Changes Correlated with Active Findings", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FindingCode;
            ws.Cell(row, 2).Value = f.AffectedObjectType;
            ws.Cell(row, 3).Value = f.AffectedObjectName;
            ws.Cell(row, 4).Value = f.AffectedObjectId;
            ws.Cell(row, 5).Value = f.ChangeTimestamp?.UtcDateTime.ToString("u");
            ws.Cell(row, 6).Value = f.ChangeAction;
            ws.Cell(row, 7).Value = f.ChangedBy;
            ws.Cell(row, 8).Value = f.ChangeCount;
            ws.Cell(row, 9).Value = f.RelatedFindingType;
            ws.Cell(row, 10).Value = f.Severity.ToString();
            ws.Cell(row, 11).Value = f.Issue;
            ws.Cell(row, 12).Value = f.RecommendedAction;

            var severityCell = ws.Cell(row, 10);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;
            else
                severityCell.Style.Fill.BackgroundColor = SeverityInfo;

            ApplyAltRow(ws, row, 12);
            row++;
        }

        AdjustColumns(ws, 12);
    }

    // ─── Shared helpers ──────────────────────────────────────────────────────

    private static void WriteFlappingDetectionSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Flapping_Detection");
        var findings = report.FlappingDetectionFindings;

        string[] headers =
        [
            "Finding Code", "Object Type", "Object Name", "Object ID",
            "First Change (UTC)", "Last Change (UTC)", "Change Count", "Distinct Action Count",
            "Observed Actions", "Severity", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "Flapping & Instability — Repeated State Changes Detected", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.FindingCode;
            ws.Cell(row, 2).Value = f.AffectedObjectType;
            ws.Cell(row, 3).Value = f.AffectedObjectName;
            ws.Cell(row, 4).Value = f.AffectedObjectId;
            ws.Cell(row, 5).Value = f.FirstChangeUtc?.UtcDateTime.ToString("u");
            ws.Cell(row, 6).Value = f.LastChangeUtc?.UtcDateTime.ToString("u");
            ws.Cell(row, 7).Value = f.ChangeCount;
            ws.Cell(row, 8).Value = f.DistinctActionCount;
            ws.Cell(row, 9).Value = string.Join(", ", f.ObservedActions);
            ws.Cell(row, 10).Value = f.Severity.ToString();
            ws.Cell(row, 11).Value = f.Issue;
            ws.Cell(row, 12).Value = f.RecommendedAction;

            var severityCell = ws.Cell(row, 10);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;
            else
                severityCell.Style.Fill.BackgroundColor = SeverityInfo;

            ApplyAltRow(ws, row, 12);
            row++;
        }

        AdjustColumns(ws, 12);
    }

    private static void WriteHotSpotSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Hot_Spots");
        var findings = report.HotSpotFindings;

        string[] headers =
        [
            "Rank", "Object Type", "Object Name", "Object ID",
            "Total Finding Count", "Distinct Domain Count", "Affected Domains",
            "Severity", "Issue", "Recommended Action"
        ];
        WriteSheetHeader(ws, "Hot Spot Ranking — Objects Appearing Across Multiple Audit Domains", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.Rank;
            ws.Cell(row, 2).Value = f.ObjectType;
            ws.Cell(row, 3).Value = f.ObjectName;
            ws.Cell(row, 4).Value = f.ObjectId;
            ws.Cell(row, 5).Value = f.TotalFindingCount;
            ws.Cell(row, 6).Value = f.DistinctDomainCount;
            ws.Cell(row, 7).Value = string.Join(", ", f.AffectedDomains);
            ws.Cell(row, 8).Value = f.Severity.ToString();
            ws.Cell(row, 9).Value = f.Issue;
            ws.Cell(row, 10).Value = f.RecommendedAction;

            var severityCell = ws.Cell(row, 8);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;
            else
                severityCell.Style.Fill.BackgroundColor = SeverityInfo;

            ApplyAltRow(ws, row, 10);
            row++;
        }

        AdjustColumns(ws, 10);
    }

    // ─── Finding Lifecycle (Phase 4.1) ──────────────────────────────────────

    private static void WriteFindingLifecycleSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Finding_Lifecycle");
        var findings = report.FindingLifecycleFindings;

        string[] headers =
        [
            "Lifecycle Status", "Domain", "Finding Type", "Object Name", "Object ID",
            "First Seen (UTC)", "Last Seen (UTC)", "Observation Count", "Severity", "Issue", "Finding Key"
        ];
        WriteSheetHeader(ws, "Finding Lifecycle — Comparison Against Previous Snapshot", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.LifecycleStatus;
            ws.Cell(row, 2).Value = f.Domain;
            ws.Cell(row, 3).Value = f.FindingType;
            ws.Cell(row, 4).Value = f.ObjectName;
            ws.Cell(row, 5).Value = f.ObjectId;
            ws.Cell(row, 6).Value = f.FirstSeenUtc.UtcDateTime.ToString("u");
            ws.Cell(row, 7).Value = f.LastSeenUtc.UtcDateTime.ToString("u");
            ws.Cell(row, 8).Value = f.ObservationCount;
            ws.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 9).Value = f.Severity.ToString();
            ws.Cell(row, 10).Value = f.Issue;
            ws.Cell(row, 11).Value = f.FindingKey;

            ws.Cell(row, 1).Style.Fill.BackgroundColor = f.LifecycleStatus switch
            {
                FindingLifecycleStatus.New => SeverityWarning,
                FindingLifecycleStatus.Recurrent => SeverityCritical,
                FindingLifecycleStatus.Resolved => SeverityInfo,
                _ => XLColor.NoColor
            };

            var severityCell = ws.Cell(row, 9);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;
            else
                severityCell.Style.Fill.BackgroundColor = SeverityInfo;

            ApplyAltRow(ws, row, 11);
            row++;
        }

        AdjustColumns(ws, 11);
    }

    private static void WriteHistoricalDriftSheet(IXLWorkbook wb, AuditReportData report)
    {
        var ws = wb.Worksheets.Add("Historical_Drift");
        var findings = report.HistoricalDriftFindings;

        string[] headers =
        [
            "Change Type", "Domain", "Relationship Type", "Object Type", "Object Name", "Object ID",
            "Previous Value", "Current Value", "Severity", "Issue", "Recommended Action", "Relationship Key"
        ];
        WriteSheetHeader(ws, "Historical Drift — Relationship Changes Since Previous Snapshot", report, findings.Count, headers);

        int row = 4;
        foreach (var f in findings)
        {
            ws.Cell(row, 1).Value = f.ChangeType;
            ws.Cell(row, 2).Value = f.Domain;
            ws.Cell(row, 3).Value = f.RelationshipType;
            ws.Cell(row, 4).Value = f.ObjectType;
            ws.Cell(row, 5).Value = f.ObjectName;
            ws.Cell(row, 6).Value = f.ObjectId;
            ws.Cell(row, 7).Value = f.PreviousValue;
            ws.Cell(row, 8).Value = f.CurrentValue;
            ws.Cell(row, 9).Value = f.Severity.ToString();
            ws.Cell(row, 10).Value = f.Issue;
            ws.Cell(row, 11).Value = f.RecommendedAction;
            ws.Cell(row, 12).Value = f.RelationshipKey;

            ws.Cell(row, 1).Style.Fill.BackgroundColor = f.ChangeType switch
            {
                HistoricalDriftChangeType.Changed => SeverityCritical,
                HistoricalDriftChangeType.Added => SeverityWarning,
                HistoricalDriftChangeType.Removed => SeverityInfo,
                _ => XLColor.NoColor
            };

            var severityCell = ws.Cell(row, 9);
            if (f.Severity is FindingSeverity.Critical or FindingSeverity.High)
                severityCell.Style.Fill.BackgroundColor = SeverityCritical;
            else if (f.Severity == FindingSeverity.Medium)
                severityCell.Style.Fill.BackgroundColor = SeverityWarning;
            else
                severityCell.Style.Fill.BackgroundColor = SeverityInfo;

            ApplyAltRow(ws, row, 12);
            row++;
        }

        AdjustColumns(ws, 12);
    }

    private static IXLWorksheet WriteSheetHeader(
        IXLWorksheet ws,
        string title,
        AuditReportData report,
        int findingCount,
        string[] headers)
    {
        int colCount = headers.Length;

        // Row 1: title band
        var titleRange = ws.Range(1, 1, 1, colCount);
        titleRange.Merge();
        titleRange.Style.Fill.BackgroundColor = TitleBg;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontColor = HeaderFg;
        titleRange.Style.Font.FontSize = 14;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 24;
        ws.Cell(1, 1).Value = $"  {title}";

        // Row 2: metadata
        var metaRange = ws.Range(2, 1, 2, colCount);
        metaRange.Merge();
        metaRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D6E4F0");
        metaRange.Style.Font.FontSize = 10;
        metaRange.Style.Font.Italic = true;
        ws.Cell(2, 1).Value =
            $"  Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}   |   Org: {report.OrgRegion}   |   Findings: {findingCount}";

        // Row 3: column headers
        for (int c = 1; c <= colCount; c++)
        {
            var cell = ws.Cell(3, c);
            cell.Value = headers[c - 1];
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = HeaderFg;
            cell.Style.Font.FontSize = 10;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        ws.Row(3).Height = 18;
        ws.SheetView.FreezeRows(3);
        ws.RangeUsed()?.SetAutoFilter();

        return ws;
    }

    private static void WriteRow(IXLWorksheet ws, int row, params object?[] values)
    {
        for (int c = 0; c < values.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = values[c] switch
            {
                null => "",
                string s => s,
                int i => (XLCellValue)i,
                _ => values[c]?.ToString() ?? ""
            };
        }
    }

    private static void ApplyAltRow(IXLWorksheet ws, int row, int colCount)
    {
        if (row % 2 == 0)
        {
            for (int c = 1; c <= colCount; c++)
            {
                var cell = ws.Cell(row, c);
                // Only apply if not already colored
                if (cell.Style.Fill.BackgroundColor == XLColor.NoColor
                    || cell.Style.Fill.BackgroundColor == XLColor.Transparent)
                {
                    cell.Style.Fill.BackgroundColor = AltRowBg;
                }
            }
        }
    }

    private static void AdjustColumns(IXLWorksheet ws, int colCount, int minWidth = 10, int maxWidth = 60)
    {
        for (int c = 1; c <= colCount; c++)
        {
            ws.Column(c).AdjustToContents();
            if (ws.Column(c).Width < minWidth) ws.Column(c).Width = minWidth;
            if (ws.Column(c).Width > maxWidth) ws.Column(c).Width = maxWidth;
        }
    }
}
