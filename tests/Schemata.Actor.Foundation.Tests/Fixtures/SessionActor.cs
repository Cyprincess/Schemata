using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Actor.Skeleton.Entities;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     The host-defined persisted actor-state entity passed to <c>UseActor&lt;TActor&gt;</c>. Its
///     own table name keeps the EF Core model and the LINQ to DB repository contract identical.
/// </summary>
[Table("SchemataActorSessions")]
public sealed class SessionActor : SchemataActor;
