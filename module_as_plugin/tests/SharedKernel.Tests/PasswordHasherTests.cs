using SharedKernel;
using Xunit;

namespace SharedKernel.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_produces_self_describing_argon2id_and_verifies()
    {
        var stored = PasswordHasher.Hash("correct horse battery staple");

        Assert.StartsWith("argon2id$v=19$m=", stored);
        var (ok, needsRehash) = PasswordHasher.Verify("correct horse battery staple", stored);
        Assert.True(ok);
        Assert.False(needsRehash); // already current format
    }

    [Fact]
    public void Wrong_password_does_not_verify()
    {
        var stored = PasswordHasher.Hash("correct horse battery staple");

        Assert.False(PasswordHasher.Verify("wrong password", stored).Ok);
        Assert.False(PasswordHasher.Verify("", stored).Ok);
    }

    [Fact]
    public void Same_password_hashes_differently_each_time()
    {
        Assert.NotEqual(PasswordHasher.Hash("same-password"), PasswordHasher.Hash("same-password"));
    }

    [Fact]
    public void Legacy_sha256_format_verifies_and_requests_rehash()
    {
        var legacy = "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("old-pass"))).ToLowerInvariant();

        var (ok, needsRehash) = PasswordHasher.Verify("old-pass", legacy);
        Assert.True(ok);
        Assert.True(needsRehash); // transparent upgrade-on-login path
        Assert.False(PasswordHasher.Verify("other-pass", legacy).Ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev-stub")]           // old platform seed placeholder
    [InlineData("argon2:deadbeef")]    // old unverifiable placeholder format
    [InlineData("unknown-format")]
    public void Unverifiable_placeholders_never_verify(string? stored)
    {
        Assert.False(PasswordHasher.Verify("any-password", stored).Ok);
    }

    [Fact]
    public void Unverifiable_placeholders_are_flagged_for_dev_upgrade_only()
    {
        Assert.True(PasswordHasher.IsUnverifiablePlaceholder("dev-stub"));
        Assert.True(PasswordHasher.IsUnverifiablePlaceholder("argon2:deadbeef"));
        Assert.False(PasswordHasher.IsUnverifiablePlaceholder(PasswordHasher.Hash("x")));
        Assert.False(PasswordHasher.IsUnverifiablePlaceholder(null));
        Assert.False(PasswordHasher.IsUnverifiablePlaceholder("sha256:abc"));
    }

    [Fact]
    public void Corrupted_hash_fails_closed()
    {
        var stored = PasswordHasher.Hash("pw");
        var corrupted = stored[..^4] + "AAAA";

        Assert.False(PasswordHasher.Verify("pw", corrupted).Ok);
        Assert.False(PasswordHasher.Verify("pw", "argon2id$v=19$m=bad,t=x,p=y$c2FsdA==$aGFzaA==").Ok);
    }
}
