namespace GenesysExtensionAudit.Application;

/// <summary>
/// A single filter clause applied server-side to the Genesys Cloud audit-log query
/// (<c>POST /api/v2/audits/query</c>).
/// </summary>
/// <param name="Property">
/// The filterable field name. Genesys-supported values:
/// <list type="bullet">
///   <item><c>action</c> — the audit action performed (e.g. CREATE, UPDATE, DELETE, EXECUTE, PUBLISH).</item>
///   <item><c>entityType</c> — the type of entity that was acted upon (e.g. Flow, Queue, User).</item>
///   <item><c>entityId</c> — the GUID of the specific entity.</item>
///   <item><c>userId</c> — the GUID of the user who performed the action.</item>
///   <item><c>clientId</c> — the OAuth client ID used to perform the action.</item>
/// </list>
/// </param>
/// <param name="Value">The value to match against <paramref name="Property"/>.</param>
public sealed record AuditLogFilter(string Property, string Value);
