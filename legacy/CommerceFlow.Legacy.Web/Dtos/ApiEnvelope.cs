namespace CommerceFlow.Legacy.Web.Dtos;

// Every business outcome -- success or failure -- goes through this envelope with HTTP 200.
// Only structurally invalid JSON gets a framework-level 400; that's ASP.NET Core's own model
// binding, not this envelope.
public class ApiEnvelope<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }

    public static ApiEnvelope<T> Ok(T data) => new() { Success = true, Data = data, Message = null };

    public static ApiEnvelope<T> Fail(string message) => new() { Success = false, Data = default, Message = message };
}
