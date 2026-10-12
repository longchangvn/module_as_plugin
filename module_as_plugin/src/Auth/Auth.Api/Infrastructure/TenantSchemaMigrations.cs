using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Auth.Api.Infrastructure;

// Stamps the tenant schema onto migration operations at apply time so the
// scaffolded (schema-less) migration files work per shared schema — the same
// problem/solution shape as schema-stamped DDL (§27b-style). Table/index/
// constraint names are untouched: Postgres scopes them per schema natively.
//
// NOTE: typed Generate overrides (not the MigrationOperation dispatcher —
// Npgsql dispatches typed overloads directly, so a dispatcher override never
// runs for index operations). Registered only for shared-schema contexts,
// with the schema carried by TenantSchemaOptionsExtension (per-schema
// internal service provider).
public sealed record TenantSchemaHolder(string? Schema);

public sealed class TenantSchemaOptionsExtension(string? schema) : IDbContextOptionsExtension
{
    public string? Schema { get; } = schema;

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) =>
        services.AddSingleton(new TenantSchemaHolder(Schema));

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        private readonly TenantSchemaOptionsExtension _ext;

        public ExtensionInfo(TenantSchemaOptionsExtension ext) : base(ext) => _ext = ext;

        public override bool IsDatabaseProvider => false;
        public override string LogFragment => $"TenantSchema={_ext.Schema} ";
        public override int GetServiceProviderHashCode() => (_ext.Schema ?? "").GetHashCode();
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo o && o._ext.Schema == _ext.Schema;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["TenantSchema"] = _ext.Schema ?? "";
    }
}

public sealed class TenantSchemaMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.INpgsqlSingletonOptions npgsqlOptions,
    TenantSchemaHolder holder)
    : NpgsqlMigrationsSqlGenerator(dependencies, npgsqlOptions)
{
    private string? Schema => string.IsNullOrEmpty(holder.Schema) ? null : holder.Schema;

    // Net for operations without a typed virtual (rename/data/check ops):
    // stamps when Npgsql routes through the dispatcher.
    protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder b)
    {
        StampDispatch(operation);
        base.Generate(operation, model, b);
    }

    private void StampDispatch(MigrationOperation op)
    {
        if (Schema is null) return;
        switch (op)
        {
            case RenameTableOperation r:
                r.Schema ??= Schema;
                r.NewSchema ??= Schema;
                break;
            case RenameIndexOperation i:
                i.Schema ??= Schema;
                break;
            case AlterColumnOperation c:
                c.Schema ??= Schema;
                break;
            case RenameColumnOperation c:
                c.Schema ??= Schema;
                break;
            case AddUniqueConstraintOperation u:
                u.Schema ??= Schema;
                break;
            case AddCheckConstraintOperation c:
                c.Schema ??= Schema;
                break;
            case InsertDataOperation d:
                d.Schema ??= Schema;
                break;
            case UpdateDataOperation d:
                d.Schema ??= Schema;
                break;
            case DeleteDataOperation d:
                d.Schema ??= Schema;
                break;
        }
    }

    protected override void Generate(CreateTableOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        foreach (var fk in op.ForeignKeys)
        {
            fk.Schema ??= Schema;
            fk.PrincipalSchema ??= Schema;
        }
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(DropTableOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(CreateIndexOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(DropIndexOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(AddColumnOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(DropColumnOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(AddForeignKeyOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        op.PrincipalSchema ??= Schema;
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(DropForeignKeyOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(AddPrimaryKeyOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        base.Generate(op, model, b, terminate);
    }

    protected override void Generate(InsertDataOperation op, IModel? model, MigrationCommandListBuilder b, bool terminate = true)
    {
        op.Schema ??= Schema;
        base.Generate(op, model, b, terminate);
    }
}
