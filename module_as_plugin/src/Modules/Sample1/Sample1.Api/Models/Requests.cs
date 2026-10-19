namespace Sample1.Api.Models;

public sealed record MigrateRequest(string FromVersion, string ToVersion);
