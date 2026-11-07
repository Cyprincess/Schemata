using System.Collections.Generic;

namespace Schemata.Transport.Grpc.Linking;

public sealed record GrpcLinkTarget(string Method, IReadOnlyDictionary<string, string?> Inputs);
