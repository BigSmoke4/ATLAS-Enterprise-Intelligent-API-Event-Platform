namespace Atlas.Shared.Application;

/// <summary>Shared clamp so every module's paginated list endpoint enforces the same sane bounds (page >= 1, 1 <= pageSize <= 200).</summary>
public static class Paging
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static (int Page, int PageSize) Clamp(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize));
}
