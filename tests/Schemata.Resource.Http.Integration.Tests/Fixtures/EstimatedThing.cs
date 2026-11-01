using System;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

[CanonicalName("estimatedThings/{estimatedThing}")]
[Resource(typeof(EstimatedThing), DefaultPageSize = 1, MaxPageSize = 1, TotalSize = TotalSizeMode.Estimated)]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public class EstimatedThing : IIdentifier, ICanonicalName, IConcurrency
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
