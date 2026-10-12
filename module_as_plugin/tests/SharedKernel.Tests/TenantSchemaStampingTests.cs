using Auth.Api.Infrastructure;
using Auth.Api.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace SharedKernel.Tests;

// Schema-stamped DDL: shared tenants must get schema-qualified tables/indexes,
// dedicated tenants must stay unqualified.
public sealed class TenantSchemaStampingTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public TenantSchemaStampingTests(Xunit.Abstractions.ITestOutputHelper @out) => _out = @out;
    private static string GenerateSql(string? schema)
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "module_platform",
            Username = "module_platform",
            Password = "module_platform",
        };
        var builder = new DbContextOptionsBuilder<TenantDbContext>();
        if (schema is not null)
        {
            ((IDbContextOptionsBuilderInfrastructure)builder)
                .AddOrUpdateExtension(new TenantSchemaOptionsExtension(schema));
            builder.ReplaceService<IMigrationsSqlGenerator, TenantSchemaMigrationsSqlGenerator>();
            builder.UseNpgsql(csb.ToString(), o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        }
        else
        {
            builder.UseNpgsql(csb.ToString());
        }
        using var ctx = schema is null ? new TenantDbContext(builder.Options) : new TenantDbContext(builder.Options, schema);
        var infra = (IInfrastructure<IServiceProvider>)ctx;
        var generator = infra.GetService<IMigrationsSqlGenerator>();
        var migration = new InitialTenant();
        var commands = generator.Generate(migration.UpOperations, migration.TargetModel, MigrationsSqlGenerationOptions.Default);
        return string.Join("\n", commands.Select(c => c.CommandText));
    }

    [Fact]
    public void Shared_schema_is_stamped()
    {
        var sql = GenerateSql("t9");
        Assert.Contains("t9.\"Menus\"", sql);
        Assert.Contains("ON t9.\"Permissions\"", sql);
        Assert.DoesNotContain("CREATE TABLE \"Menus\"", sql);
        Assert.DoesNotContain("ON \"Permissions\"", sql);
    }

    [Fact]
    public void Dedicated_stays_unqualified()
    {
        var sql = GenerateSql(null);
        Assert.Contains("CREATE TABLE \"Menus\"", sql);
    }

    // Full MigrateAsync path (skipped when the local dev Postgres is absent).
    [Fact]
    public async Task MigrateAsync_lands_in_tenant_schema()
    {
        const string schema = "ittestx";
        var csb = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = 55432,
            Username = "module_platform",
            Password = "module_platform",
            Database = "module_platform",
            SearchPath = schema,
        };
        await using (var admin = new Npgsql.NpgsqlConnection(csb.ToString()))
        {
            try
            {
                await admin.OpenAsync();
            }
            catch
            {
                // Local dev Postgres (:55432) unavailable — nothing to verify against.
                return;
            }
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
            await using var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        var builder = new DbContextOptionsBuilder<TenantDbContext>();
        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new TenantSchemaOptionsExtension(schema));
        builder
            .ReplaceService<IMigrationsSqlGenerator, TenantSchemaMigrationsSqlGenerator>()
            .UseNpgsql(csb.ToString(), o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        await using var ctx = new TenantDbContext(builder.Options, schema);
        await ctx.Database.MigrateAsync();
        await using var check = new Npgsql.NpgsqlConnection(csb.ToString());
        await check.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'ittestx'", check);
        var tables = (long)(await cmd.ExecuteScalarAsync())!;
        await using var idx = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'ittestx'", check);
        var indexes = (long)(await idx.ExecuteScalarAsync())!;
        await using var cross = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace " +
            "WHERE n.nspname = 'ittestx' AND c.contype = 'f' AND c.confrelid::regclass::text NOT LIKE 'ittestx.%'", check);
        var badFks = (long)(await cross.ExecuteScalarAsync())!;
        Assert.True(tables >= 8, $"expected auth tables in schema, got {tables}");
        Assert.True(indexes >= 3, $"expected indexes in schema, got {indexes}");
        Assert.Equal(0, badFks);
    }
}
