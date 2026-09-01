namespace Schemata.Flow.Foundation;

/// <summary>Marks a request that writes one existing process instance.</summary>
public interface IProcessScoped
{
    /// <summary>Canonical name used to serialize and reload the process inside its execution scope.</summary>
    string ProcessCanonicalName { get; }
}
