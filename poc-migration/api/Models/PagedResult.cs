namespace PocApi.Models;

/// <summary>Paginated response wrapper returned by list endpoints.</summary>
public sealed class PagedResult<T>
{
    /// <summary>The records on the current page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>1-based current page number.</summary>
    public required int Page { get; init; }

    /// <summary>Maximum items per page (1–100).</summary>
    public required int PageSize { get; init; }

    /// <summary>Total records across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));
}
