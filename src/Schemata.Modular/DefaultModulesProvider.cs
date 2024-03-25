using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Schemata.Abstractions.Modular;

namespace Schemata.Modular;

public class DefaultModulesProvider : IModulesProvider
{
    private static readonly List<ModuleInfo> Modules = [];

    public DefaultModulesProvider() {
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

            var display     = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
            var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;

            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(version)) {
                version = assembly.GetCustomAttribute<AssemblyVersionAttribute>()?.Version;
            }

            version = GetVersion(version);

            Modules.Add(new ModuleInfo( //
                module.Name,            //
                assembly, type,         //
                display,                //
                description,            //
                version                 //
            ));
        }
    }

    #region IModulesProvider Members

    public IEnumerable<ModuleInfo> GetModules() {
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

        if (build.Length > 12) {
            build = build[..12];
        }

        return $"{core}+{build}";
    }
}
