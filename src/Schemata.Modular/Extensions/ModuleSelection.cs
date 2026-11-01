using System;
using System.Collections.Generic;
using Schemata.Modular;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     The module runner a host committed: the runner type and instance identity, the provider
///     that produced the module set, and the snapshot of modules actually configured. Recorded
///     explicitly on the Schemata options so repeat registrations are decided by committed
///     state, not by scanning service descriptors or re-running discovery.
/// </summary>
internal sealed record ModuleSelection(Type Runner, object RunnerInstance, Type Provider, List<ModuleDescriptor> Modules);