using System.Text.Json;

namespace Schemata.Resource.Http;

public class ResourceJsonSerializerOptions(JsonSerializerOptions options)
{
    public JsonSerializerOptions Options { get; } = options;
}
