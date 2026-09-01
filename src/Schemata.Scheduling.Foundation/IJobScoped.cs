namespace Schemata.Scheduling.Foundation;

/// <summary>Marks a request that writes one existing job instance.</summary>
public interface IJobScoped
{
    /// <summary>Canonical name used to serialize and reload the job inside its execution scope.</summary>
    string JobCanonicalName { get; }
}
