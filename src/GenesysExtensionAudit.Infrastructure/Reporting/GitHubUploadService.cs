using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

/// <summary>
/// Pushes audit report files into a GitHub repository via the GitHub REST API.
/// Supports two modes, controlled by <see cref="GitHubOptions.CreateDraftPr"/>:
///   Direct commit — PUT /repos/.../contents/...  (default)
///   Draft PR      — create branch, commit file, open draft pull request
/// </summary>
public sealed class GitHubUploadService : IGitHubUploadService
{
    private const string GitHubApiBase = "https://api.github.com";
    private const string ApiVersion = "2022-11-28";
    private const string UserAgentProduct = "GenesysCloudAuditor";

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly IOptionsMonitor<GitHubOptions> _optionsMonitor;
    private readonly HttpClient _http;
    private readonly ILogger<GitHubUploadService> _logger;

    public GitHubUploadService(
        IOptionsMonitor<GitHubOptions> optionsMonitor,
        HttpClient httpClient,
        ILogger<GitHubUploadService> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Set static headers once — auth is applied per-request so token changes propagate.
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(GitHubApiBase);

        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgentProduct, "1.0"));
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }

    /// <inheritdoc/>
    public bool IsConfigured => _optionsMonitor.CurrentValue.IsConfigured;

    /// <inheritdoc/>
    public async Task<string> UploadAsync(string fileName, byte[] content, CancellationToken ct)
    {
        var opts = _optionsMonitor.CurrentValue;

        if (!opts.IsConfigured)
            throw new InvalidOperationException(
                "GitHub push is not configured. Set GitHub:Token, GitHub:Owner, and GitHub:Repository.");

        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        return opts.CreateDraftPr
            ? await UploadWithDraftPrAsync(opts, fileName, content, ct).ConfigureAwait(false)
            : await UploadDirectAsync(opts, fileName, content, ct).ConfigureAwait(false);
    }

    // ── Direct-commit flow ────────────────────────────────────────────────────

    private async Task<string> UploadDirectAsync(GitHubOptions opts, string fileName, byte[] content, CancellationToken ct)
    {
        var filePath = BuildFilePath(opts, fileName);
        var url = ContentsUrl(opts, filePath);

        _logger.LogDebug("Checking for existing file at GitHub path: {FilePath}", filePath);
        var existingSha = await GetExistingFileShaAsync(opts, url, opts.Branch, ct).ConfigureAwait(false);

        var message = opts.CommitMessage.Replace("{fileName}", fileName, StringComparison.OrdinalIgnoreCase);
        var body = new PutContentsRequest
        {
            Message = message,
            Content = Convert.ToBase64String(content),
            Branch = BranchOrDefault(opts.Branch),
            Sha = existingSha
        };

        _logger.LogInformation(
            "Pushing {Bytes:N0} bytes to GitHub: {Owner}/{Repo}/{FilePath} (branch: {Branch})",
            content.Length, opts.Owner, opts.Repository, filePath, body.Branch);

        using var request = CreateAuthorizedRequest(HttpMethod.Put, url, opts.Token);
        request.Content = JsonContent.Create(body, options: JsonOpts);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        var htmlUrl = doc.RootElement
            .TryGetProperty("content", out var contentEl) &&
            contentEl.TryGetProperty("html_url", out var htmlUrlEl)
            ? htmlUrlEl.GetString() ?? string.Empty
            : BuildFallbackFileUrl(opts, filePath, body.Branch);

        _logger.LogInformation("GitHub direct-commit complete: {Url}", htmlUrl);
        return htmlUrl;
    }

    // ── Draft-PR flow ─────────────────────────────────────────────────────────

    private async Task<string> UploadWithDraftPrAsync(GitHubOptions opts, string fileName, byte[] content, CancellationToken ct)
    {
        var baseBranch = BranchOrDefault(opts.Branch);
        var prBranch = $"{opts.PrBranchPrefix.TrimEnd('/')}/{DateTime.UtcNow:yyyyMMdd_HHmmss}";

        // 1. Get the SHA at the tip of the base branch.
        _logger.LogDebug("Resolving SHA of base branch '{Branch}'", baseBranch);
        var baseSha = await GetBranchShaAsync(opts, baseBranch, ct).ConfigureAwait(false);

        // 2. Create a new source branch from that SHA.
        _logger.LogDebug("Creating PR source branch '{PrBranch}'", prBranch);
        await CreateBranchAsync(opts, prBranch, baseSha, ct).ConfigureAwait(false);

        // 3. Commit the file to the new branch.
        var filePath = BuildFilePath(opts, fileName);
        var url = ContentsUrl(opts, filePath);
        var existingSha = await GetExistingFileShaAsync(opts, url, prBranch, ct).ConfigureAwait(false);

        var commitMessage = opts.CommitMessage.Replace("{fileName}", fileName, StringComparison.OrdinalIgnoreCase);
        var body = new PutContentsRequest
        {
            Message = commitMessage,
            Content = Convert.ToBase64String(content),
            Branch = prBranch,
            Sha = existingSha
        };

        _logger.LogInformation(
            "Pushing {Bytes:N0} bytes to GitHub PR branch: {Owner}/{Repo}/{FilePath} (branch: {Branch})",
            content.Length, opts.Owner, opts.Repository, filePath, prBranch);

        using var putReq = CreateAuthorizedRequest(HttpMethod.Put, url, opts.Token);
        putReq.Content = JsonContent.Create(body, options: JsonOpts);
        using var putResp = await _http.SendAsync(putReq, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(putResp, ct).ConfigureAwait(false);

        // 4. Open a draft pull request.
        var prTitle = $"chore: audit report {fileName}";
        var prBody = $"Automated audit report generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.\n\nFile: `{filePath}`";
        var prUrl = await CreateDraftPullRequestAsync(opts, prBranch, baseBranch, prTitle, prBody, ct).ConfigureAwait(false);

        _logger.LogInformation("GitHub draft PR created: {PrUrl}", prUrl);
        return prUrl;
    }

    // ── GitHub API helpers ────────────────────────────────────────────────────

    /// <summary>Returns the commit SHA at the tip of <paramref name="branch"/>.</summary>
    private async Task<string> GetBranchShaAsync(GitHubOptions opts, string branch, CancellationToken ct)
    {
        var url = $"{GitHubApiBase}/repos/{Esc(opts.Owner)}/{Esc(opts.Repository)}/git/refs/heads/{Esc(branch)}";
        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, opts.Token);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        // Response is an array when the ref exists; grab the first element.
        var root = doc.RootElement;
        var refEl = root.ValueKind == JsonValueKind.Array ? root[0] : root;

        if (refEl.TryGetProperty("object", out var obj) && obj.TryGetProperty("sha", out var sha))
            return sha.GetString() ?? throw new InvalidOperationException($"Could not read SHA for branch '{branch}'.");

        throw new InvalidOperationException($"Unexpected response shape when resolving branch SHA for '{branch}'.");
    }

    /// <summary>Creates a new git ref (branch) in the repository.</summary>
    private async Task CreateBranchAsync(GitHubOptions opts, string newBranch, string fromSha, CancellationToken ct)
    {
        var url = $"{GitHubApiBase}/repos/{Esc(opts.Owner)}/{Esc(opts.Repository)}/git/refs";
        var body = new CreateRefRequest { Ref = $"refs/heads/{newBranch}", Sha = fromSha };

        using var request = CreateAuthorizedRequest(HttpMethod.Post, url, opts.Token);
        request.Content = JsonContent.Create(body, options: JsonOpts);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>Opens a draft pull request and returns its HTML URL.</summary>
    private async Task<string> CreateDraftPullRequestAsync(
        GitHubOptions opts, string head, string @base, string title, string body, CancellationToken ct)
    {
        var url = $"{GitHubApiBase}/repos/{Esc(opts.Owner)}/{Esc(opts.Repository)}/pulls";
        var prBody = new CreatePrRequest
        {
            Title = title,
            Body = body,
            Head = head,
            Base = @base,
            Draft = true
        };

        using var request = CreateAuthorizedRequest(HttpMethod.Post, url, opts.Token);
        request.Content = JsonContent.Create(prBody, options: JsonOpts);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        return doc.RootElement.TryGetProperty("html_url", out var el)
            ? el.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>Returns the blob SHA of an existing file, or null if it does not exist.</summary>
    private async Task<string?> GetExistingFileShaAsync(GitHubOptions opts, string url, string branch, CancellationToken ct)
    {
        var requestUri = $"{url}?ref={Esc(branch)}";
        using var request = CreateAuthorizedRequest(HttpMethod.Get, requestUri, opts.Token);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        return doc.RootElement.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    /// <summary>Creates an <see cref="HttpRequestMessage"/> with the Bearer token set on the message itself.</summary>
    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static string ContentsUrl(GitHubOptions opts, string filePath)
        => $"{GitHubApiBase}/repos/{Esc(opts.Owner)}/{Esc(opts.Repository)}/contents/{filePath}";

    private static string BuildFilePath(GitHubOptions opts, string fileName)
    {
        var folder = (opts.FolderPath ?? string.Empty).Trim('/');
        return string.IsNullOrWhiteSpace(folder)
            ? Esc(fileName)
            : $"{string.Join('/', folder.Split('/').Select(Esc))}/{Esc(fileName)}";
    }

    private static string BuildFallbackFileUrl(GitHubOptions opts, string filePath, string branch)
    {
        var decodedPath = Uri.UnescapeDataString(filePath);
        return $"https://github.com/{opts.Owner}/{opts.Repository}/blob/{branch}/{decodedPath}";
    }

    private static string BranchOrDefault(string? branch)
        => string.IsNullOrWhiteSpace(branch) ? "main" : branch;

    private static string Esc(string value) => Uri.EscapeDataString(value);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    private sealed class PutContentsRequest
    {
        [JsonPropertyName("message")]  public string Message { get; init; } = string.Empty;
        [JsonPropertyName("content")]  public string Content { get; init; } = string.Empty;
        [JsonPropertyName("branch")]   public string Branch  { get; init; } = "main";
        [JsonPropertyName("sha")]      public string? Sha    { get; init; }
    }

    private sealed class CreateRefRequest
    {
        [JsonPropertyName("ref")] public string Ref { get; init; } = string.Empty;
        [JsonPropertyName("sha")] public string Sha { get; init; } = string.Empty;
    }

    private sealed class CreatePrRequest
    {
        [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
        [JsonPropertyName("body")]  public string Body  { get; init; } = string.Empty;
        [JsonPropertyName("head")]  public string Head  { get; init; } = string.Empty;
        [JsonPropertyName("base")]  public string Base  { get; init; } = string.Empty;
        [JsonPropertyName("draft")] public bool   Draft { get; init; }
    }
}
