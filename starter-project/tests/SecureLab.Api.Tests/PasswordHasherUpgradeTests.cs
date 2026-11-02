using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class PasswordHasherUpgradeTests
{
    [Fact]
    public void VerifyHashedPassword_OldParameters_ReturnsSuccessRehashNeeded()
    {
        var user = new ApplicationUser
        {
            UserName = "rehash-demo",
            DisplayName = "Rehash Demo"
        };

        const string password = "TestPassword123!";

        var oldHasher = new PasswordHasher<ApplicationUser>(
            Options.Create(new PasswordHasherOptions
            {
                IterationCount = 10_000
            }));

        var hash = oldHasher.HashPassword(user, password);

        var strongerHasher = new PasswordHasher<ApplicationUser>(
            Options.Create(new PasswordHasherOptions
            {
                IterationCount = 100_000
            }));

        var result = strongerHasher.VerifyHashedPassword(
            user, hash, password);

        Assert.Equal(
            PasswordVerificationResult.SuccessRehashNeeded,
            result);
    }
}