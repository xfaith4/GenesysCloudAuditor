// File: src/GenesysExtensionAudit.Infrastructure/Logging/Logging.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Context;

namespace GenesysExtensionAudit.Infrastructure.Logging;

/// <summary>
/// Centralized logging + diagnostics configuration:
/// - Serilog configuration (console + rolling file)
/// - Request correlation (AsyncLocal scope + optional outbound HttpClient handler)
/// - Redaction utilities for secrets (headers/query/body)
/// </summary>
public static class Logging
{
    public const string CorrelationHeaderName = "X-Correlation-ID";
    public const string CorrelationPropertyName = "CorrelationId";

    /// <summary>
    /// Configure Serilog and bridge it into Microsoft.Extensions.Logging.
    /// Call from HostBuilder.ConfigureLogging or early during bootstrap.
    /// </summary>
    public static void ConfigureSerilog(IHostBuilder hostBuilder)
    {
        hostBuilder.UseSerilog((ctx, services, cfg) =>
        {
            var options = ctx.Configuration.GetSection("Logging").Get<LoggingOptions>() ?? new LoggingOptions();

            cfg.MinimumLevel.Is(options.MinimumLevel);

            // Reduce common noise
            cfg.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);
            cfg.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning);

            cfg.Enrich.FromLogContext()
               .Enrich.WithMachineName()
               .Enrich.WithProcessId()
               .Enrich.WithThreadId();

            // Friendly/compact JSON logs to file for diagnostics
            var rawLogDir = string.IsNullOrWhiteSpace(options.LogDirectory) ? "logs" : options.LogDirectory;
            var logDir = Path.IsPathRooted(rawLogDir)
                ? rawLogDir
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rawLogDir));
            var filePath = Path.Combine(logDir, "app-.log");

            if (options.EnableConsole)
            {
                cfg.WriteTo.Console(
                    outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] ({CorrelationId}) {Message:lj}{NewLine}{Exception}");
            }

            if (options.EnableFile)
            {
                Directory.CreateDirectory(logDir);
                CleanupOldLogFiles(logDir, options.RetainedDays);
                cfg.WriteTo.File(
                    formatter: new CompactJsonFormatter(),
                    path: filePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.RetainedFileCount,
                    fileSizeLimitBytes: options.MaxFileSizeMb * 1024L * 1024L,
                    rollOnFileSizeLimit: true,
                    restrictedToMinimumLevel: options.FileMinimumLevel);
            }

            // Allow adding additional enrichers via DI if desired
            foreach (var enricher in services.GetServices<ILogEventEnricher>())
                cfg.Enrich.With(enricher);
        });
    }

    /// <summary>
    /// Optional Serilog self-diagnostics (helps when sinks/config break).
    /// Writes to logs/serilog-selflog.txt by default.
    /// </summary>
    public static void EnableSerilogSelfLog(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "logs", "serilog-selflog.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        SelfLog.Enable(message =>
        {
            try
            {
                File.AppendAllText(path, $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
            }
            catch
            {
                // best-effort; never throw
            }
        });
    }

    /// <summary>
    /// Register correlation + HTTP diagnostics helpers.
    /// Add this during DI setup.
    /// </summary>
    public static IServiceCollection AddDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();
        services.AddSingleton<CorrelationHttpMessageHandler>();
        return services;
    }

    /// <summary>
    /// Adds the correlation handler to an HttpClient pipeline.
    /// Example:
    /// services.AddHttpClient("Genesys").AddHttpMessageHandler&lt;CorrelationHttpMessageHandler&gt;();
    /// </summary>
    public static IHttpClientBuilder AddRequestCorrelation(this IHttpClientBuilder builder)
        => builder.AddHttpMessageHandler<CorrelationHttpMessageHandler>();

    /// <summary>
    /// Begin a correlation scope for an operation (e.g., "Run Audit" button click).
    /// Disposes scope when complete.
    /// </summary>
    public static IDisposable BeginCorrelationScope(
        Microsoft.Extensions.Logging.ILogger logger,
        ICorrelationIdAccessor accessor,
        string? correlationId = null)
    {
        correlationId ??= CorrelationId.New();
        accessor.Set(correlationId);

        // Add to Serilog + MEL scope
        var serilogScope = LogContext.PushProperty(CorrelationPropertyName, correlationId);
        var melScope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationPropertyName] = correlationId
        });

        return new CompositeDisposable(serilogScope, melScope ?? new DelegateDisposable(() => { }), new DelegateDisposable(() => accessor.Clear()));
    }

    private sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
    {
        public void Dispose()
        {
            for (var i = disposables.Length - 1; i >= 0; i--)
            {
                try { disposables[i].Dispose(); } catch { /* swallow */ }
            }
        }
    }

    private sealed class DelegateDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private static void CleanupOldLogFiles(string logDirectory, int retainedDays)
    {
        if (retainedDays <= 0 || !Directory.Exists(logDirectory))
            return;

        var cutoffUtc = DateTime.UtcNow.AddDays(-retainedDays);
        foreach (var file in Directory.EnumerateFiles(logDirectory, "*.log*"))
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite < cutoffUtc)
                    File.Delete(file);
            }
            catch
            {
                // best effort; do not fail app startup because of log cleanup
            }
        }
    }
}

/// <summary>
/// Strongly-typed logging options (appsettings.json: "Logging").
/// </summary>
public sealed class LoggingOptions
{
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    public bool EnableConsole { get; set; } = true;
    public bool EnableFile { get; set; } = true;

    public LogEventLevel FileMinimumLevel { get; set; } = LogEventLevel.Information;

    /// <summary>Directory for rolling files, default "logs".</summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>How many rolling files to keep.</summary>
    public int RetainedFileCount { get; set; } = 14;

    /// <summary>If true, enables Serilog internal selflog file output.</summary>
    public bool EnableSerilogSelfLog { get; set; } = false;

    /// <summary>Max file size (MB) before rolling to a new file.</summary>
    public int MaxFileSizeMb { get; set; } = 20;

    /// <summary>Maximum age of log files in days.</summary>
    public int RetainedDays { get; set; } = 8;
}

/// <summary>
/// Provides ambient correlation id via AsyncLocal, suitable for desktop/WPF.
/// </summary>
public interface ICorrelationIdAccessor
{
    string? Get();
    void Set(string correlationId);
    void Clear();
}

internal sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? Get() => Current.Value;

    public void Set(string correlationId) => Current.Value = correlationId;

    public void Clear() => Current.Value = null;
}

/// <summary>
/// Ensures every outbound request has a correlation id header and logging scope.
/// Add to HttpClient pipeline.
/// </summary>
public sealed class CorrelationHttpMessageHandler : DelegatingHandler
{
    private readonly ICorrelationIdAccessor _accessor;
    private readonly ILogger<CorrelationHttpMessageHandler> _logger;

    public CorrelationHttpMessageHandler(
        ICorrelationIdAccessor accessor,
        ILogger<CorrelationHttpMessageHandler> logger)
    {
        _accessor = accessor;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corr = _accessor.Get();
        if (string.IsNullOrWhiteSpace(corr))
        {
            corr = CorrelationId.New();
            _accessor.Set(corr);
        }

        // Ensure header exists
        if (!request.Headers.TryGetValues(Logging.CorrelationHeaderName, out _))
            request.Headers.TryAddWithoutValidation(Logging.CorrelationHeaderName, corr);

        using var serilogScope = LogContext.PushProperty(Logging.CorrelationPropertyName, corr);
        using var melScope = _logger.BeginScope(new Dictionary<string, object>
        {
            [Logging.CorrelationPropertyName] = corr
        });

        // Minimal diagnostics; avoid logging secrets
        _logger.LogDebug(
            "HTTP {Method} {Uri} (Headers={Headers})",
            request.Method.Method,
            request.RequestUri?.ToString(),
            Redaction.RedactHeaders(request.Headers));

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            _logger.LogInformation(
                "HTTP {Method} {Uri} -> {StatusCode} in {ElapsedMs}ms (CorrelationId={CorrelationId})",
                request.Method.Method,
                request.RequestUri?.ToString(),
                (int)response.StatusCode,
                sw.ElapsedMilliseconds,
                corr);

            return response;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                ex,
                "HTTP {Method} {Uri} failed after {ElapsedMs}ms (CorrelationId={CorrelationId})",
                request.Method.Method,
                request.RequestUri?.ToString(),
                sw.ElapsedMilliseconds,
                corr);
            throw;
        }
    }
}

public static class CorrelationId
{
    // Compact, sortable-ish, very low collision; fine for diagnostics.
    public static string New()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

/// <summary>
/// Utilities to redact secrets from logs (headers/query/body).
/// Intended for diagnostics logs of HTTP requests/responses.
/// </summary>
public static class Redaction
{
    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "X-Api-Key",
        "Api-Key",
        "X-Client-Secret",
        "Set-Cookie",
        "Cookie"
    };

    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "client_secret",
        "clientSecret",
        "secret",
        "password",
        "access_token",
        "refresh_token",
        "token",
        "apikey",
        "api_key"
    };

    private static readonly HashSet<string> SensitiveJsonKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "client_secret",
        "clientSecret",
        "secret",
        "password",
        "access_token",
        "refresh_token",
        "token",
        "apiKey",
        "api_key"
    };

    private const string Mask = "***REDACTED***";

    public static IReadOnlyDictionary<string, string> RedactHeaders(System.Net.Http.Headers.HttpHeaders headers)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
        {
            if (SensitiveHeaderNames.Contains(h.Key))
            {
                dict[h.Key] = Mask;
                continue;
            }

            // Avoid super noisy multi-value headers
            dict[h.Key] = string.Join(",", h.Value.Select(v => v.Length > 256 ? v[..256] + "…" : v));
        }
        return dict;
    }

    public static string RedactUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return string.Empty;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) && !Uri.TryCreate(uri, UriKind.Relative, out u))
            return uri;

        if (u is null) return uri;

        var raw = u.ToString();
        var qIndex = raw.IndexOf('?', StringComparison.Ordinal);
        if (qIndex < 0) return raw;

        var basePart = raw[..qIndex];
        var query = raw[(qIndex + 1)..];

        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var kvp = parts[i].Split('=', 2);
            var key = Uri.UnescapeDataString(kvp[0]);
            if (SensitiveQueryKeys.Contains(key))
            {
                parts[i] = kvp.Length == 2 ? $"{kvp[0]}={Mask}" : $"{kvp[0]}={Mask}";
            }
        }

        return $"{basePart}?{string.Join("&", parts)}";
    }

    public static string RedactJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var redacted = RedactElement(doc.RootElement);
            return JsonSerializer.Serialize(redacted, new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            // fallback: regex masking for common patterns if not valid JSON
            return Regex.Replace(
                json,
                "(?i)\"(client_secret|clientSecret|password|access_token|refresh_token|token|apiKey|api_key)\"\\s*:\\s*\".*?\"",
                m =>
                {
                    var key = Regex.Match(m.Value, "(?i)\"(client_secret|clientSecret|password|access_token|refresh_token|token|apiKey|api_key)\"").Groups[1].Value;
                    return $"\"{key}\":\"{Mask}\"";
                });
        }
    }

    private static object? RedactElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => RedactObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, object?> RedactObject(JsonElement obj)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.EnumerateObject())
        {
            if (SensitiveJsonKeys.Contains(prop.Name))
            {
                dict[prop.Name] = Mask;
            }
            else
            {
                dict[prop.Name] = RedactElement(prop.Value);
            }
        }
        return dict;
    }
}
