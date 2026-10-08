using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

/// <summary>
///     Behavioral coverage for declared nested-value column conversions, per issue #137:
///     an explicitly declared <c>[Column]</c> member outside the automatic JSON eligibility
///     set binds and materializes losslessly, while the automatic set, NotMapped exclusions,
///     and seeded/raw values keep their contracts.
/// </summary>
public class NestedColumnShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    private static NestedThing Sample(Guid uid) {
        return new() {
            Uid       = uid,
            Name      = $"nestedThings/{uid:n}",
            Map       = new() {
                ["alpha"] = ["one", "two"],
                ["beta"]  = [],
                ["gamma"] = ["three"],
            },
            SimpleMap = new() { ["key"] = "value" },
            Tags      = ["a", "b"],
            Transient = ["never-persisted"],
        };
    }

    private static async Task<NestedThing> RoundTripAsync(IntegrationFixture fixture, NestedThing entity) {
        await using (var scope = fixture.ServiceProvider.CreateAsyncScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<NestedThing>>();
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        await using var read = fixture.ServiceProvider.CreateAsyncScope();
        var repository2 = read.ServiceProvider.GetRequiredService<IRepository<NestedThing>>();
        return await repository2.FirstOrDefaultAsync(q => q.Where(e => e.Uid == entity.Uid)) ?? throw new InvalidOperationException("seeded row missing");
    }

    private string RawMap(Guid uid) {
        using var scope     = _fixture.ServiceProvider.CreateScope();
        var       connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();
        return connection.Execute<string>($"SELECT \"Map\" FROM \"NestedThings\" WHERE \"Name\" = 'nestedThings/{uid:n}'") ?? string.Empty;
    }

    private void RawInsert(Guid uid, string map) {
        using var scope     = _fixture.ServiceProvider.CreateScope();
        var       connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();
        connection.Execute($"INSERT INTO \"NestedThings\" (\"Uid\", \"Name\", \"Map\") VALUES ('{uid:n}', 'nestedThings/{uid:n}', '{map.Replace("'", "''")}')");
    }

    private async Task<NestedThing?> LoadByNameAsync(Guid uid) {
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<NestedThing>>();
        return await repository.FirstOrDefaultAsync(
            q => q.Where(e => e.Name == $"nestedThings/{uid:n}"));
    }

    [Fact]
    public async Task DeclaredNestedDictionary_RoundTripsLosslessly_AcrossFreshConnections() {
        var uid    = Guid.NewGuid();
        var loaded = await RoundTripAsync(_fixture, Sample(uid));

        Assert.NotNull(loaded.Map);
        Assert.Equal(3, loaded.Map.Count);
        Assert.Equal(new() { "one", "two" }, loaded.Map["alpha"]);
        Assert.Empty(loaded.Map["beta"]);
        Assert.Equal(new() { "three" }, loaded.Map["gamma"]);

        // The persisted representation is provider JSON written by the framework converter; no
        // SetConverter is registered in this host, so the automatic-set controls below exercise
        // the automatic converter — the one actually invoked.
        var raw = RawMap(uid);
        Assert.Contains("alpha", raw);
        Assert.Contains("one", raw);

        Assert.Null(loaded.Transient);
    }

    [Fact]
    public async Task AutomaticSetControls_RetainTheirRepresentation() {
        var uid    = Guid.NewGuid();
        var loaded = await RoundTripAsync(_fixture, Sample(uid));

        Assert.Equal("value", loaded.SimpleMap?["key"]);
        Assert.Equal(new() { "a", "b" }, loaded.Tags);
    }

    [Fact]
    public async Task RawSeed_ValidJson_MaterializesThroughConfiguredConversion() {
        var uid = Guid.NewGuid();
        RawInsert(uid, "{\"seeded\":[\"x\",\"y\"]}");

        var loaded = await LoadByNameAsync(uid);

        Assert.NotNull(loaded?.Map);
        Assert.Equal(new() { "x", "y" }, loaded.Map["seeded"]);
    }

    [Fact]
    public async Task RawSeed_InvalidJson_FailsExplicitly_InsteadOfDisappearing() {
        var uid = Guid.NewGuid();
        RawInsert(uid, "not-json");

        var ex = await Record.ExceptionAsync(() => LoadByNameAsync(uid));

        Assert.IsType<System.Text.Json.JsonException>(ex);
    }
}
