using System;
using System.Collections.Generic;

namespace Schemata.Core.Linking;

public abstract record SemanticTarget(IReadOnlyDictionary<string, string?>? Parameters = null);

public sealed record ResourceInstanceTarget(
    Type? Entity, string CanonicalName, string? Verb = null,
    IReadOnlyDictionary<string, string?>? Parameters = null) : SemanticTarget(Parameters);

public sealed record ResourceCollectionTarget(
    Type? Entity, string? Parent = null, string? Filter = null,
    IReadOnlyDictionary<string, string?>? Parameters = null) : SemanticTarget(Parameters);

public sealed record DomainOperationTarget(
    string Operation, IReadOnlyDictionary<string, string?>? Parameters = null) : SemanticTarget(Parameters);
