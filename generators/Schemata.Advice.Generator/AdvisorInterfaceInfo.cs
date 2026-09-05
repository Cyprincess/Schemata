using System;
using System.Collections.Generic;
using System.Linq;

namespace Schemata.Advice.Generator;

internal sealed class AdvisorInterfaceInfo : IEquatable<AdvisorInterfaceInfo>
{
    public AdvisorInterfaceInfo(
        string interfaceFullyQualifiedName,
        string interfaceMinimalName,
        string constructedAdvisorType
    ) {
        InterfaceFullyQualifiedName = interfaceFullyQualifiedName;
        InterfaceMinimalName        = interfaceMinimalName;
        ConstructedAdvisorType      = constructedAdvisorType;
    }

    public string InterfaceFullyQualifiedName { get; }

    public string InterfaceMinimalName { get; }

    public string ConstructedAdvisorType { get; }

    public List<string> InterfaceTypeParameters  { get; } = [];
    public List<string> InterfaceTypeConstraints { get; } = [];
    public List<string> AdvisorTypeArguments     { get; } = [];
    public List<string> RunMethodParameters      { get; } = [];
    public List<string> RunMethodArguments       { get; } = [];

    #region IEquatable<AdvisorInterfaceInfo> Members

    public bool Equals(AdvisorInterfaceInfo? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return InterfaceFullyQualifiedName == other.InterfaceFullyQualifiedName
            && InterfaceMinimalName == other.InterfaceMinimalName
            && InterfaceTypeParameters.SequenceEqual(other.InterfaceTypeParameters)
            && InterfaceTypeConstraints.SequenceEqual(other.InterfaceTypeConstraints)
            && AdvisorTypeArguments.SequenceEqual(other.AdvisorTypeArguments)
            && RunMethodParameters.SequenceEqual(other.RunMethodParameters)
            && RunMethodArguments.SequenceEqual(other.RunMethodArguments)
            && ConstructedAdvisorType == other.ConstructedAdvisorType;
    }

    #endregion

    public override bool Equals(object? obj) { return obj is AdvisorInterfaceInfo other && Equals(other); }

    public override int GetHashCode() {
        unchecked {
            var hash = 17;
            hash = hash * 31 + InterfaceFullyQualifiedName.GetHashCode();
            hash = hash * 31 + InterfaceMinimalName.GetHashCode();
            hash = hash * 31 + ContentHash(InterfaceTypeParameters);
            hash = hash * 31 + ContentHash(InterfaceTypeConstraints);
            hash = hash * 31 + ContentHash(AdvisorTypeArguments);
            hash = hash * 31 + ContentHash(RunMethodParameters);
            hash = hash * 31 + ContentHash(RunMethodArguments);
            hash = hash * 31 + ConstructedAdvisorType.GetHashCode();
            return hash;
        }

        static int ContentHash(List<string> values) {
            var h = 19;
            foreach (var v in values) {
                h = h * 31 + StringComparer.Ordinal.GetHashCode(v);
            }
            return h;
        }
    }
}
