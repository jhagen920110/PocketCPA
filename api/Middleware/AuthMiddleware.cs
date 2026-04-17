using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace api.Middleware;

public class AuthMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var request = await context.GetHttpRequestDataAsync();
        if (request != null)
        {
            var email = request.Headers.TryGetValues("X-User-Email", out var values)
                ? values.FirstOrDefault()?.ToLowerInvariant().Trim()
                : null;

            if (!string.IsNullOrEmpty(email))
            {
                context.Items["UserId"] = email;
            }
        }

        await next(context);
    }
}

public static class FunctionContextExtensions
{
    public static string? GetUserId(this FunctionContext context)
    {
        return context.Items.TryGetValue("UserId", out var userId) ? userId as string : null;
    }
}
