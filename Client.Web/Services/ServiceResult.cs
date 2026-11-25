namespace Client.Web.Services;

public sealed record ServiceError(string Code, string Message);

public class ServiceResult
{
    protected ServiceResult(ServiceError? error)
    {
        Error = error;
    }

    public ServiceError? Error { get; }

    public bool Succeeded => Error is null;

    public static ServiceResult Success() => new(null);

    public static ServiceResult Failure(ServiceError error) => new(error);
}

public sealed class ServiceResult<T> : ServiceResult
{
    private ServiceResult(T? value, ServiceError? error) : base(error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static ServiceResult<T> Success(T value) => new(value, null);

    public static new ServiceResult<T> Failure(ServiceError error) => new(default, error);
}
