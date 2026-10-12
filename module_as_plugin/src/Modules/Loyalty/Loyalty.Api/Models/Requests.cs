namespace Loyalty.Api.Models;

public sealed record MigrateRequest(string FromVersion, string ToVersion);
