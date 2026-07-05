namespace Core;

public sealed record Error(string Code, string Message);

/// <summary>
/// Non-generic result for void-returning handlers.
/// Use Result.Ok() or Result.Fail(...) as factories.
/// </summary>
public readonly struct Result
{
    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public static Result Ok() => new(true, null);
    public static Result Fail(string message) => new(false, new Error("Error", message));
    public static Result Fail(Error error) => new(false, error);

    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Fail<T>(string message) => Result<T>.Fail(message);
    public static Result<T> Fail<T>(Error error) => Result<T>.Fail(error);
}

public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
        Error = null;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        _value = default;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed Result.");

    /// <summary>Factory alias — preferred over Success() at call sites.</summary>
    public static Result<T> Ok(T value) => new(value);

    /// <summary>Factory alias — preferred over Failure() at call sites.</summary>
    public static Result<T> Fail(string message) => new(new Error("Error", message));
    public static Result<T> Fail(Error error) => new(error);

    // Keep original names for backward compatibility with existing code
    public static Result<T> Success(T value) => Ok(value);
    public static Result<T> Failure(Error error) => Fail(error);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);
}
