using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Security.Foundation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Tests;

public class SecretVerifierShould
{
    [Fact]
    public async Task Hash_Then_Verify_With_The_Same_Presented_Password_Returns_True() {
        var verifier = NewVerifier();

        var hash = await verifier.HashAsync("s3cret");
        var row  = new SchemataSecurity { Kind = Kinds.Password, Value = hash };

        Assert.True(await verifier.VerifyAsync(row, "s3cret"));
    }

    [Fact]
    public async Task Verify_With_A_Wrong_Presented_Password_Returns_False() {
        var verifier = NewVerifier();

        var hash = await verifier.HashAsync("s3cret");
        var row  = new SchemataSecurity { Kind = Kinds.Password, Value = hash };

        Assert.False(await verifier.VerifyAsync(row, "wrong"));
    }

    [Fact]
    public async Task Hash_With_A_Keyed_Algorithm_Routes_To_Its_Hasher() {
        var verifier = NewVerifier(services => services.AddKeyedScoped<IPasswordHasher<SchemataSecurity>>(
            Algorithms.Bcrypt, (_, _) => new StubHasher("bcrypt-stamp")));

        var hash = await verifier.HashAsync("s3cret", Algorithms.Bcrypt);

        Assert.Equal("bcrypt-stamp:s3cret", hash);
    }

    [Fact]
    public async Task Verify_A_Row_Keyed_To_A_Registered_Hasher_Verifies_Through_It() {
        var verifier = NewVerifier(services => services.AddKeyedScoped<IPasswordHasher<SchemataSecurity>>(
            Algorithms.Bcrypt, (_, _) => new StubHasher("bcrypt-stamp")));

        var hit    = new SchemataSecurity { Kind = Kinds.Password, Algorithm = Algorithms.Bcrypt, Value = "bcrypt-stamp:s3cret" };
        var missed = new SchemataSecurity { Kind = Kinds.Password, Algorithm = Algorithms.Bcrypt, Value = "bcrypt-stamp:other" };

        Assert.True(await verifier.VerifyAsync(hit, "s3cret"));
        Assert.False(await verifier.VerifyAsync(missed, "s3cret"));
    }

    [Fact]
    public async Task An_Unregistered_Algorithm_Falls_Back_To_The_Default_Hasher() {
        var verifier = NewVerifier();

        var hash = await verifier.HashAsync("s3cret", Algorithms.Argon2Id);
        var row  = new SchemataSecurity { Kind = Kinds.Password, Algorithm = Algorithms.Argon2Id, Value = hash };

        Assert.True(await verifier.VerifyAsync(row, "s3cret"));
        Assert.False(await verifier.VerifyAsync(row, "wrong"));
    }

    [Fact]
    public async Task Verify_A_Row_Without_Presentation_Semantics_Throws_OutOfRange() {
        var verifier = NewVerifier();
        var row      = new SchemataSecurity { Kind = Kinds.Jwk, Value = "{}" };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => verifier.VerifyAsync(row, "{}"));
    }

    [Fact]
    public async Task Verify_With_Blank_Stored_Or_Blank_Presented_Material_Returns_False() {
        var verifier = NewVerifier();

        var blankStored     = new SchemataSecurity { Kind = Kinds.Password, Value = "" };
        var blankSecret     = new SchemataSecurity { Kind = Kinds.Secret,    Value = "" };
        var blankPresented  = new SchemataSecurity { Kind = Kinds.Password, Value = await verifier.HashAsync("s3cret") };

        Assert.False(await verifier.VerifyAsync(blankSecret, "plain"));
        Assert.False(await verifier.VerifyAsync(blankStored, "s3cret"));
        Assert.False(await verifier.VerifyAsync(blankPresented, ""));
    }

    [Fact]
    public async Task Verify_A_Plaintext_Secret_With_The_Matching_Presented_Value_Returns_True() {
        var verifier = NewVerifier();
        var row      = new SchemataSecurity { Kind = Kinds.Secret, Value = "plain" };

        Assert.True(await verifier.VerifyAsync(row, "plain"));
    }

    [Fact]
    public async Task Verify_A_Plaintext_Secret_With_A_Wrong_Presented_Value_Returns_False() {
        var verifier = NewVerifier();
        var row      = new SchemataSecurity { Kind = Kinds.Secret, Value = "plain" };

        Assert.False(await verifier.VerifyAsync(row, "wrong"));
    }

    private static SecretVerifier NewVerifier(Action<IServiceCollection>? setup = null) {
        // Mirrors the production registration: a plain default hasher plus keyed AnyKey
        // forwarding, so resolution falls through to the default for unregistered algorithms.
        var services = new ServiceCollection();
        services.TryAddScoped<IPasswordHasher<SchemataSecurity>, PasswordHasher<SchemataSecurity>>();
        services.AddKeyedScoped<IPasswordHasher<SchemataSecurity>>(
            KeyedService.AnyKey, (sp, _) => sp.GetRequiredService<IPasswordHasher<SchemataSecurity>>());
        setup?.Invoke(services);

        return new(services.BuildServiceProvider());
    }

    private sealed class StubHasher(string marker) : IPasswordHasher<SchemataSecurity>
    {
        public string HashPassword(SchemataSecurity user, string password) {
            return $"{marker}:{password}";
        }

        public PasswordVerificationResult VerifyHashedPassword(SchemataSecurity user, string hashedPassword, string providedPassword) {
            return hashedPassword == $"{marker}:{providedPassword}"
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }
    }
}
