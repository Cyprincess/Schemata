using System;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

[CanonicalName("students/{student}/notes/{note}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public class StudentNote : IIdentifier, ICanonicalName, IConcurrency
{
    public string? Text { get; set; }

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
