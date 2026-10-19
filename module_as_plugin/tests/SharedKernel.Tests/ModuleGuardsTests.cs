using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace SharedKernel.Tests;

public sealed class ModuleGuardsTests
{
    [Fact]
    public void Present_permission_is_granted() =>
        Assert.True(ModuleGuards.HasPermission(["SAMPLE_ONE_DATA_VIEW", "SAMPLE_ONE_DATA_PROTECTED"], "SAMPLE_ONE_DATA_PROTECTED"));

    [Fact]
    public void Absent_permission_is_denied() =>
        Assert.False(ModuleGuards.HasPermission(["SAMPLE_TWO_DATA_PROTECTED"], "SAMPLE_ONE_DATA_PROTECTED"));

    [Fact]
    public void Null_permission_list_is_denied() =>
        Assert.False(ModuleGuards.HasPermission(null, "SAMPLE_ONE_DATA_PROTECTED"));

    [Fact]
    public void Permission_match_is_case_sensitive() =>
        Assert.False(ModuleGuards.HasPermission(["sample_one_data_protected"], "SAMPLE_ONE_DATA_PROTECTED"));

    private static HttpContext With(params Claim[] claims) =>
        new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };

    [Fact]
    public void UserId_prefers_userId_then_sub_then_name_identifier()
    {
        Assert.Equal("u1", ModuleGuards.UserId(With(new Claim("userId", "u1"), new Claim("sub", "u2"), new Claim(ClaimTypes.NameIdentifier, "u3"))));
        Assert.Equal("u2", ModuleGuards.UserId(With(new Claim("sub", "u2"), new Claim(ClaimTypes.NameIdentifier, "u3"))));
        Assert.Equal("u3", ModuleGuards.UserId(With(new Claim(ClaimTypes.NameIdentifier, "u3"))));
        Assert.Null(ModuleGuards.UserId(With()));
    }

    [Fact]
    public void TenantId_reads_tenant_claim() =>
        Assert.Equal("tnt_a", ModuleGuards.TenantId(With(new Claim("tenantId", "tnt_a"))));
}
