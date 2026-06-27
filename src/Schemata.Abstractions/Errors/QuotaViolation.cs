using System.Collections.Generic;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     A single quota limit exceeded by a particular subject, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso> and the
///     <c>QuotaFailure.Violation</c> message in
///     <see href="https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto">
///     google/rpc/error_details.proto</see>.
/// </summary>
public class QuotaViolation
{
    /// <summary>
    ///     Identifier of the entity that exceeded the quota
    ///     (e.g. <c>"client:192.168.1.1"</c>, <c>"project:my-project"</c>).
    /// </summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    ///     Human-readable description of the exceeded quota and amount.
    /// </summary>
    public virtual string? Description { get; set; }

    /// <summary>
    ///     API service that owns the violated quota when it lives on a dependency of the
    ///     called service (e.g. <c>"compute.googleapis.com"</c>).
    /// </summary>
    public virtual string? ApiService { get; set; }

    /// <summary>
    ///     Named counter that measures the violated quota
    ///     (e.g. <c>"compute.googleapis.com/cpus_per_vm_family"</c>).
    /// </summary>
    public virtual string? QuotaMetric { get; set; }

    /// <summary>
    ///     Unique limit identifier for the violated quota inside the originating API service
    ///     (e.g. <c>"CPUS-PER-VM-FAMILY-per-project-region"</c>).
    /// </summary>
    public virtual string? QuotaId { get; set; }

    /// <summary>
    ///     Per-dimension values defining the enforcement scope at the moment of the
    ///     violation (e.g. <c>{ "region": "us-central1", "vm_family": "n1" }</c>). Empty
    ///     for globally enforced quotas.
    /// </summary>
    public virtual Dictionary<string, string>? QuotaDimensions { get; set; }

    /// <summary>
    ///     Enforced quota value at the moment of the violation.
    /// </summary>
    public virtual long? QuotaValue { get; set; }

    /// <summary>
    ///     New quota value being rolled out at the moment of the violation; on completion
    ///     this value will be enforced in place of <see cref="QuotaValue" />.
    /// </summary>
    public virtual long? FutureQuotaValue { get; set; }
}
