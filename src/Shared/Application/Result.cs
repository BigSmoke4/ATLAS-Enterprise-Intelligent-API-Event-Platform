namespace Atlas.Shared.Application;

/// <summary>
/// Standard result envelope for application-layer use cases so controllers
/// never need to inspect exceptions to decide on HTTP status codes.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }

    protected Result(bool isSuccess, string? error, string? errorCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, string errorCode = "ERROR") => new(false, error, errorCode);
    public static Result<T> Success<T>(T value) => new(value, true, null, null);
    public static Result<T> Failure<T>(string error, string errorCode = "ERROR") => new(default, false, error, errorCode);
}

public class Result<T> : Result
{
    public T? Value { get; }
    internal Result(T? value, bool isSuccess, string? error, string? errorCode) : base(isSuccess, error, errorCode)
    {
        Value = value;
    }
}
