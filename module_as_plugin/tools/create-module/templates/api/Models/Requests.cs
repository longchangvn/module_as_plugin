namespace __PASCAL__.Api.Models;

public sealed record MigrateRequest(string FromVersion, string ToVersion);
