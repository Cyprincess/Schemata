using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>Test gate accepting the fixed initial access token for dynamic registration.</summary>
public sealed class TestInitialAccessTokenValidator : IInitialAccessTokenValidator
{
    public Task<bool> ValidateAsync(string? token, System.Threading.CancellationToken ct) {
        return Task.FromResult(token == "initial-token");
    }
}