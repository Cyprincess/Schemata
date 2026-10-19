using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>Payload persisted in a logout-confirmation interaction token.</summary>
public sealed record LogoutConfirmationPayload(EndSessionRequest Request, LogoutSessionTarget Target);