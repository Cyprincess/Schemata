using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace Schemata.Resource.Grpc.Tests.Unit;

/// <summary>
///     Minimal concrete <see cref="ServerCallContext" /> implementation for unit tests.
///     Stores an <see cref="HttpContext" /> in UserState so that the
///     <c>Grpc.AspNetCore.Server</c> extension method <c>GetHttpContext()</c> can read it.
/// </summary>
internal sealed class TestServerCallContext : ServerCallContext
{
    // The key used by Grpc.AspNetCore.Server's GetHttpContext() extension.
    private const string HttpContextKey = "__HttpContext";

    private readonly Dictionary<object, object> _userState = new();

    public TestServerCallContext(HttpContext? httpContext = null) {
        _userState[HttpContextKey] = httpContext ?? new DefaultHttpContext();
    }

    protected override string            MethodCore            => "TestMethod";
    protected override string            HostCore              => "localhost";
    protected override string            PeerCore              => "peer";
    protected override DateTime          DeadlineCore          => DateTime.MaxValue;
    protected override Metadata          RequestHeadersCore    => [];
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata          ResponseTrailersCore  => [];
    protected override Status            StatusCore            { get; set; } = Status.DefaultSuccess;
    protected override WriteOptions?     WriteOptionsCore      { get; set; }
    protected override AuthContext       AuthContextCore       => new("", []);

    protected override IDictionary<object, object> UserStateCore => _userState;

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) {
        throw new NotSupportedException();
    }

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) { return Task.CompletedTask; }
}
