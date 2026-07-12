using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Web;

/// <summary>
/// Deduplicates POST/PUT requests using the Idempotency-Key header (UUID).
/// On a duplicate key the cached response is replayed without re-executing the handler.
/// Keys expire after 24 hours.
/// </summary>
public sealed class IdempotencyMiddleware(
    IDistributedCache cache,
    ILogger<IdempotencyMiddleware> logger)
    : IMiddleware
{
    private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(24);
    private static readonly string[] IdempotentMethods = ["POST", "PUT", "PATCH"];

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        if (!IdempotentMethods.Contains(ctx.Request.Method, StringComparer.OrdinalIgnoreCase))
        {
            await next(ctx).ConfigureAwait(false);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues)
            || string.IsNullOrWhiteSpace(keyValues.FirstOrDefault()))
        {
            await next(ctx).ConfigureAwait(false);
            return;
        }

        var rawKey = keyValues.First()!;
        if (!Guid.TryParse(rawKey, out _))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new
            {
                type   = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                title  = "Bad Request",
                status = 400,
                detail = "Idempotency-Key must be a valid UUID.",
            }).ConfigureAwait(false);
            return;
        }

        var cacheKey = CacheKey(ctx, rawKey);
        var cached   = await cache.GetStringAsync(cacheKey).ConfigureAwait(false);

        if (cached is not null)
        {
            logger.LogInformation(
                "Idempotent replay: Key={Key} Path={Path}",
                rawKey, ctx.Request.Path);

            ctx.Response.Headers["X-Idempotent-Replayed"] = "true";
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode  = StatusCodes.Status200OK;
            await ctx.Response.WriteAsync(cached).ConfigureAwait(false);
            return;
        }

        // Buffer the response so we can cache it.
        var originalBody = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        await next(ctx).ConfigureAwait(false);

        buffer.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(buffer).ReadToEndAsync().ConfigureAwait(false);

        // Only cache successful responses (2xx).
        if (ctx.Response.StatusCode is >= 200 and < 300)
        {
            await cache.SetStringAsync(cacheKey, responseBody, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = KeyTtl,
            }).ConfigureAwait(false);
        }

        buffer.Seek(0, SeekOrigin.Begin);
        await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
        ctx.Response.Body = originalBody;
    }

    private static string CacheKey(HttpContext ctx, string idempotencyKey)
    {
        // Key is scoped to tenant + path + idempotency key to prevent cross-tenant replay.
        var tenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "global";
        var raw      = $"{tenantId}:{ctx.Request.Path}:{idempotencyKey}";
        var hash     = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"idempotency:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
