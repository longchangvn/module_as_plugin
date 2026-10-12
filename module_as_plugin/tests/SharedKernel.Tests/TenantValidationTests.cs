using SharedKernel;
using Xunit;

namespace SharedKernel.Tests;

// Tenant validation (V-C1..V-C5) + resolution order (SR-4).
public sealed class TenantValidationTests
{
    [Fact]
    public void Valid_create_passes()
    {
        var errors = TenantValidator.ValidateCreate("Acme Retail", "acme", "path", null, null, "ops@acme.example");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Name_too_short_rejected(string? name)
    {
        var errors = TenantValidator.ValidateCreate(name, "acme", "path", null, null, "ops@acme.example");
        Assert.Contains(errors, e => e.Field == "name");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("t")]
    [InlineData("login")]
    public void Reserved_slug_rejected(string slug)
    {
        var errors = TenantValidator.ValidateCreate("Acme", slug, "path", null, null, "ops@acme.example");
        Assert.Contains(errors, e => e.Field == "slug");
    }

    [Theory]
    [InlineData("ACME")]
    [InlineData("acme_inc")]
    [InlineData("ac me")]
    public void Bad_slug_charset_rejected(string slug)
    {
        var errors = TenantValidator.ValidateCreate("Acme", slug, "path", null, null, "ops@acme.example");
        Assert.Contains(errors, e => e.Field == "slug");
    }

    [Fact]
    public void Domain_mode_requires_domain()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "domain", null, null, "ops@acme.example");
        Assert.Contains(errors, e => e.Field == "domain");
    }

    [Theory]
    [InlineData("https://acme.loyalty.com")]
    [InlineData("Acme.Loyalty.Com")]
    [InlineData("acme.loyalty.com:8080")]
    [InlineData("not a host")]
    public void Bad_domain_rejected(string domain)
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "domain", domain, null, "ops@acme.example");
        Assert.Contains(errors, e => e.Field == "domain");
    }

    [Fact]
    public void Bad_path_prefix_rejected()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, "/other", "ops@acme.example");
        Assert.Contains(errors, e => e.Field == "pathPrefix");
    }

    [Theory]
    [InlineData(null, "/t")]
    [InlineData("", "/t")]
    [InlineData("t", "/t")]
    [InlineData("/t", "/t")]
    [InlineData("/t/", "/t")]
    [InlineData("/tenants", "/tenants")]
    public void Path_base_normalized(string? configured, string expected)
    {
        Assert.Equal(expected, TenantValidator.NormalizePathBase(configured));
    }

    [Fact]
    public void Custom_path_base_accepted_in_prefix()
    {
        var errors = TenantValidator.ValidateCreate(
            "Acme", "acme", "path", null, "/tenants/acme", "ops@acme.example", pathBase: "/tenants");
        Assert.Empty(errors);
    }

    [Fact]
    public void Default_base_rejected_under_custom_base()
    {
        var errors = TenantValidator.ValidateCreate(
            "Acme", "acme", "path", null, "/t/acme", "ops@acme.example", pathBase: "/tenants");
        Assert.Contains(errors, e => e.Field == "pathPrefix");
    }

    [Fact]
    public void Default_path_prefix_uses_configured_base()
    {
        Assert.Equal("/t/acme", TenantValidator.DefaultPathPrefix("acme"));
        Assert.Equal("/tenants/acme", TenantValidator.DefaultPathPrefix("Acme", "/tenants"));
    }

    [Fact]
    public void Path_slug_uses_configured_base()
    {
        Assert.Equal("acme", TenantResolution.PathSlug("/t/acme/orders"));
        Assert.Null(TenantResolution.PathSlug("/t/acme/orders", "/tenants"));
        Assert.Equal("acme", TenantResolution.PathSlug("/tenants/acme/orders", "/tenants"));
    }

    [Fact]
    public void Bad_email_rejected()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, null, "not-an-email");
        Assert.Contains(errors, e => e.Field == "contactEmail");
    }

    [Fact]
    public void Unknown_resolution_mode_rejected()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "pigeon", null, null, "ops@acme.example");
        Assert.Contains(errors, e => e.Field == "resolutionMode");
    }

    [Fact]
    public void Update_omits_slug_and_allows_partial()
    {
        var errors = TenantValidator.ValidateUpdate("New Name", null, null, null, null);
        Assert.Empty(errors);
    }

    [Fact]
    public void Shared_without_schema_rejected()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, null, "ops@acme.example", "shared", null, null);
        Assert.Contains(errors, e => e.Field == "schema");
    }

    [Theory]
    [InlineData("public")]
    [InlineData("pg_temp")]
    [InlineData("information_schema")]
    [InlineData("ACME")]
    public void Reserved_schema_rejected(string schema)
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, null, "ops@acme.example", "shared", null, schema);
        Assert.Contains(errors, e => e.Field == "schema");
    }

    [Theory]
    [InlineData("1bad")]
    [InlineData("has space")]
    [InlineData("UPPER")]
    public void Bad_dbname_rejected(string dbName)
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, null, "ops@acme.example", "dedicated", dbName, null);
        Assert.Contains(errors, e => e.Field == "dbName");
    }

    [Fact]
    public void Unknown_deployment_mode_rejected()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, null, "ops@acme.example", "serverless", null, null);
        Assert.Contains(errors, e => e.Field == "deploymentMode");
    }

    [Fact]
    public void Valid_shared_passes()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, null, "ops@acme.example", "shared", "shared_db", "acme");
        Assert.Empty(errors);
    }

    [Fact]
    public void Bad_bootstrap_email_rejected()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, null, "ops@acme.example",
            "dedicated", null, null, "not-an-email");
        Assert.Contains(errors, e => e.Field == "bootstrapAdminEmail");
    }

    [Fact]
    public void Valid_bootstrap_email_passes()
    {
        var errors = TenantValidator.ValidateCreate("Acme", "acme", "path", null, null, "ops@acme.example",
            "dedicated", null, null, "admin@acme.example");
        Assert.Empty(errors);
    }
}

public sealed class TenantResolutionTests
{
    [Fact]
    public void Header_first_host_second_path_third()
    {
        var c = TenantResolution.Candidates("tnt_1", "acme.loyalty.com:5100", "/t/acme/admin");
        Assert.Equal(3, c.Count);
        Assert.Equal(TenantResolutionSource.Header, c[0].Source);
        Assert.Equal("tnt_1", c[0].Key);
        Assert.Equal(TenantResolutionSource.Host, c[1].Source);
        Assert.Equal("acme.loyalty.com", c[1].Key); // port stripped, lowercased
        Assert.Equal(TenantResolutionSource.Path, c[2].Source);
        Assert.Equal("acme", c[2].Key);
    }

    [Fact]
    public void Empty_inputs_yield_no_candidates()
    {
        Assert.Empty(TenantResolution.Candidates(null, null, "/healthz"));
        Assert.Empty(TenantResolution.Candidates(" ", " ", null));
    }

    [Fact]
    public void Non_tenant_path_yields_no_path_candidate()
    {
        var c = TenantResolution.Candidates(null, "loyalty.com", "/api/v1/platform/tenants");
        Assert.DoesNotContain(c, x => x.Source == TenantResolutionSource.Path);
    }
}
