namespace PdfStudio.Application.Common;

/// <summary>
/// 操作の成功/失敗を表現するResult型。
/// </summary>
public readonly record struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }

    private Result(bool ok, T? value, string? error, Exception? ex)
    {
        IsSuccess = ok;
        Value = value;
        ErrorMessage = error;
        Exception = ex;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(string message, Exception? ex = null) =>
        new(false, default, message, ex);
}

public readonly record struct Result
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }

    private Result(bool ok, string? error, Exception? ex)
    {
        IsSuccess = ok;
        ErrorMessage = error;
        Exception = ex;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string message, Exception? ex = null) =>
        new(false, message, ex);
}
