using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

public sealed class PreviewResourceRequest : ICanonicalName, IFreshness, ICommand<Student>, IRequestPrincipal
{
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    public string? EntityTag { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
