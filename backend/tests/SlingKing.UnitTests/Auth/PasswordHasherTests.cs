using Shouldly;
using SlingKing.Infrastructure.Auth;
using Xunit;

namespace SlingKing.UnitTests.Auth;

public class PasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void A_hash_never_contains_the_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        hash.ShouldNotContain("correct horse");
        hash.ShouldStartWith("$2");
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        _hasher.Hash("same").ShouldNotBe(_hasher.Hash("same"));
    }

    [Fact]
    public void Verify_accepts_the_right_password()
    {
        var hash = _hasher.Hash("s3cret-value");

        _hasher.Verify("s3cret-value", hash).ShouldBeTrue();
    }

    [Fact]
    public void Verify_rejects_the_wrong_password()
    {
        var hash = _hasher.Hash("s3cret-value");

        _hasher.Verify("s3cret-valuf", hash).ShouldBeFalse();
    }

    [Fact]
    public void Verify_returns_false_rather_than_throwing_on_a_malformed_hash()
    {
        _hasher.Verify("anything", "not-a-bcrypt-hash").ShouldBeFalse();
    }
}
