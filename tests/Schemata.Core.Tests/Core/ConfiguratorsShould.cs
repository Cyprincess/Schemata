using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Schemata.Core.Tests.Core;

public class ConfiguratorsShould
{
    [Fact]
    public void Set_Get_RoundTrip() {
        var configurators = new Configurators();
        var called        = false;

        configurators.Set<string>(s => called = true);
        var action = configurators.Get<string>();
        action("test");

        Assert.True(called);
    }

    [Fact]
    public void Set_Chain_MultipleActions() {
        var configurators = new Configurators();
        var calls         = new List<int>();

        configurators.Set<string>(_ => calls.Add(1));
        configurators.Set<string>(_ => calls.Add(2));

        var action = configurators.Get<string>();
        action("test");

        Assert.Equal([1, 2], calls);
    }

    [Fact]
    public void Set_TwoType_RoundTrip() {
        var configurators = new Configurators();
        var captured      = "";

        configurators.Set<string, int>((s, i) => captured = $"{s}-{i}");
        var action = configurators.Get<string, int>();
        action("hello", 42);

        Assert.Equal("hello-42", captured);
    }

    [Fact]
    public void TryGet_Missing_ReturnFalse() {
        var configurators = new Configurators();

        var found = configurators.TryGet<string>(out var action);

        Assert.False(found);
        Assert.Null(action);
    }

    [Fact]
    public void Get_Missing_ThrowKeyNotFoundException() {
        var configurators = new Configurators();

        Assert.Throws<KeyNotFoundException>(() => configurators.Get<string>());
    }

    [Fact]
    public void Pop_RemoveAndReturn() {
        var configurators = new Configurators();
        configurators.Set<string>(_ => { });

        var action = configurators.Pop<string>();

        Assert.NotNull(action);
        Assert.False(configurators.TryGet<string>(out var _));
    }

    [Fact]
    public void PopOrDefault_Missing_ReturnNoOp() {
        var configurators = new Configurators();

        var action = configurators.PopOrDefault<string>();

        Assert.NotNull(action);
        // Should not throw when invoked
        action("test");
    }

    [Fact]
    public void PopOrDefault_TwoType_Missing_ReturnNoOp() {
        var configurators = new Configurators();

        var action = configurators.PopOrDefault<string, int>();

        Assert.NotNull(action);
        // Should not throw when invoked
        action("test", 42);
    }

    [Fact]
    public void Invoke_RegisterAsConfigureOptions() {
        var configurators = new Configurators();
        var called        = false;

        configurators.Set<TestOptions>(opts => {
            opts.Value = "configured";
            called     = true;
        });

        var services = new ServiceCollection();
        configurators.Invoke(services);

        var provider = services.BuildServiceProvider();
        var options  = provider.GetRequiredService<IOptions<TestOptions>>();

        Assert.Equal("configured", options.Value.Value);
        Assert.True(called);
    }

    #region Nested type: TestOptions

    private sealed class TestOptions
    {
        public string? Value { get; set; }
    }

    #endregion
}
