using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Integration.Tests.Fixtures;

/// <summary>
///     Stand-in subject resource so this host resolves <c>users/...</c> canonical names in
///     <c>[ResourceReference]</c> token fields without hosting the Identity stack.
/// </summary>
[Table("TestSubjects")]
[CanonicalName("users/{subject}")]
[PrimaryKey(nameof(Uid))]
public class TestSubject : IIdentifier, ICanonicalName, ITimestamp
{
    #region IIdentifier Members

    public virtual Guid Uid { get; set; }

    #endregion

    #region ICanonicalName Members

    public virtual string? Name          { get; set; }
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
