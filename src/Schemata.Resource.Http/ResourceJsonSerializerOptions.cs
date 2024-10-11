using System.Text.Json;

namespace Schemata.Resource.Http;

public class ResourceJsonSerializerOptions
{
    public ResourceJsonSerializerOptions(JsonSerializerOptions options) {
        Options = options;
    }

    public JsonSerializerOptions Options { get; }
}
