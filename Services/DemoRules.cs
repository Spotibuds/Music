using MongoDB.Bson;
using System.Security.Claims;

namespace Music.Services;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public static class DemoRules
{
    public static bool DependencyFailure(Exception ex) => ex is MongoDB.Driver.MongoException or TimeoutException or Azure.RequestFailedException or HttpRequestException or IOException
        || ex is AggregateException aggregate && aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(DependencyFailure);
    public static string Id(string value)
    {
        if (!ObjectId.TryParse(value, out _)) throw new ApiException(400, "A valid catalogue ObjectId is required.");
        return value;
    }
    public static string Text(string? value, string field, int max, bool required = true)
    {
        var text = value?.Trim() ?? "";
        if ((required && text.Length == 0) || text.Length > max) throw new ApiException(400, $"{field} must contain {(required ? "1" : "0")} to {max} characters.");
        return text;
    }
    public static void Duration(int seconds)
    {
        if (seconds is < 1 or > 7200) throw new ApiException(400, "Duration must be between 1 and 7200 seconds.");
    }
    public static int Page(int? limit, int skip)
    {
        if (skip is < 0 or > 10000 || limit is < 1 or > 100) throw new ApiException(400, "Limit must be 1..100 and skip 0..10000.");
        return limit ?? 50;
    }
    public static string Actor(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? throw new ApiException(401, "Sign in is required.");
}

// All commands share this gate. The demo intentionally runs one Music instance.
public sealed class MutationGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public async Task<T> Run<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await Semaphore.WaitAsync(ct);
        try { return await action(); }
        finally { Semaphore.Release(); }
    }
}
