using GenesysExtensionAudit.Domain.Paging;
using Microsoft.Extensions.Logging;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Pagination;

/// <summary>
/// Sequential paginator: fetches pages 1, 2, 3… until the endpoint
/// signals completion via pageCount or an empty items list.
/// </summary>
public sealed class Paginator : IPaginator
{
    private readonly ILogger<Paginator> _logger;

    public Paginator(ILogger<Paginator> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<T>> FetchAllAsync<T>(
        Func<int, Task<PagedResult<T>>> getPage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(getPage);

        var all = new List<T>();
        var pageNumber = 1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var page = await getPage(pageNumber).ConfigureAwait(false);

            if (page.Items.Count > 0)
                all.AddRange(page.Items);

            _logger.LogDebug(
                "Fetched page {Page}/{PageCount}, items={ItemCount}, total so far={Total}",
                pageNumber, page.PageCount?.ToString() ?? "?", page.Items.Count, all.Count);

            // Stop when: we've reached the last page (via PageCount)…
            if (page.PageCount.HasValue && pageNumber >= page.PageCount.Value)
                break;

            // …or the API returned fewer items than requested (fallback for endpoints without PageCount)
            if (!page.PageCount.HasValue && page.Items.Count == 0)
                break;

            pageNumber++;
        }

        return all;
    }
}
