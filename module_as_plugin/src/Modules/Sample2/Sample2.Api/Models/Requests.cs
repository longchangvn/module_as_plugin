namespace Sample2.Api.Models;

public sealed record MigrateRequest(string FromVersion, string ToVersion);
