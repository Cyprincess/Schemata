using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Security.Foundation.Signatures;

namespace Schemata.Security.Foundation.Extensions;

/// <summary>Registers the RFC 9421 message signature capability.</summary>
public static class SignatureServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="HttpMessageSigner" /> and <see cref="HttpMessageSignatureVerifier" />
    ///     as the transport-level message signature capability shared by the HTTP and gRPC
    ///     transports. Verification requirements (RFC 9421 section 3.2.1) are configured once
    ///     through <paramref name="configure" />; a host-supplied <see cref="TimeProvider" /> is
    ///     honored when registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the verification requirements.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHttpMessageSignatures(
        this IServiceCollection                          services,
        Action<HttpMessageSignatureVerificationOptions>? configure = null
    ) {
        if (configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton(sp => new HttpMessageSigner(sp.GetService<TimeProvider>()));
        services.TryAddSingleton(sp => new HttpMessageSignatureVerifier(
            sp.GetService<TimeProvider>(),
            sp.GetService<IOptions<HttpMessageSignatureVerificationOptions>>()?.Value
        ));

        return services;
    }
}
