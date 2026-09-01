using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Integration.Tests.Resource.Fixtures;

public sealed class PreviewResourceRequest : ICanonicalName, ICommand<Student>, IRequestPrincipal
{
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
