using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record RegisterEndpointQuery(RegisterRequest Request, string? BearerToken) : IQuery<RegistrationResponse>;
