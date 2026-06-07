using Schemata.Abstractions.Resource;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Request body for starting a new process instance.</summary>
public sealed class StartProcessInstanceRequest : IRequestIdentification
{
    /// <summary>The <see cref="Models.ProcessDefinition.Name" /> of the definition to instantiate.</summary>
    public string DefinitionName { get; set; } = null!;

    /// <summary>Optional display name for the new process instance.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Optional description for the new process instance.</summary>
    public string? Description { get; set; }

    /// <summary>Optional serialized initial variables.</summary>
    public string? Variables { get; set; }

    #region IRequestIdentification Members

    public string? RequestId { get; set; }

    #endregion
}
