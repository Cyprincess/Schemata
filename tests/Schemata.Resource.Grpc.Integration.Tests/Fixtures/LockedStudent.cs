using System;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

/// <summary>
///     Scheme-protected resource covering the issue #34 matrix: the whitelist keeps only
///     List/Get/Update, Get and the announce custom verb are anonymous, and seal stays protected.
/// </summary>
[CanonicalName("lockedStudents/{locked_student}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
[Resource(typeof(LockedStudent), Operations = [Operations.List, Operations.Get, Operations.Update], AuthenticationScheme = TestAuthHandler.TestAuthScheme)]
[Anonymous(nameof(Operations.Get), "announce")]
[ResourceMethod("announce", typeof(LockedAnnounceHandler), Method = ResourceHttpMethod.Post)]
[ResourceMethod("seal", typeof(LockedSealHandler), Method = ResourceHttpMethod.Post)]
public class LockedStudent : IIdentifier, ICanonicalName, IConcurrency, IFreshness, IValidation, IUpdateMask
{
    public string? FullName { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region IUpdateMask Members

    public string? UpdateMask { get; set; }

    #endregion

    #region IValidation Members

    public bool ValidateOnly { get; set; }

    #endregion
}