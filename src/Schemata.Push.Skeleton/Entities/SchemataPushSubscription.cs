using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Push.Skeleton.Entities;

/// <summary>
///     Addressing record binding an owner to a transport delivery endpoint, modeled after
///     ASP.NET Core Identity <c>AspNetUserLogins</c>. A transport queries these rows to resolve
///     where to deliver a <see cref="RecipientTarget" />. The owner is a free-form canonical name
///     (<c>users/{x}</c>, <c>groups/{x}</c>, <c>tags/{x}</c>, …), so subscriptions are not bound to
///     any identity model.
/// </summary>
[DisplayName("PushSubscription")]
[Table("SchemataPushSubscriptions")]
[CanonicalName("pushSubscriptions/{push_subscription}")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(Owner), nameof(Provider), nameof(ProviderKey), IsUnique = true)]
public class SchemataPushSubscription : IIdentifier, ICanonicalName, IOwnable, IConcurrency, IDescriptive,
                                        ISoftDelete, ITimestamp
{
    /// <summary>The transport that owns this endpoint (e.g. <c>fcm</c>, <c>apns</c>, <c>webhook</c>).</summary>
    public virtual string? Provider { get; set; }

    /// <summary>The transport-specific endpoint identity (device token, URL, address).</summary>
    public virtual string? ProviderKey { get; set; }

    /// <summary>Transport-specific metadata serialized as JSON.</summary>
    public virtual Dictionary<string, string?>? Metadata { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    public virtual string? DisplayName { get; set; }

    public virtual Dictionary<string, string?>? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual Dictionary<string, string?>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual Guid Uid { get; set; }

    #endregion

    #region IOwnable Members

    [ResourceReference]
    public virtual string? Owner { get; set; }

    #endregion

    #region ISoftDelete Members

    public virtual DateTime? DeleteTime { get; set; }

    public virtual DateTime? PurgeTime { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
