using System.Security.Cryptography;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Skeleton.Services;

/// <summary>A stored security row paired with its loaded, domain-neutral key material. Rows are
/// constructed exclusively through the Foundation extension <c>ToKeyMaterialAsync</c>, which owns
/// the full kind-to-material mapping and the private-key import dispatch.</summary>
/// <param name="Security">The stored row the material was loaded from.</param>
/// <param name="Material">The loaded material.</param>
/// <remarks>Asymmetric keys are freshly imported on every load; the caller owns the returned
/// <see cref="RSA" /> / <see cref="ECDsa" /> instances and must dispose them. No instances
/// are shared or cached.</remarks>
public sealed record SchemataKeyMaterial(SchemataSecurity Security, SecurityKeyMaterial Material);