using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Modular;

namespace Schemata.Modular;

public class DefaultModulesProvider : IModulesProvider
{
    private static readonly List<ModuleDescriptor> Modules = [];

    public DefaultModulesProvider(ILogger<DefaultModulesProvider> logger) {
        if (Modules.Count != 0) {
            return;
        }

        var entry = Assembly.GetEntryAssembly();
        if (entry is null) {
            throw new InvalidOperationException("Entry assembly is null.");
        }

        var modules = entry.GetCustomAttributes<ModuleAttribute>();

        foreach (var module in modules) {
            var assembly = Assembly.Load(module.Name);

            var type = assembly.GetTypes()
                               .Where(t => typeof(IModule).IsAssignableFrom(t))
                               .FirstOrDefault(t => !t.IsAbstract);

            if (type is null) {
                continue;
            }

            var display     = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
            var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;
            var company     = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            var copyright   = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(version)) {
                version = assembly.GetCustomAttribute<AssemblyVersionAttribute>()?.Version;
            }

            version = GetVersion(version);

            Modules.Add(new(module.Name, assembly, type, typeof(DefaultModulesProvider), display, description, company, copyright, version));
        }
    }

    #region IModulesProvider Members

    public IEnumerable<ModuleDescriptor> GetModules() {
        return Modules;
    }

    #endregion

    private static string? GetVersion(string? version) {
        if (string.IsNullOrWhiteSpace(version)) {
            return null;
        }

        var index = version.IndexOf('+');
        if (index == -1) {
            return version;
        }

        var core  = version[..index];
        var build = version[(index + 1)..];

        build = build.Length switch {
            40    => build[..8],
            > 12  => build[..12],
            var _ => build,
        };

        return $"{core}+{build}";
    }
}
