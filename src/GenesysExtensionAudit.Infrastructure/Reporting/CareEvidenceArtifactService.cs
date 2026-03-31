using System.Net;
using System.Text;
using System.Text.Json;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

public interface ICareEvidenceArtifactService
{
    byte[] BuildJson(CareEvidencePacket packet);
    byte[] BuildHtml(AuditReportData report, CareEvidencePacket packet);
}

public sealed class CareEvidenceArtifactService : ICareEvidenceArtifactService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public byte[] BuildJson(CareEvidencePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet, JsonOptions));
    }

    public byte[] BuildHtml(AuditReportData report, CareEvidencePacket packet)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(packet);

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\">");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("  <title>Genesys Cloud Audit Summary</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    :root { color-scheme: light; --ink:#16324f; --muted:#5f6b7a; --line:#d5dde6; --bg:#f6f8fb; --panel:#ffffff; --critical:#8c1d18; --critical-bg:#fde8e7; --warn:#8a5a00; --warn-bg:#fff5db; --good:#1d5f2f; --good-bg:#e6f4ea; }");
        html.AppendLine("    body { margin:0; font-family:'Segoe UI', Tahoma, sans-serif; background:var(--bg); color:#1f2933; }");
        html.AppendLine("    main { max-width:1200px; margin:0 auto; padding:32px 24px 48px; }");
        html.AppendLine("    h1, h2, h3 { color:var(--ink); margin:0; }");
        html.AppendLine("    h1 { font-size:32px; }");
        html.AppendLine("    h2 { font-size:22px; margin-top:28px; }");
        html.AppendLine("    p.meta { color:var(--muted); margin:12px 0 0; }");
        html.AppendLine("    .grid { display:grid; grid-template-columns:repeat(auto-fit, minmax(180px, 1fr)); gap:16px; margin-top:20px; }");
        html.AppendLine("    .card, .panel { background:var(--panel); border:1px solid var(--line); border-radius:14px; box-shadow:0 8px 24px rgba(22,50,79,0.06); }");
        html.AppendLine("    .card { padding:18px; }");
        html.AppendLine("    .label { display:block; font-size:12px; font-weight:700; text-transform:uppercase; letter-spacing:0.06em; color:var(--muted); }");
        html.AppendLine("    .value { display:block; margin-top:10px; font-size:28px; font-weight:700; color:var(--ink); }");
        html.AppendLine("    .panel { margin-top:18px; overflow:hidden; }");
        html.AppendLine("    table { width:100%; border-collapse:collapse; }");
        html.AppendLine("    th, td { border-bottom:1px solid var(--line); padding:12px 14px; text-align:left; vertical-align:top; }");
        html.AppendLine("    th { background:#eaf1f8; color:var(--ink); font-size:12px; text-transform:uppercase; letter-spacing:0.05em; }");
        html.AppendLine("    tr:last-child td { border-bottom:none; }");
        html.AppendLine("    .pill { display:inline-block; padding:4px 10px; border-radius:999px; font-size:12px; font-weight:700; }");
        html.AppendLine("    .ready { color:var(--critical); background:var(--critical-bg); }");
        html.AppendLine("    .review { color:var(--warn); background:var(--warn-bg); }");
        html.AppendLine("    .monitor { color:var(--good); background:var(--good-bg); }");
        html.AppendLine("    ul.chain { margin:0; padding-left:18px; }");
        html.AppendLine("    li { margin:0 0 6px; }");
        html.AppendLine("    .muted { color:var(--muted); }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("<main>");
        html.AppendLine($"  <h1>{Html("Genesys Cloud Audit Summary")}</h1>");
        html.AppendLine($"  <p class=\"meta\">Generated {Html(report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"))} | Region {Html(report.OrgRegion)} | Escalation candidates {packet.Summary.EscalationCandidateCount}</p>");
        html.AppendLine("  <section class=\"grid\">");
        AppendMetricCard(html, "Total Findings", packet.Summary.TotalFindingsInRun.ToString());
        AppendMetricCard(html, "Open Case Recommended", packet.Summary.ReadyForCareCount.ToString());
        AppendMetricCard(html, "Needs Review", packet.Summary.NeedsReviewCount.ToString());
        AppendMetricCard(html, "Monitor", packet.Summary.MonitorCount.ToString());
        AppendMetricCard(html, "Critical", packet.Summary.CriticalCount.ToString());
        AppendMetricCard(html, "High", packet.Summary.HighCount.ToString());
        html.AppendLine("  </section>");

        html.AppendLine("  <h2>Escalation Overview</h2>");
        html.AppendLine("  <div class=\"panel\">");
        html.AppendLine("    <table>");
        html.AppendLine("      <thead><tr><th>Readiness</th><th>Domain</th><th>Object</th><th>Probable Cause</th><th>Blast Radius</th><th>Why This Matters</th></tr></thead>");
        html.AppendLine("      <tbody>");
        foreach (var candidate in packet.EscalationCandidates.Take(10))
        {
            html.AppendLine("        <tr>");
            html.AppendLine($"          <td>{ReadinessPill(candidate.SupportReadiness)}</td>");
            html.AppendLine($"          <td>{Html(candidate.Domain)}</td>");
            html.AppendLine($"          <td>{Html(candidate.AffectedObjectName ?? candidate.AffectedObjectId ?? "(unknown)")}</td>");
            html.AppendLine($"          <td>{Html(candidate.ProbableCauseCategory)}</td>");
            html.AppendLine($"          <td>{Html(candidate.BlastRadius)}</td>");
            html.AppendLine($"          <td>{Html(candidate.WhyThisMatters)}</td>");
            html.AppendLine("        </tr>");
        }

        if (packet.EscalationCandidates.Count == 0)
            html.AppendLine("        <tr><td colspan=\"6\" class=\"muted\">No escalation candidates were produced for this run.</td></tr>");

        html.AppendLine("      </tbody>");
        html.AppendLine("    </table>");
        html.AppendLine("  </div>");

        html.AppendLine("  <h2>Evidence Chains</h2>");
        foreach (var candidate in packet.EscalationCandidates.Take(5))
        {
            html.AppendLine("  <div class=\"panel\">");
            html.AppendLine("    <div style=\"padding:18px 18px 6px;\">");
            html.AppendLine($"      <h3>{Html(candidate.Domain)}: {Html(candidate.AffectedObjectName ?? candidate.AffectedObjectId ?? "(unknown)")}</h3>");
            html.AppendLine($"      <p class=\"meta\">Dependency chain: {Html(candidate.DependencyChain)}</p>");
            html.AppendLine("    </div>");
            html.AppendLine("    <table>");
            html.AppendLine("      <tbody>");
            html.AppendLine($"        <tr><th style=\"width:180px;\">Evidence Chain</th><td><ul class=\"chain\">{string.Join(string.Empty, candidate.EvidenceChain.Select(step => $"<li>{Html(step)}</li>"))}</ul></td></tr>");
            html.AppendLine($"        <tr><th>Recent Change Context</th><td>{Html(candidate.RecentChangeContext ?? "No recent correlated admin change was found in the active audit-log window.")}</td></tr>");
            html.AppendLine($"        <tr><th>Qualification Notes</th><td><ul class=\"chain\">{string.Join(string.Empty, candidate.QualificationNotes.Select(step => $"<li>{Html(step)}</li>"))}</ul></td></tr>");
            html.AppendLine($"        <tr><th>Workbook Sheet</th><td>{Html(candidate.WorkbookSheet)}</td></tr>");
            html.AppendLine("      </tbody>");
            html.AppendLine("    </table>");
            html.AppendLine("  </div>");
        }

        html.AppendLine("</main>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return Encoding.UTF8.GetBytes(html.ToString());
    }

    private static void AppendMetricCard(StringBuilder html, string label, string value)
    {
        html.AppendLine("    <div class=\"card\">");
        html.AppendLine($"      <span class=\"label\">{Html(label)}</span>");
        html.AppendLine($"      <span class=\"value\">{Html(value)}</span>");
        html.AppendLine("    </div>");
    }

    private static string ReadinessPill(string readiness)
    {
        var css = readiness switch
        {
            "Ready" => "pill ready",
            "NeedsReview" => "pill review",
            _ => "pill monitor"
        };

        return $"<span class=\"{css}\">{Html(readiness)}</span>";
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
