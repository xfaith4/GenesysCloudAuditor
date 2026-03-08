using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/architect/ivrs
/// </summary>
public sealed class IvrsPageDto
{
    [JsonPropertyName("entities")]
    public List<IvrDto>? Entities { get; init; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; init; }

    [JsonPropertyName("pageCount")]
    public int? PageCount { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }
}

/// <summary>
/// Represents a single IVR entry from GET /api/v2/architect/ivrs.
/// Each IVR binds one or more DNIS (Direct Inward Dial) numbers to
/// open-hours, closed-hours, and holiday-hours Architect flows.
/// </summary>
public sealed class IvrDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Phone number(s) (DNIS) routed through this IVR.</summary>
    [JsonPropertyName("dnis")]
    public List<string>? Dnis { get; init; }

    /// <summary>Flow invoked during open (business) hours.</summary>
    [JsonPropertyName("openHoursFlow")]
    public IvrFlowRefDto? OpenHoursFlow { get; init; }

    /// <summary>Flow invoked when outside business hours.</summary>
    [JsonPropertyName("closedHoursFlow")]
    public IvrFlowRefDto? ClosedHoursFlow { get; init; }

    /// <summary>Flow invoked on configured holiday dates.</summary>
    [JsonPropertyName("holidayHoursFlow")]
    public IvrFlowRefDto? HolidayHoursFlow { get; init; }

    /// <summary>Schedule group controlling which flow applies at any given time.</summary>
    [JsonPropertyName("scheduleGroup")]
    public IvrScheduleGroupRefDto? ScheduleGroup { get; init; }
}

/// <summary>Minimal flow reference embedded in an IVR binding slot.</summary>
public sealed class IvrFlowRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>Minimal schedule-group reference embedded in an IVR.</summary>
public sealed class IvrScheduleGroupRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
