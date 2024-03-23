using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace Schemata.Modular;

public class ModularApplicationPart : ApplicationPart, IApplicationPartTypeProvider, IActionDescriptorChangeProvider
{
    private readonly HashSet<TypeInfo>       _types = [];
    private          CancellationTokenSource _cts   = new();

    public override string Name => "Schemata.Modular";

    #region IActionDescriptorChangeProvider Members

    public IChangeToken GetChangeToken() {
        return new CancellationChangeToken(_cts.Token);
    }

    #endregion

    #region IApplicationPartTypeProvider Members

    public IEnumerable<TypeInfo> Types => _types;

    #endregion

    public ModularApplicationPart AddAssembly(Assembly assembly) {
        foreach (var type in assembly.DefinedTypes) {
            _types.Add(type);
        }

        return this;
    }

    public ModularApplicationPart AddType(TypeInfo type) {
        _types.Add(type);

        return this;
    }

    public ModularApplicationPart Commit() {
        var old = _cts;
        _cts = new CancellationTokenSource();
        old.Cancel();

        return this;
    }
}
