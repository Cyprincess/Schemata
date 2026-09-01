using System;
using System.Security.Claims;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests creation and initial execution of a registered process definition.</summary>
/// <param name="DefinitionName">Registered process definition name.</param>
/// <param name="Source">Loaded source entity, when the process starts from an entity.</param>
/// <param name="SourceType">CLR source type selected by the generic facade or resource loader.</param>
/// <param name="SourceCanonicalName">Canonical source name.</param>
/// <param name="Options">Process start options.</param>
/// <param name="Principal">Caller associated with the initial transition.</param>
public sealed record StartProcessRequest(
    string               DefinitionName,
    object?              Source,
    Type?                SourceType,
    string?              SourceCanonicalName,
    StartProcessOptions? Options,
    ClaimsPrincipal?     Principal
) : ICommand<SchemataProcess>;
