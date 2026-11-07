using Schemata.Core.Building;

namespace Schemata.Core.Linking;

public sealed record LinkContext(
    SemanticTarget? Target,
    object? Result,
    ResourceRegistration? Resource,
    ILinkRequest? Request = null);
