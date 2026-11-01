using System;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

/// <summary>
///     A minimal resource whose <see cref="ResourceAttribute" /> pins a per-resource paging
///     policy (default 2, maximum 3) that overrides the global resource options in tests.
/// </summary>
[CanonicalName("pagedThings/{pagedThing}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
[Resource(typeof(PagedThing), DefaultPageSize = 2, MaxPageSize = 3)]
public class PagedThing : IIdentifier, ICanonicalName, IConcurrency
{
    public string? Label { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion
}
