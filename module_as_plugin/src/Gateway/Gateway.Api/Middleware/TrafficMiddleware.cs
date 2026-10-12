using Gateway.Api.Services;

namespace Gateway.Api.Middleware;

public sealed class TrafficMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TrafficRecorder recorder)
    {
        await next(ctx);
        recorder.Record(ctx.Request.Path.Value ?? "(unknown)");
    }
}
