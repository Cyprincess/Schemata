namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A None event definition — carries no trigger or payload type.
///     Used for plain Start and End events that do not send or receive messages.
/// </summary>
public sealed class NoneDefinition : IEventDefinition
{
    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
