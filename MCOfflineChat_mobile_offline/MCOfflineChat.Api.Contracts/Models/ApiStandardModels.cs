namespace MCOfflineChat.Api.Contracts.Models;

/// <summary>
/// v1.1.71: Standardized API error response format.
/// All endpoints should return this shape for error responses.
/// </summary>
public sealed class ApiErrorResponse
{
    /// <summary>Short error code (e.g., "VALIDATION_ERROR", "NOT_FOUND", "UNAUTHORIZED").</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Human-readable error description.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>ISO 8601 timestamp of the error.</summary>
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");

    /// <summary>Trace ID for correlating with server logs.</summary>
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N")[..16];

    /// <summary>Optional validation errors for input validation failures.</summary>
    public Dictionary<string, string[]>? ValidationErrors { get; set; }

    public static ApiErrorResponse BadRequest(string message, Dictionary<string, string[]>? validationErrors = null) => new()
    {
        Error = "BAD_REQUEST",
        Message = message,
        ValidationErrors = validationErrors
    };

    public static ApiErrorResponse NotFound(string message) => new()
    {
        Error = "NOT_FOUND",
        Message = message
    };

    public static ApiErrorResponse Unauthorized(string message = "Authentication required.") => new()
    {
        Error = "UNAUTHORIZED",
        Message = message
    };

    public static ApiErrorResponse Forbidden(string message = "Insufficient permissions.") => new()
    {
        Error = "FORBIDDEN",
        Message = message
    };

    public static ApiErrorResponse Internal(string message = "An internal server error occurred.") => new()
    {
        Error = "INTERNAL_ERROR",
        Message = message
    };

    public static ApiErrorResponse RateLimited(string message = "Too many requests. Please try again later.") => new()
    {
        Error = "RATE_LIMITED",
        Message = message
    };
}

/// <summary>
/// v1.1.71: Standardized paginated response wrapper.
/// All list endpoints should return PagedResult{T} with ?page=1&amp;size=50 parameters.
/// </summary>
public sealed class PagedResult<T>
{
    /// <summary>Items for the current page.</summary>
    public IReadOnlyList<T> Items { get; set; } = [];

    /// <summary>Total number of items across all pages.</summary>
    public int Total { get; set; }

    /// <summary>Current page number (1-based).</summary>
    public int Page { get; set; } = 1;

    /// <summary>Items per page.</summary>
    public int Size { get; set; } = 50;

    /// <summary>Total number of pages.</summary>
    public int TotalPages => Size > 0 ? (int)Math.Ceiling((double)Total / Size) : 0;

    /// <summary>Whether there is a next page.</summary>
    public bool HasNext => Page < TotalPages;

    /// <summary>Whether there is a previous page.</summary>
    public bool HasPrev => Page > 1;

    /// <summary>Creates a paged result from a full collection.</summary>
    public static PagedResult<T> Create(IEnumerable<T> source, int page, int size)
    {
        var items = source.ToList();
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        return new PagedResult<T>
        {
            Items = items.Skip((page - 1) * size).Take(size).ToList(),
            Total = items.Count,
            Page = page,
            Size = size
        };
    }
}
