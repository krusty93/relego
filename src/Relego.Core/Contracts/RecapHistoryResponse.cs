namespace Relego.Core.Contracts;

/// <summary>
/// Recent recap delivery attempts, newest first.
/// </summary>
public sealed record RecapHistoryResponse
{
    /// <summary>Delivery attempts on the current page.</summary>
    public List<RecapHistoryItemDto> Items { get; set; } = [];
}

/// <summary>
/// A single recap delivery attempt.
/// </summary>
public sealed record RecapHistoryItemDto
{
    /// <summary>Recap job identifier.</summary>
    public int Id { get; set; }

    /// <summary>Slot the recap was scheduled for, in UTC.</summary>
    public DateTimeOffset ScheduledFor { get; set; }

    /// <summary>Delivery state: <c>pending</c>, <c>delivered</c>, or <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of delivery attempts made.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Failure detail when <c>status</c> is <c>failed</c>; otherwise <c>null</c>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>When the recap was delivered. <c>null</c> when it was not.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>When the job record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
