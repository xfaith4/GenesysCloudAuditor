namespace GenesysExtensionAudit.Domain.Paging;

/// <summary>
/// Abstracts paginated HTTP fetching so the application layer
/// does not depend directly on Infrastructure paging internals.
/// </summary>
public interface IPaginator
{
    /// <summary>
    /// Fetches all items from a paginated endpoint by calling <paramref name="getPage"/>
    /// with sequential page numbers until the endpoint signals completion.
    /// </summary>
    Task<IReadOnlyList<T>> FetchAllAsync<T>(
        Func<int, Task<PagedResult<T>>> getPage,
        CancellationToken ct);
}
