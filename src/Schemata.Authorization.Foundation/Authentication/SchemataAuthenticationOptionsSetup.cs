using System;
using Microsoft.Extensions.Options;

namespace Schemata.Authorization.Foundation.Authentication;

internal sealed class SchemataAuthenticationOptionsSetup(TimeProvider time)
    : IPostConfigureOptions<SchemataAuthenticationHandlerOptions>, IValidateOptions<SchemataAuthenticationHandlerOptions>
{
    public void PostConfigure(string? name, SchemataAuthenticationHandlerOptions options) {
        options.TimeProvider ??= time;
    }

    public ValidateOptionsResult Validate(string? name, SchemataAuthenticationHandlerOptions options) {
        options.Validate(name ?? Options.DefaultName);
        return ValidateOptionsResult.Success;
    }
}
