namespace Schemata.Abstractions.Errors;

/// <summary>
///     Marker interface for typed error details carried as <c>google.protobuf.Any</c> entries
///     inside an <see cref="ErrorBody" />, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
public interface IErrorDetail;
