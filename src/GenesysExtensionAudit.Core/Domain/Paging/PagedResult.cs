namespace GenesysExtensionAudit.Domain.Paging;

/// <summary>
/// Generic paged response contract used across all Genesys paginated endpoints.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int? PageCount,
    int? Total = null);
