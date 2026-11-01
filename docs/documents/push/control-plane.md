Push control-plane contracts over HTTP and gRPC: endpoints are explicit and authenticated; verbs require conventional policies.

## Endpoints

| Method & path | Policy | Behavior |
|---|---|---|
| `POST /v1/push/subscriptions` | `push.subscriptions.create` | Creates the calling owner's subscription; identical `(owner, provider, providerKey)` returns the existing row. 201 + `PushSubscriptionInfo`. |
| `GET /v1/push/subscriptions?provider=` | `push.subscriptions.get` | Lists only the calling owner's subscriptions. |
| `DELETE /v1/push/subscriptions` | `push.subscriptions.delete` | Deletes the calling owner's `(provider, providerKey)` address; 204 regardless of prior existence. |
| `POST /v1/push/subscriptions:send` | `push.send` | Operator capability: fans one dispatch through every registered `IPushTransport` and returns each outcome, including partial failures. |

In a Schemata host, enable the controller with `schema.UsePush().MapHttp()` inside
`builder.UseSchemata(...)`. Configure authentication and the four policies through
`schema.UseAuthentication(...)`. The feature installs the shared HTTP transport and explicitly
registers the controller application part.

A plain MVC host calls `services.AddSchemataPush(); services.AddSchemataPushHttp();`, configures
authentication and the same policies, installs `app.UseSchemataExceptionHandler()`,
`app.UseAuthentication()`, and `app.UseAuthorization()`, then calls `app.MapControllers()`.
Both entry points use the same registration function. Repeated registration and registration
before or after Schemata preserve the controller. HTTP property and dictionary names retain
Schemata's snake_case convention; enums use kebab-case strings.
`AddSchemataPush()` installs `AddAuthorizationCore()` for the shared handler's policy evaluation.
Hosts still define the four action policies; installing Push grants no control-plane permission.

## Ownership and secrets

The shared `PushControlHandler` authorizes Create/List/Delete/Send through
`IAuthorizationService.AuthorizeAsync(principal, policyName)` and the names in `PushPolicies`.
Subscription actions then resolve the owner from the trusted principal through `IPushOwnerResolver`
(default: the first authenticated identity's `sub`, then name-identifier claim). Required provider
and provider-key fields are checked after authorization and owner resolution.

Local management calls dispatch the same control requests from `Schemata.Push.Skeleton.Control`.
Transports attach their authenticated principal; the principal is excluded from JSON serialization.
Request bodies carry no owner. List filters by the resolved owner, and Delete uses that owner plus
the supplied provider/address. Replace `IPushOwnerResolver` to map application identities onto
canonical owner names.

`provider_key` (device token, URL, address) and `metadata` are input-only. The shared
`PushSubscriptionInfo` result contains uid, canonical name, provider, and create/update times.

## Send

The shared send request carries `message` as any JSON value, a `target` (`kind`: `channel`,
`recipient`, `topic`, `broadcast`, `custom`), nullable `options`, and transport `metadata`.
An absent target becomes broadcast. Unknown kinds and empty fields of the selected target are
rejected before fan-out. An absent message is rejected; explicit JSON null is preserved.
Objects retain their nesting, so `data.title` cannot overwrite a top-level `title`.

Missing options or priority use `Normal`; an explicit `Low` remains `Low`. Lifetime is a nullable
`TimeSpan`, preserving signed and fractional values. Each request dispatches one `SendPushRequest`.
Its result contains every transport outcome in completion order, including failures and skips;
a failed transport leaves the other outcomes visible.

## gRPC surface

`Schemata.Push.Grpc` exposes `schemata.push.v1.PushControl` (`IPushControlService`):
`Create`, `List`, `Delete`, and `Send` adapt to the shared control requests and dispatch through
`IRequestDispatcher`. The service takes its principal from the gRPC server's HTTP context.
Register with `services.AddSchemataPushGrpc()` and `app.MapSchemataPushGrpc()`; responses use
the shared `PushGrpcModel`, with credential-free `PushSubscriptionView` and per-transport outcomes.

`AddSchemataPushGrpc()` installs the shared gRPC transport and registers
`PushGrpcServiceDescriptorContributor`. Reflection describes the actual Create/List/Delete/Send
method definitions from `PushGrpcModel`, including JSON text, priority presence, and the
`google.protobuf.Duration` field. The same model backs server marshallers and descriptor generation.
Repeated transport installation retains one exception interceptor; proto-first services retain
their generated descriptor discovery.

`SendPushCommand.MessageJson` carries JSON text. Missing text is rejected by the shared handler;
malformed JSON receives structured `INVALID_ARGUMENT` during binding. The parsed value is cloned
before its `JsonDocument` is disposed. `TargetKind.Unspecified` means an absent target; unknown
numeric discriminators reach the shared handler as invalid kinds. Selected target fields are
validated there. `Options` is a nullable `SendPushOptions` message with nullable `Priority`.

`Options.TimeToLive` remains `TimeSpan?`. The Level300 schema uses `google.protobuf.Duration`
field shape (signed int64 seconds and signed int32 nanos). The options serializer preserves
every `TimeSpan` tick, including MinValue, MaxValue, zero, negative, and fractional values.
It rejects nanos outside ±999,999,999, conflicting seconds/nanos signs, sub-100ns precision,
and values outside the `TimeSpan` range before delivery. This range is wider than the standard
Google Duration ±10,000-year range; this contract specifies the full CLR `TimeSpan` range,
so clients using stricter well-known-type validators need to limit their values accordingly.
The custom serializer reads original fields because protobuf-net's built-in duration conversion
can truncate sub-tick nanos. HTTP uses the standard JSON `TimeSpan` string representation.
Repeated protobuf Duration submessages merge their raw fields before tick validation. Later
encoded scalar fields replace earlier values, including explicit zero; absent fields preserve
their preceding values. Repeated options messages preserve this same Duration state until binding
finishes. See the protobuf [embedded-message merge rules](https://protobuf.dev/programming-guides/encoding/#last-one-wins).

Sources for duration conversion: protobuf-net 3.2.56
[`WellKnownTypes/Duration.cs`](https://github.com/protobuf-net/protobuf-net/blob/3.2.56/src/protobuf-net.Core/WellKnownTypes/Duration.cs)
and `src/Schemata.Push.Grpc/PushOptionsSerializer.cs`.
The standard Duration field limits are specified in
[`google/protobuf/duration.proto`](https://github.com/protocolbuffers/protobuf/blob/main/src/google/protobuf/duration.proto).

Sources: `src/Schemata.Push.Http/Controllers/PushController.cs`,
`src/Schemata.Push.Http/Extensions/PushHttpServiceCollectionExtensions.cs`,
`src/Schemata.Push.Grpc/PushControlGrpcService.cs`,
`src/Schemata.Push.Grpc/Extensions/PushGrpcServiceCollectionExtensions.cs`,
`src/Schemata.Push.Skeleton/Models/PushSubscriptionModels.cs`,
`src/Schemata.Push.Skeleton/IPushOwnerResolver.cs`,
`tests/Schemata.Push.Http.Integration.Tests/PushControlShould.cs`,
`tests/Schemata.Push.Grpc.Integration.Tests/PushControlGrpcShould.cs`.
