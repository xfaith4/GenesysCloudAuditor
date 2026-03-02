// File: src/GenesysExtensionAudit.Infrastructure/Genesys/Paging/PagingOrchestrator.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Paging;

/// <summary>
/// Orchestrates paginated API fetching with:
/// - bounded concurrency (fetch multiple pages in parallel, but capped)
/// - rate-limit awareness (429 + Retry-After)
/// - transient retries with exponential backoff + jitter
/// - in-memory TTL caching (per "cache key" + page number)
/// - single-flight dedupe so concurrent requests for same page share a single task
/// </summary>
public sealed class PagingOrchestrator
{
    private readonly PagingOrchestratorOptions _opt;
    private readonly ILogger<PagingOrchestrator> _logger;

    // Cache: key -> cached page
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    // Single-flight: key -> in-flight task
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _inflight = new();

    // Global concurrency gate across all paging work
    private readonly SemaphoreSlim _globalConcurrency;

    public PagingOrchestrator(
        IOptions<PagingOrchestratorOptions> options,
        ILogger<PagingOrchestrator> logger)
    {
        _opt = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_opt.MaxParallelRequests <= 0) _opt.MaxParallelRequests = 1;
        if (_opt.MaxRetries < 0) _opt.MaxRetries = 0;
        if (_opt.CacheTtl <= TimeSpan.Zero) _opt.CacheTtl = TimeSpan.FromMinutes(2);

        _globalConcurrency = new SemaphoreSlim(_opt.MaxParallelRequests, _opt.MaxParallelRequests);
    }

    /// <summary>
    /// Fetches all items from a paginated endpoint using bounded-parallel page fetch.
    /// Requires the caller to provide:
    /// - fetchPageAsync(pageNumber) returning a PageResult{ Items, PageNumber, PageCount }.
    ///
    /// Concurrency plan:
    /// - fetch page 1 first to discover pageCount
    /// - then schedule pages 2..pageCount with bounded concurrency
    /// - aggregate results in page order
    /// </summary>
    public async Task<IReadOnlyList<TItem>> FetchAllAsync<TItem>(
        string cacheKeyPrefix,
        Func<int, CancellationToken, Task<PageResult<TItem>>> fetchPageAsync,
        int? knownPageCount,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cacheKeyPrefix))
            throw new ArgumentException("Cache key prefix is required.", nameof(cacheKeyPrefix));

        if (fetchPageAsync is null) throw new ArgumentNullException(nameof(fetchPageAsync));

        ct.ThrowIfCancellationRequested();

        // Always fetch page 1 first (it also warms cache and discovers pageCount)
        var first = await FetchPageCachedSingleFlightAsync(
            cacheKeyPrefix,
            pageNumber: 1,
            fetchPageAsync,
            ct).ConfigureAwait(false);

        var pageCount = knownPageCount ?? first.PageCount ?? 1;
        if (pageCount < 1) pageCount = 1;

        // Aggregate in order
        var pages = new PageResult<TItem>[pageCount];
        pages[0] = first;

        if (pageCount == 1)
            return Flatten(pages);

        // bounded parallel scheduling for pages 2..pageCount
        var tasks = new List<Task>(capacity: pageCount - 1);

        for (var page = 2; page <= pageCount; page++)
        {
            var pageNumber = page;
            tasks.Add(Task.Run(async () =>
            {
                var pr = await FetchPageCachedSingleFlightAsync(
                    cacheKeyPrefix,
                    pageNumber,
                    fetchPageAsync,
                    ct).ConfigureAwait(false);

                pages[pageNumber - 1] = pr;
            }, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return Flatten(pages);
    }

    private IReadOnlyList<TItem> Flatten<TItem>(PageResult<TItem>[] pages)
    {
        var total = 0;
        for (var i = 0; i < pages.Length; i++)
            total += pages[i].Items?.Count ?? 0;

        var all = new List<TItem>(capacity: total);

        for (var i = 0; i < pages.Length; i++)
        {
            var items = pages[i].Items;
            if (items is { Count: > 0 })
                all.AddRange(items);
        }

        return all;
    }

    private async Task<PageResult<TItem>> FetchPageCachedSingleFlightAsync<TItem>(
        string cacheKeyPrefix,
        int pageNumber,
        Func<int, CancellationToken, Task<PageResult<TItem>>> fetchPageAsync,
        CancellationToken ct)
    {
        var cacheKey = $"{cacheKeyPrefix}::page={pageNumber}";

        // 1) Cache hit?
        if (TryGetFromCache<PageResult<TItem>>(cacheKey, out var cached))
        {
            _logger.LogDebug("Paging cache hit: {CacheKey}", cacheKey);
            return cached;
        }

        // 2) Single-flight: if multiple callers request same page simultaneously, only one fetch runs.
        var lazy = _inflight.GetOrAdd(cacheKey, _ =>
            new Lazy<Task<object>>(() => FetchAndCacheAsync(cacheKey, pageNumber, fetchPageAsync, ct),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var obj = await lazy.Value.ConfigureAwait(false);
            return (PageResult<TItem>)obj;
        }
        finally
        {
            // Clean up inflight entry (if it's still the same lazy instance)
            _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<object>>>(cacheKey, lazy));
        }
    }

    private async Task<object> FetchAndCacheAsync<TItem>(
        string cacheKey,
        int pageNumber,
        Func<int, CancellationToken, Task<PageResult<TItem>>> fetchPageAsync,
        CancellationToken ct)
    {
        // Global bounded concurrency
        await _globalConcurrency.WaitAsync(ct).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();

        try
        {
            var page = await ExecuteWithRetriesAsync(
                operationName: $"Fetch page {pageNumber}",
                action: () => fetchPageAsync(pageNumber, ct),
                ct).ConfigureAwait(false);

            SetCache(cacheKey, page);

            _logger.LogDebug(
                "Fetched {CacheKey} in {ElapsedMs}ms (items={Count}, pageCount={PageCount})",
                cacheKey, sw.ElapsedMilliseconds, page.Items?.Count ?? 0, page.PageCount);

            return page!;
        }
        finally
        {
            sw.Stop();
            _globalConcurrency.Release();
        }
    }

    private bool TryGetFromCache<T>(string key, out T value)
    {
        value = default!;
        if (!_opt.EnableCache) return false;

        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                if (entry.Value is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            else
            {
                _cache.TryRemove(key, out _);
            }
        }

        return false;
    }

    private void SetCache(string key, object value)
    {
        if (!_opt.EnableCache) return;

        _cache[key] = new CacheEntry(
            Value: value,
            ExpiresAtUtc: DateTimeOffset.UtcNow.Add(_opt.CacheTtl));
    }

    private async Task<T> ExecuteWithRetriesAsync<T>(
        string operationName,
        Func<Task<T>> action,
        CancellationToken ct)
    {
        var attempt = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (GenesysRateLimitedException ex) when (attempt <= _opt.MaxRetries)
            {
                var delay = ComputeRateLimitDelay(ex, attempt);
                _logger.LogWarning(
                    "Rate limited during {Operation}. Attempt {Attempt}/{MaxAttempts}. Delay {DelayMs}ms. {Message}",
                    operationName, attempt, _opt.MaxRetries + 1, (int)delay.TotalMilliseconds, ex.Message);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (GenesysTransientException ex) when (attempt <= _opt.MaxRetries)
            {
                var delay = ComputeBackoffDelay(attempt);
                _logger.LogWarning(
                    "Transient failure during {Operation}. Attempt {Attempt}/{MaxAttempts}. Delay {DelayMs}ms. {Message}",
                    operationName, attempt, _opt.MaxRetries + 1, (int)delay.TotalMilliseconds, ex.Message);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt <= _opt.MaxRetries)
            {
                var delay = ComputeBackoffDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "HTTP request failure during {Operation}. Attempt {Attempt}/{MaxAttempts}. Delay {DelayMs}ms.",
                    operationName, attempt, _opt.MaxRetries + 1, (int)delay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan ComputeRateLimitDelay(GenesysRateLimitedException ex, int attempt)
    {
        // Prefer Retry-After if provided; add small jitter.
        var retryAfter = ex.RetryAfter ?? TimeSpan.Zero;
        var baseDelay = retryAfter > TimeSpan.Zero ? retryAfter : ComputeBackoffDelay(attempt);

        return baseDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, _opt.JitterMaxMs));
    }

    private TimeSpan ComputeBackoffDelay(int attempt)
    {
        // attempt is 1-based
        var baseMs = _opt.BaseDelay.TotalMilliseconds;
        var exp = Math.Pow(2, Math.Max(0, attempt - 1));
        var delayMs = Math.Min(_opt.MaxDelay.TotalMilliseconds, baseMs * exp);

        return TimeSpan.FromMilliseconds(delayMs + Random.Shared.Next(0, _opt.JitterMaxMs));
    }

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAtUtc);
}

/// <summary>
/// Paging result contract used by PagingOrchestrator.
/// </summary>
public sealed class PageResult<TItem>
{
    public required IReadOnlyList<TItem> Items { get; init; }
    public int PageNumber { get; init; }
    public int? PageCount { get; init; }
    public int? PageSize { get; init; }
    public int? Total { get; init; }
}

/// <summary>
/// Options controlling cache, throttling, and retries.
/// </summary>
public sealed class PagingOrchestratorOptions
{
    /// <summary>Hard cap on concurrent HTTP requests issued by the orchestrator.</summary>
    public int MaxParallelRequests { get; set; } = 3;

    /// <summary>Enable/disable in-memory TTL cache for pages.</summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>TTL for cached pages. Keep small to avoid stale audits.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum retry attempts for transient errors and 429 rate limiting (does not include initial try).</summary>
    public int MaxRetries { get; set; } = 6;

    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Max jitter added to delays, in milliseconds.</summary>
    public int JitterMaxMs { get; set; } = 250;
}

/// <summary>
/// Exception you can throw from your API client when the server returns 429.
/// Include Retry-After if available.
/// </summary>
public sealed class GenesysRateLimitedException : Exception
{
    public GenesysRateLimitedException(string message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Exception you can throw from your API client for transient HTTP status codes like 408/5xx.
/// </summary>
public sealed class GenesysTransientException : Exception
{
    public GenesysTransientException(string message, Exception? inner = null) : base(message, inner) { }
}
