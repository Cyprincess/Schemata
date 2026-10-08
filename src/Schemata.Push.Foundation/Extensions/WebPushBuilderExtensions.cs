using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Push.Foundation.Builders;
using Schemata.Push.Foundation.WebPush;
using Schemata.Push.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataPushBuilder" /> extensions installing the built-in Web Push provider.</summary>
public static class WebPushBuilderExtensions
{
    /// <summary>
    ///     Installs the Web Push delivery transport (RFC 8030 delivery, RFC 8291 message
    ///     encryption, RFC 8292 VAPID self-identification) into the enumerable
    ///     <see cref="Schemata.Push.Skeleton.IPushTransport" /> fan-out, composing with any other
    ///     registered transport. The transport keeps no delivery ledger or retry state; a failed
    ///     send is reported as <see cref="Schemata.Push.Skeleton.TransportStatus.Failed" /> and
    ///     redelivery is expressed by the application through the Push Scheduling bridge.
    /// </summary>
    /// <param name="builder">The push builder.</param>
    /// <param name="configure">Configures the VAPID keys, contact, and delivery defaults.</param>
    /// <returns>The push builder for chaining.</returns>
    public static SchemataPushBuilder AddWebPush(this SchemataPushBuilder builder, Action<WebPushOptions> configure) {
        builder.Services.Configure(configure);
        return builder.AddWebPush();
    }

    /// <summary>
    ///     Installs the Web Push delivery transport, drawing <see cref="WebPushOptions" /> from
    ///     options already configured on the service collection (e.g. configuration binding).
    /// </summary>
    /// <param name="builder">The push builder.</param>
    /// <returns>The push builder for chaining.</returns>
    public static SchemataPushBuilder AddWebPush(this SchemataPushBuilder builder) {
        builder.Services.AddHttpClient(PushConstants.WebPush.HttpClientName);
        builder.AddTransport<WebPushTransport>();
        return builder;
    }
}
