using Schemata.Core;
using Schemata.Modular;
using Xunit;

namespace Schemata.Modular.Tests;

public class ModuleSelectionShould
{
    [Fact]
    public void Expose_Module_Options_Extensions_In_The_Modular_Namespace() {
        Assert.Equal("Schemata.Modular.ModularOptionsExtensions", typeof(ModularOptionsExtensions).FullName);

        var schemata = new SchemataOptions();
        schemata.SetModules([
            new("test.module", typeof(ModuleSelectionShould).Assembly, typeof(ModuleSelectionShould), typeof(ModuleSelectionShould)),
        ]);

        var modules = schemata.GetModules();
        Assert.NotNull(modules);
        Assert.Equal("test.module", Assert.Single(modules).Name);
    }
}
