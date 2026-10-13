// Aspire orchestration (local dev): Postgres (volume-persisted) + 4 APIs +
// 3 Vite frontends, all on their fixed ports so existing URLs, ocelot.json
// downstreams, and inter-service BaseUrls keep working unchanged.
//
// Postgres is pinned to host :5434 (host :5432 belongs to another project,
// :5433 to the standalone msa-postgres container). First run initializes an
// empty module_platform database; both APIs auto-apply EF migrations and the
// dev seed (admin@local + demo tenant) on startup.
//
// Run: dotnet run --project src/Aspire/ModulePlatform.AppHost
var builder = DistributedApplication.CreateBuilder(args);

// Local-dev password (never production). Fixed so the data volume stays
// accessible across AppHost restarts (Aspire otherwise generates a random one).
var pgPassword = builder.AddParameter("pg-password", "module_platform_dev", publishValueAsDefault: false, secret: true);
const string pgHost = "localhost";
const int pgPort = 5434;
const string pgPortString = "5434";

var postgres = builder.AddPostgres("postgres", password: pgPassword)
    .WithEndpoint("tcp", e => { e.Port = pgPort; })
    .WithDataVolume("module-platform-pgdata");
var catalogDb = postgres.AddDatabase("Catalog", "module_platform");
var tenantDb = postgres.AddDatabase("Tenant", "module_platform");

var gateway = builder.AddProject<Projects.Gateway_Api>("gateway")
    .WithEndpoint("http", e => { e.Port = 5100; e.IsProxied = false; })
    .WithEnvironment("ASPNETCORE_URLS", "http://localhost:5100");

var auth = builder.AddProject<Projects.Auth_Api>("auth")
    .WithEndpoint("http", e => { e.Port = 5122; e.IsProxied = false; })
    .WithEnvironment("ASPNETCORE_URLS", "http://localhost:5122")
    .WithReference(tenantDb)
    .WithEnvironment("TenantStores__Host", pgHost)
    .WithEnvironment("TenantStores__Port", pgPort.ToString())
    .WithEnvironment("TenantStores__Username", "postgres")
    .WithEnvironment("TenantStores__Password", pgPassword)
    .WithEnvironment("TenantStores__SharedDatabase", "module_platform");

var platform = builder.AddProject<Projects.Platform_Api>("platform")
    .WithEndpoint("http", e => { e.Port = 5121; e.IsProxied = false; })
    .WithEnvironment("ASPNETCORE_URLS", "http://localhost:5121")
    .WithReference(catalogDb)
    .WithReference(gateway)
    .WithReference(auth)
    .WithEnvironment("TenantStores__ProvisioningConnection", ReferenceExpression.Create(
        $"Host={pgHost};Port={pgPortString};Username=postgres;Password={pgPassword};Database=postgres"))
    .WithEnvironment("TenantStores__SharedDatabase", "module_platform");

auth.WithReference(platform);

var loyalty = builder.AddProject<Projects.Loyalty_Api>("loyalty")
    .WithEndpoint("http", e => { e.Port = 5201; e.IsProxied = false; })
    .WithEnvironment("ASPNETCORE_URLS", "http://localhost:5201")
    .WithReference(platform);

var shell = builder.AddNpmApp("shell", "../../../apps/shell", "dev")
    .WithEndpoint("http", e => { e.Port = 5173; e.IsProxied = false; })
    .WithExternalHttpEndpoints()
    .WithReference(gateway);

var loyaltyWeb = builder.AddNpmApp("loyalty-web", "../../../modules/loyalty/web", "dev")
    .WithEndpoint("http", e => { e.Port = 5174; e.IsProxied = false; })
    .WithExternalHttpEndpoints();

var tenantAdmin = builder.AddNpmApp("tenant-admin", "../../../apps/tenant-admin", "dev")
    .WithEndpoint("http", e => { e.Port = 5175; e.IsProxied = false; })
    .WithExternalHttpEndpoints()
    .WithReference(gateway);

builder.Build().Run();
