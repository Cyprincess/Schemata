using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core;

public class Services
{
    private readonly List<Action<IServiceCollection>> _actions = [];

    public void Add(Action<IServiceCollection> action) {
        _actions.Add(action);
    }

    internal IServiceCollection Invoke(IServiceCollection services) {
        foreach (var action in _actions) {
            action.Invoke(services);
        }

        _actions.Clear();

        return services;
    }
}
