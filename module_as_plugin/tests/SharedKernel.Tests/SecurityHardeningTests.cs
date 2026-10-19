using Auth.Api.Application;
using Microsoft.Extensions.Hosting;
using ModulePlatform.ServiceDefaults;
using Xunit;

namespace SharedKernel.Tests;

public sealed class RequireSecretTests
{
    private static HostApplicationBuilder Builder(string env, string? secret)
    {
        var b = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = env });
        if (secret is not null) b.Configuration["Jwt:SecretKey"] = secret;
        return b;
    }

    [Fact]
    public void Development_falls_back_to_dev_default_and_writes_it_back()
    {
        var b = Builder("Development", null);
        Assert.Equal(Extensions.DevJwtSecret, b.RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret));
        Assert.Equal(Extensions.DevJwtSecret, b.Configuration["Jwt:SecretKey"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData(Extensions.DevJwtSecret)]
    public void Production_fails_closed_on_missing_short_or_dev_secret(string? secret)
    {
        var b = Builder("Production", secret);
        var ex = Assert.Throws<InvalidOperationException>(() => b.RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret));
        Assert.Contains("Jwt__SecretKey", ex.Message);
        if (!string.IsNullOrEmpty(secret)) Assert.DoesNotContain(secret, ex.Message); // never echo the value
    }

    [Fact]
    public void Production_accepts_strong_secret()
    {
        var strong = new string('x', 48);
        Assert.Equal(strong, Builder("Production", strong).RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret));
    }
}

public sealed class LastAdminGuardTests
{
    private static readonly AdminGrant AliceAdmin = new("alice", "role-admin");
    private static readonly AdminGrant BobAdmin = new("bob", "role-admin");
    private static readonly AdminGrant AliceLoyaltyAdmin = new("alice", "role-loyalty-admin");

    [Fact]
    public void Removing_the_only_admin_grant_locks_out()
    {
        Assert.True(LastAdminGuard.WouldLockOut([AliceAdmin], g => g.UserId == "alice"));
    }

    [Fact]
    public void Removing_one_of_two_admins_is_allowed()
    {
        Assert.False(LastAdminGuard.WouldLockOut([AliceAdmin, BobAdmin], g => g.UserId == "alice"));
    }

    [Fact]
    public void Removing_one_admin_role_while_user_keeps_another_is_allowed()
    {
        Assert.False(LastAdminGuard.WouldLockOut([AliceAdmin, AliceLoyaltyAdmin], g => g.RoleId == "role-loyalty-admin"));
    }

    [Fact]
    public void Deactivating_the_shared_admin_role_locks_out_everyone()
    {
        Assert.True(LastAdminGuard.WouldLockOut([AliceAdmin, BobAdmin], g => g.RoleId == "role-admin"));
    }

    [Fact]
    public void Tenant_without_any_admin_grant_is_not_blocked()
    {
        Assert.False(LastAdminGuard.WouldLockOut([], _ => true));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Tenant Admin", true)]
    [InlineData("loyalty-admin", true)]
    [InlineData("Loyalty Operator", false)]
    public void Admin_role_name_rule_matches_login_claim(string name, bool expected)
    {
        Assert.Equal(expected, LastAdminGuard.IsAdminRoleName(name));
    }
}
