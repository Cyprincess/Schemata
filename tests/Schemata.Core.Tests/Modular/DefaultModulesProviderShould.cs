using System;
using System.Collections.Concurrent;
using System.Reflection;
using Schemata.Modular;
using Xunit;

namespace Schemata.Core.Tests.Modular;

public class DefaultModulesProviderShould
{
    [Fact]
    public void KeepDiscoveredModules_PerProviderInstance() {
        var first  = new DefaultModulesProvider(TimeProvider.System);
        var second = new DefaultModulesProvider(TimeProvider.System);

        var modules = GetModules(first);
        modules.Add(new("test.module", typeof(DefaultModulesProvider).Assembly, typeof(DefaultModulesProvider),
                        typeof(DefaultModulesProvider)));

        Assert.Contains(first.GetModules(), m => m.Name == "test.module");
        Assert.DoesNotContain(second.GetModules(), m => m.Name == "test.module");
    }

    private static ConcurrentBag<ModuleDescriptor> GetModules(DefaultModulesProvider provider) {
        var field = typeof(DefaultModulesProvider).GetField("_modules", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<ConcurrentBag<ModuleDescriptor>>(field!.GetValue(provider));
    }
}
