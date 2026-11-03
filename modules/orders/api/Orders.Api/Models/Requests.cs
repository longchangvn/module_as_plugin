namespace Orders.Api.Models;

public sealed record MigrateRequest(string FromVersion, string ToVersion);
