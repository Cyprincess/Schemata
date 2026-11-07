using System.Collections.Generic;
using Schemata.Core.Linking;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation.Linking;

public sealed class FlowLinkContributor<TProcess, TToken, TTransition> : ILinkContributor
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TTransition : SchemataProcessTransition
{
    public IEnumerable<SemanticLink> GetLinks(LinkContext context) {
        if (context.Target is null) yield break;
        if (context.Target is DomainOperationTarget { Operation: FlowConstants.LinkOperations.Definitions } operation)
            yield return new("self", operation);
        if (context.Resource?.Entity == typeof(TProcess) && context.Result is TProcess { DefinitionName: { Length: > 0 } name } process)
            yield return new("related", new DomainOperationTarget(FlowConstants.LinkOperations.Definitions,
                new Dictionary<string, string?> { ["definitionName"] = name, ["definitionVersion"] = process.DefinitionVersion }), "definitions");
        if (context.Resource?.Entity == typeof(TToken) && context.Result is TToken token) {
            if (token.Process is { Length: > 0 } owner)
                yield return new("related", new ResourceInstanceTarget(typeof(TProcess), owner), "process");
            if (token.Spawner is { Length: > 0 } spawner)
                yield return new("related", new ResourceInstanceTarget(typeof(TToken), spawner), "spawner");
        }
        if (context.Resource?.Entity == typeof(TTransition) && context.Result is TTransition transition) {
            if (transition.Process is { Length: > 0 } owner)
                yield return new("related", new ResourceInstanceTarget(typeof(TProcess), owner), "process");
            if (transition.Token is { Length: > 0 } tokenName)
                yield return new("related", new ResourceInstanceTarget(typeof(TToken), tokenName), "token");
        }
    }
}
