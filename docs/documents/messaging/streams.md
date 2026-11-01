# Scoped streams

`MapSchemataStream<TRequest, TItem>(pattern)` in `Schemata.Transport.Http` exposes a selected
`IStreamRequest<TItem>` handler over HTTP as newline-delimited JSON. The adapter resolves
`IStreamDispatcher.Stream(request, HttpContext.User, RequestAborted)`; registration is explicit per
endpoint and `RequireAuthorization()` composes with endpoint metadata.

Wire contract, one JSON value per physical line:

- `{"item":{…}}` for each produced item.
- `{"complete":true}` after the enumerator is disposed and every advisor `finally` has run.
- `{"error":{…}}` when output already started and the producer, validation, or disposal fails. The
  frame carries the same structured error body as the unary error envelope.

Framing details:

- The first item is awaited and serialized before the response starts, so validation and
  producer failures before any item return the unary structured error status instead of NDJSON.
- Frames serialize with a per-request clone of the host `JsonSerializerOptions` forcing
  `WriteIndented = false`; host indentation never splits one frame across lines.
- Each frame is buffered in one reusable `MemoryStream`, then written and flushed before the next
  `MoveNextAsync`, so a slow client backpressures the producer without buffering the sequence.
- Client cancellation aborts the connection; the enumerator and its execution scope are disposed.
- A host without `IStreamDispatcher` registered answers 501 before any output.

Sources: `src/Schemata.Transport.Http/Extensions/StreamEndpointExtensions.cs`,
`tests/Schemata.Flow.Integration.Tests/Resource/StreamHttpTransportShould.cs`.

# gRPC server streams

`AddSchemataGrpcStream<TRequest, TItem>(serviceName, methodName)` registers a server-streaming
method bound to the canonical `IStreamDispatcher`; `MapSchemataGrpcStream<TRequest, TItem>()` maps
the service. Registration is explicit: each call contributes one method, and all methods under the
same service name are merged into one reflection descriptor generated from the same
`RuntimeTypeModel` used by the marshallers (`protobuf-net.Reflection` parses the generated schema;
descriptor messages come from `Google.Protobuf`).

The stream registry owns the model used by each registered method. `GrpcSchema` composes its
methods by service name and parses the schema emitted by that model. Resource, Insight, and
Push contributors use the same composition helper with their executable models. Shared transport
installation adds one `ExceptionMappingInterceptor` across repeated domain registrations and
discovers generated proto-first descriptors through `BindServiceMethod` metadata.
Code-first descriptors use proto2 optional-field presence so reflected clients retain explicit
zero and false values in nullable slots. The wire tags and runtime marshallers remain the model's
own definitions. Imported well-known types and generated proto-first files retain their syntax.
The schema helper uses protobuf-net 3.2.56's field emitter for custom-serializer messages omitted
by its public schema generator; dependency upgrades must review that pinned internal signature.

Behavior:

- Items stream through `IServerStreamWriter<TItem>` with the call's cancellation token.
- A missing dispatcher or a dispatcher that throws `NotSupportedException` fails with
  `UNIMPLEMENTED` before any message is written.
- The shared `ExceptionMappingInterceptor` covers server-streaming calls: `SchemataException`
  maps to its structured status with `google.rpc.Status` trailers, caller cancellation maps to
  `CANCELLED`, and unhandled exceptions map to `INTERNAL`.
- `ValidationException` carries `ErrorReasons.ValidationFailed` on its `ErrorInfo` reason; the
  message template stays separately localizable.
- A producer failure before the first item writes zero messages. A failure after output starts
  ends the RPC with its error status and trailers; it cannot appear as successful completion.
- Cancellation and early client disposal release the stream's `MessageExecutionScope` after
  iterator cleanup. Each item write is awaited before the dispatcher requests the next item.

Sources: `src/Schemata.Transport.Grpc/StreamService.cs`,
`src/Schemata.Transport.Grpc/StreamRegistration.cs`,
`src/Schemata.Transport.Grpc/Extensions/StreamServiceExtensions.cs`,
`tests/Schemata.Transport.Grpc.Tests/StreamDescriptorContributorShould.cs`,
`tests/Schemata.Flow.Integration.Tests/Resource/StreamGrpcTransportShould.cs`.
