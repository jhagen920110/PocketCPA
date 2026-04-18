using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace api.Middleware;

public class AuthMiddleware : IFunctionsWorkerMiddleware
{
    // Shared-account mapping: certain emails collapse to a single shared userId
    // so multiple people (e.g. spouses) see the same data. Anything not in this
    // map uses the email itself as the userId.
    private static readonly Dictionary<string, string> EmailToUserId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jhagen920110@gmail.com"] = "jonathanh",
        ["dee0624kim@gmail.com"] = "jonathanh",
        ["local-dev@test.com"] = "jonathanh"
    };

    public static string ResolveUserId(string email)
    {
        var key = email.Trim().ToLowerInvariant();
        return EmailToUserId.TryGetValue(key, out var mapped) ? mapped : key;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var request = await context.GetHttpRequestDataAsync();
        if (request != null)
        {
            var email = ResolveEmail(request);
            if (!string.IsNullOrEmpty(email))
            {
                context.Items["UserId"] = ResolveUserId(email);
                context.Items["UserEmail"] = email;
            }
        }

        await next(context);
    }

    /// <summary>
    /// Prefers the signed SWA principal header (x-ms-client-principal), which
    /// cannot be spoofed because SWA strips any client-supplied value and
    /// re-injects it on authenticated requests. Falls back to X-User-Email for
    /// local dev (func start) where the SWA proxy is not in front.
    /// </summary>
    private static string? ResolveEmail(Microsoft.Azure.Functions.Worker.Http.HttpRequestData request)
    {
        if (request.Headers.TryGetValues("x-ms-client-principal", out var principalValues))
        {
            var encoded = principalValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(encoded))
            {
                try
                {
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("userDetails", out var details))
                    {
                        var email = details.GetString();
                        if (!string.IsNullOrEmpty(email))
                            return email.ToLowerInvariant().Trim();
                    }
                }
                catch
                {
                    // Fall through to X-User-Email below.
                }
            }
        }

        if (request.Headers.TryGetValues("X-User-Email", out var emailValues))
        {
            var email = emailValues.FirstOrDefault();
            // Only trust X-User-Email when running locally (no SWA principal present).
            // In production, SWA always injects x-ms-client-principal for authenticated
            // requests, so X-User-Email is ignored.
            if (!string.IsNullOrEmpty(email) && IsLocalDev())
            {
                return email.ToLowerInvariant().Trim();
            }
        }

        return null;
    }

    private static bool IsLocalDev()
    {
        // Functions runtime sets AZURE_FUNCTIONS_ENVIRONMENT=Development locally
        // and Production in Azure by default.
        var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}

public static class FunctionContextExtensions
{
    public static string? GetUserId(this FunctionContext context)
    {
        return context.Items.TryGetValue("UserId", out var userId) ? userId as string : null;
    }

    public static string? GetUserEmail(this FunctionContext context)
    {
        return context.Items.TryGetValue("UserEmail", out var email) ? email as string : null;
    }
}
