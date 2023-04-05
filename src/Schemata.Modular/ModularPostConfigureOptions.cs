using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Schemata.Modular;

public class ModularPostConfigureOptions : IPostConfigureOptions<SchemataOptions>
{
    private readonly IServiceProvider _provider;

    public ModularPostConfigureOptions(IServiceProvider provider) {
        _provider = provider;
    }

    public void PostConfigure(string name, SchemataOptions options) {
        if (options.GetModules() is not null) {
            return;
        }

        var providers = _provider.GetRequiredService<IEnumerable<IModulesProvider>>();
        var modules   = providers.SelectMany(p => p.GetModules()).ToList();
        options.SetModules(modules);
    }
}
