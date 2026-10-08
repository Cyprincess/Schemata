using Schemata.Push.Foundation.Builders;
using Schemata.Push.Http.Features;

namespace Microsoft.AspNetCore.Builder;

public static class PushHttpBuilderExtensions
{
    public static SchemataPushBuilder MapHttp(this SchemataPushBuilder builder) {
        builder.AddFeature<SchemataPushHttpFeature>();
        return builder;
    }
}
