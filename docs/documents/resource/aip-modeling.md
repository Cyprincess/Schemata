# AIP Resource Modeling

**Audience:** This reference is for Schemata API designers who need to map a resource model to its actual HTTP JSON and gRPC protobuf contracts.

This document evaluates individual requirements from the listed [Google API Improvement Proposals](https://google.aip.dev/). A matching CLR type, route, or RPC name is evidence only after the complete path from registration through runtime transformation to each transport has been traced. It does not declare an entire AIP compliant.

## Status vocabulary

| Status | Meaning |
| --- | --- |
| Enforced | Schemata rejects or transforms the relevant case in its registered runtime path. |
| Supported by extension point | Schemata supplies a slot or hook; the application supplies the policy and implementation. |
| Application responsibility | Schemata serializes or persists the value without enforcing the AIP rule. |
| Partial | Some stated requirements have a traced implementation; other requirements remain absent. |
| Not implemented | The required model or runtime mechanism is absent from the registered resource path. |
| Not applicable | The requirement belongs to a protocol or construct that the named transport does not expose. |

The labels apply to the stated requirement, not to an AIP as a whole.

## Model boundaries

### Resource API schema and persistence schema

A resource API schema presents a stable public contract. A persistence entity stores the application data that repository advisors and an ORM operate on. They may be the same CLR type, but `ResourceAttribute` accepts four independently selected roles:

| Role | Purpose | Registered use |
| --- | --- | --- |
| **entity** | Persistence model and resource identity source. | Repository operations, name descriptor, standard handlers. |
| **request DTO** | Client input for Create and Update. | Request-advisor and mapping input; HTTP body and gRPC Create/Update request. |
| **detail DTO** | Complete single-resource output shape. | Get, Create, Update, and soft-delete response projection. |
| **summary DTO** | Per-item list output shape. | `ListResultBase<TSummary>.Entities`. |

`ResourceAttribute` defaults omitted roles to the entity, but that default is a convenience, not a model rule. The static detail/summary choice is set during registration. It is not an AIP-157 per-request field mask or view mechanism.

The resource runtime maps `TRequest` to `TEntity`, then maps entities to `TDetail` or `TSummary`. Single-resource results reach both transports as a bare detail DTO: HTTP unwraps the internal result wrapper before serialization, while gRPC registers `TDetail` itself as the response message. List results retain their list wrapper on both transports. See [Resource Overview](overview.md), [Entity Overview](../entity/overview.md), and [HTTP Transport](http-transport.md).

### Identity, parents, and references

`[CanonicalName("publishers/{publisher}/books/{book}")]` declares the source pattern for an addressable entity. `ResourceNameDescriptor` derives the collection, singular, plural, route path, parent segments, and gRPC service/RPC names from that pattern. At resource registration, an `ICanonicalName` entity must end with a placeholder preceded by a collection literal; the registration fails otherwise.

`ICanonicalName.Name` is Schemata's internal leaf identifier. It is suppressed on both wires. `ICanonicalName.CanonicalName` carries the collection-relative canonical resource name and is emitted as `name`. `IIdentifier.Uid` is the [AIP-148](https://google.aip.dev/148) `uid` field: `AdviceAddIdentifier` assigns it when an entity is added, request sanitizers clear it on writes, and both transports serialize it as the string field `uid` when an output DTO exposes it. `Identifiers.NewUid` returns UUID v4, meeting the AIP's UUID4 value requirement; the `UUID4` field-format annotation the AIP also requires is absent from both transports.

A child resource has one path-derived parent channel. `ResourceNameDescriptor` derives parent values from an HTTP route and `AdviceApplyChildParent` parses a DTO `IChild.Parent` into the entity's structural parent properties. Update clears all parent properties before the advisor path so a body cannot move an existing resource. Cross-resource values use `string` properties with `[ResourceReference]`; its type-resolvability validation is registered by `AddRepository`, while `ValidateExistence` requires the ownership pipeline. See [Resource Naming](resource-naming.md) and [Entity Traits](../entity/traits.md).

### Transport transformations

The following transformations are part of the public contract:

| Boundary | Transformation |
| --- | --- |
| Internal CLR | Traits use CLR names and types such as `CanonicalName`, `EntityTag`, `DateTime`, `Guid`, `Dictionary<string, string?>`, and `IStateful.State` as `string`. |
| Advisor and mapping | Create and Update sanitizers clear listed server-managed CLR fields before mapping; child-parent and response advisors supply parent/name-related values. |
| HTTP JSON | `SchemataJsonTraits` suppresses internal `Name`, maps `CanonicalName` to `name`, `EntityTag` to `etag`, and list `Entities` to the resource plural. Base JSON uses snake_case fields, kebab-case enum strings, null omission, and RFC 3339-compatible `DateTime` JSON text. |
| gRPC protobuf | `SchemataProtoModelConfigurator` applies the same resource aliases and snake_case to protobuf-net fields. Reflection descriptors expose CLR `DateTime` as `google.protobuf.Timestamp`, `Guid` as `string`, CLR enums as `int32` fields, and scalar-keyed dictionaries as proto3 maps. |
| Registration | `AddSchemataResources` registers the default advisors. `AddResource` validates addressability, registers the selected four role types, and each transport exposes only registered resources and allowed operations. |

A nullable `Dictionary<string, string?>` illustrates why an internal type is not a wire contract. JSON null omission can omit a null map property and preserves a JSON `null` value when a map is emitted. In gRPC, the configurator emits a proto3 map, where a null value is encoded as a key-only entry and a proto3 reader materializes it as an empty string. Treat nullable map values as transport-specific.

See [JSON Serialization](../core/json-serialization.md) for the HTTP settings and [gRPC Transport](grpc-transport.md) for gRPC service generation.

## Requirement matrix

The matrix gives the narrow, evidenced status for the AIP requirement summarized in each row. “Source path” identifies executable evidence; linked documents provide broader operational detail. The matrix covers resource-model AIPs: identity, hierarchy, association, fields, and values. Method, transport, and collection-lifecycle AIPs are assessed in [AIP interactions](aip-interactions.md); long-running work, authorization, and error AIPs in [AIP business logic](aip-business-logic.md).

| AIP / section | Requirement examined | Internal and runtime representation | HTTP status | gRPC status | Application obligation and gap | Source path |
| --- | --- | --- | --- | --- | --- | --- |
| [121 / Guidance](https://google.aip.dev/121) | Design resources before hierarchy, schema, and methods; keep a resource schema consistent across methods; use a stateless protocol and acyclic resource graph. | Four role types permit separate request/detail/summary shapes. Registration derives one canonical pattern and routes from an entity. | Partial | Partial | Choose nouns, hierarchy, associations, consistency, and the API schema independently from persistence. CRUD exposure alone does not establish the AIP’s consistency or graph rules. | `ResourceAttribute.cs`; `ResourceNameDescriptor.cs`; `ServiceCollectionExtensions.cs` |
| [122 / Resource names](https://google.aip.dev/122) | Expose canonical `string name`; model parents and references as resource-name strings; constrain collection and ID naming. | `[CanonicalName]` and `ICanonicalName` define a pattern, leaf `Name`, and canonical name. `ResourceReferenceAttribute` models references as strings. `AdviceAddCanonicalName` resolves the canonical name from the pattern on add; `AdviceAddUniqueness` rejects duplicate keys with `ALREADY_EXISTS`, including soft-deleted rows. | Partial | Partial | The alias path enforces the `name` wire field, validates a registered pattern's addressability, and fails duplicate inserts with `ALREADY_EXISTS` (optimistic: a concurrent race surfaces as the provider's constraint error). Character restrictions, collection-English rules, resource-reference annotations, and ID format policy remain application responsibility. | `CanonicalNameAttribute.cs`; `ICanonicalName.cs`; `ResourceNameDescriptor.cs`; `ResourceWireNameRules.cs`; `ResourceReferenceAttribute.cs`; `AdviceAddCanonicalName.cs`; `AdviceAddUniqueness.cs` |
| [123 / Resource types](https://google.aip.dev/123) | Define an API resource type, matching patterns, singular/plural metadata, and unique stable patterns. | For an addressable `[CanonicalName]` pattern, the descriptor derives singular/plural metadata. Accessors fail for non-addressable types, and Schemata has no `google.api.resource` metadata. | Partial | Partial | Keep service/type naming, multi-pattern compatibility, pattern-variable syntax, and resource annotations in the application contract. A resource registration has one addressable pattern rather than AIP resource-type annotations. | `CanonicalNameAttribute.cs`; `ResourceNameDescriptor.cs`; `FileDescriptorBridge.cs` |
| [124 / Resource association](https://google.aip.dev/124) | Give a resource at most one canonical parent; model other associations as references or repeated names. | Parent segments are derived from one canonical pattern. `IChild.Parent` is translated to structural parent values by a registered create/update advisor. | Partial | Partial | Design non-canonical associations as string references or repeated values and prevent cycles. The framework validates a supplied child parent against the pattern, but it does not prove a resource graph acyclic or choose multi-association filters. | `ResourceNameDescriptor.cs`; `AdviceApplyChildParent.cs`; `ResourceReferenceAttribute.cs` |
| [126 / Guidance](https://google.aip.dev/126) | Use enums for infrequently changing values; apply enum naming, zero-value, placement, and change guidance. | The modeling generator emits C# enums. JSON emits kebab-case enum strings; the gRPC descriptor bridge exposes CLR enum members as `int32` fields rather than declaring protobuf enum descriptors. | Partial | Partial | Define `UNSPECIFIED`, casing, scope, evolution policy, and domain meanings. A `string` trait field such as `IStateful.State` carries no enum semantics. | `EnumGenerator.cs`; `SchemataJsonSerializerFeature.cs`; `FileDescriptorBridge.cs`; `IStateful.cs` |
| [128 / Resources and reconciliation](https://google.aip.dev/128) | Declarative-friendly resources use strongly consistent lifecycle methods and, when needed, an output-only `reconciling` current-state indicator. | No resource style flag, `reconciling` trait, or reconciler registration exists in the resource path. | Not implemented | Not implemented | An application may add fields and reconciliation logic, then must implement their lifecycle and current-state semantics. Standard method routes do not confer declarative-friendly behavior. | `ResourceAttribute.cs`; `ServiceCollectionExtensions.cs`; `ResourceOperationHandler.Get.cs` |
| [129 / Single owner fields and effective values](https://google.aip.dev/129) | Assign one client/server owner per field; expose server-owned values as output-only; represent an effective value separately. | Create and Update sanitize wraps clear canonical name, UID, owner, state, timestamps, delete/purge times, concurrency token, and freshness tag before the handler maps the request. | Partial | Partial | Sanitization is wrap behavior, not serializer metadata. Define effective-value pairs, ownership documentation, normalization annotations, and normalization policy in the application. No generic `effective_` implementation exists. | `ResourceSanitizePipelineAdvisor.cs`; `ResourceWireNameRules.cs` |
| [140 / Field names](https://google.aip.dev/140) | Use lower_snake_case wire fields, singular/plural names, noun forms, and established terms. | The modeling utility turns SKM snake_case names into PascalCase CLR names. Resource aliases then pass through the transport snake_case policy. | Partial | Partial | Choose accurate English names, plural repeated fields, units, and nouns. The generator’s conversion and transport casing do not validate every AIP naming rule. | `Utilities.cs`; `SchemataJsonSerializerFeature.cs`; `SchemataProtoModelConfigurator.cs` |
| [141 / Quantities](https://google.aip.dev/141) | State units in names; use count suffixes and supported numeric types; define compound/inverse units clearly. | Fields retain their declared CLR numeric type; the modeling type map includes signed integers, floats, doubles, decimals, and `BigInteger`. | Application responsibility | Application responsibility | Select the unit-bearing field name, type, bounds, and conversion semantics. No advisor or serializer validates a quantity’s unit or signedness policy. | `Utilities.cs`; `Field.cs` |
| [142 / Time and duration](https://google.aip.dev/142) | Use common time components and `_time`/`_date`/`_offset` naming appropriate to the temporal meaning. | Built-in timestamp traits use `DateTime?`; the SKM `timestamp` token maps to `DateTimeOffset`. Repository timestamp advisors assign UTC `DateTime`. | Partial | Partial | HTTP emits `DateTime` in the JSON time representation. gRPC reflection describes CLR `DateTime` as `google.protobuf.Timestamp`; this descriptor mapping does not supply `Duration`, civil date/time, offset documentation, or semantic naming validation. | `ITimestamp.cs`; `AdviceAddTimestamp.cs`; `AdviceUpdateTimestamp.cs`; `Utilities.cs`; `FileDescriptorBridge.cs` |
| [143 / Standardized codes](https://google.aip.dev/143) | Use the applicable standard code, type, canonical output case, and documented standard. | Standard codes are ordinary CLR values and transport strings. | Application responsibility | Application responsibility | Select and validate IANA media types, CLDR regions, ISO-4217 currency, BCP-47 languages, and IANA time zones. `IDescriptive` localized map keys are data slots, not a BCP-47 validator. | `IDescriptive.cs`; `SchemataJsonTraits.cs`; `SchemataProtoModelConfigurator.cs` |
| [144 / Repeated fields](https://google.aip.dev/144) | Use plural repeated fields with a safe bound; use references instead of embedded associated resources; choose Update or specified Add/Remove methods. | `IEntitiesResult<T>.Entities` is a list and becomes the resource plural on each wire. gRPC descriptors identify supported CLR collections as repeated fields. | Partial | Partial | Set cardinality bounds and choose scalar, message, subresource, or reference shapes. Resource custom methods can expose verbs, but they do not implement AIP Add/Remove request, error, or body rules by themselves. | `IEntitiesResult.cs`; `ResourceWireNameRules.cs`; `FileDescriptorBridge.cs`; `ResourceMethodControllerConvention.cs` |
| [145 / Ranges](https://google.aip.dev/145) | Model ranges with same-type `start_`/`end_` fields, normally inclusive/exclusive, or use the specified interval type. | Schemata preserves the application’s selected fields and types. | Application responsibility | Application responsibility | Declare endpoints, interval semantics, and inclusive/exclusive behavior. Neither transport supplies `google.type.Interval` nor checks range ordering. | `Field.cs`; `SchemataJsonTraits.cs`; `SchemataProtoModelConfigurator.cs` |
| [146 / Generic fields](https://google.aip.dev/146) | Prefer the least generic representation; use oneof/maps/Struct/Any only for their appropriate cases. | Protobuf-net recognizes scalar-keyed dictionaries as maps. Other CLR properties are reflected as fields; resource metadata has no oneof or Any contract. | Partial | Partial | Design a discriminated union, structured schema, and unknown-key policy. Map support does not provide oneof selection, `Struct` schema validation, or `Any` type registration. | `SchemataProtoModelConfigurator.cs`; `FileDescriptorBridge.cs` |
| [147 / Sensitive fields](https://google.aip.dev/147) | Accept required secret material as input-only; represent optional presence with output-only `_set` or an intentional obfuscated value. | Request/detail separation can omit a secret from detail and summary DTOs. Serializers publish whatever the selected output DTO contains. | Supported by extension point | Supported by extension point | Put secret input only on the request DTO; omit it from outputs; implement storage, access control, `_set`/obfuscation semantics, and logging controls. No sensitivity trait or field-behavior annotation is generated. | `ResourceAttribute.cs`; `SchemataJsonTraits.cs`; `SchemataProtoModelConfigurator.cs` |
| [148 / Standard fields](https://google.aip.dev/148) | Model standard name, parent, display, timestamp, annotations, IP address, and UID fields with their specified behaviors. | Traits provide canonical names, `Guid Uid`, `DateTime` timestamps, expiration, soft-delete times, display name, and `Dictionary<string, string?>` annotations. Repository advisors stamp create/update times and assign `Uid` on add; sanitize wraps strip listed server-owned fields on writes. | Partial | Partial | `name` is traced to the wire. `Uid` is the AIP-148 `uid`: server-assigned, cleared on writes, serialized as string `uid` when an output DTO exposes it, and `Identifiers.NewUid` always returns UUID v4, meeting the value requirement. The `UUID4` field-format annotation is absent, so the annotation requirement is unmet. Annotation limits, namespaced keys, display-name limits, IP formats, expiry, and purge scheduling are not generic enforcement. | `ICanonicalName.cs`; `IIdentifier.cs`; `Identifiers.cs`; `AdviceAddIdentifier.cs`; `ITimestamp.cs`; `ISoftDelete.cs`; `IExpiration.cs`; `IAnnotatable.cs`; `IDescriptive.cs`; `ResourceSanitizePipelineAdvisor.cs`; `FileDescriptorBridge.cs` |
| [149 / Unset field values](https://google.aip.dev/149) | Use proto `optional` only when primitive presence must differ from its default value. | CLR nullable properties represent nullable CLR values. The code-first protobuf model unwraps nullable member types when configuring fields. | Not applicable | Partial | Define whether unset differs from a default and test that binary contract. Schemata does not generate source `.proto` declarations or apply the AIP’s selective `optional` rule; its reflection descriptors label singular fields optional. | `SchemataProtoModelConfigurator.cs`; `FileDescriptorBridge.cs` |
| [156 / Singleton resources](https://google.aip.dev/156) | A singleton has one parent-scoped static segment, no ID, no Create/Delete, and limited standard-method lifecycle. | A literal terminal segment has no addressable leaf placeholder. `AddResource` rejects an `ICanonicalName` resource that lacks an addressable pattern. | Not implemented | Not implemented | A static singleton name cannot enter Schemata’s registered resource pipeline. The `Operations` whitelist can remove Create/Delete from an ordinary resource, but it does not establish singleton naming, existence, parent lifecycle, or singleton List behavior. | `ResourceNameDescriptor.cs`; `ServiceCollectionExtensions.cs`; `ResourceAttribute.cs`; `ResourceControllerConvention.cs`; `ResourceServiceMethodProvider.cs` |
| [190 / Guidance](https://google.aip.dev/190) | Use consistent American-English API names and `VerbNoun` method names where applicable. | Resource naming derives singular/plural from the entity pattern. gRPC standard RPC names become `Get{Singular}`, `List{Plural}`, and related forms; custom RPCs combine the custom verb and singular. | Partial | Partial | Choose unambiguous terms, resource nouns, request/response names, and custom verbs. Schemata derives names mechanically but does not validate English, collisions, or semantic consistency. | `ResourceNameDescriptor.cs`; `GrpcResourceNaming.cs`; `ResourceMethodControllerConvention.cs` |
| [216 / States](https://google.aip.dev/216) | Use an output-only state enum and custom POST transition methods; reject invalid transitions with the specified error. | `IStateful.State` is a `string`. Create and Update sanitize wraps clear it as server-managed. Custom methods supply named instance or collection routes and dispatch to application handlers. | Partial | Partial | Define state enum/string vocabulary, transition preconditions, error mapping, and handler behavior. HTTP represents CLR enums as kebab-case strings while gRPC represents them numerically; the generic `string` state trait provides neither representation nor AIP state semantics. | `IStateful.cs`; `ResourceSanitizePipelineAdvisor.cs`; `ResourceMethodController.cs`; `ResourceMethodControllerConvention.cs`; `FileDescriptorBridge.cs` |

## Design rules

### Associations and hierarchy

1. Choose a single canonical parent before declaring the resource pattern. Put each alternate association in a named string reference or a repeated collection of canonical names.
2. Keep parent-child paths and reference graphs acyclic. Register an addressable pattern and use the same canonical string form in every reference.
3. Put a parent only in a DTO channel that the resource pipeline owns. The URI supplies Create parent values, and Update must retain its URI parent rather than a body-provided value.
4. Enable `ResourceReferenceAttribute.ValidateExistence` only when the application registers its corresponding validator and resolver. Existence validation does not replace authorization.

### Fields and values

1. Name public fields for their domain meaning, then verify the final snake_case HTTP and gRPC name. Give repeated fields plural nouns and quantity fields explicit units.
2. Use an enum only for a stable finite vocabulary. For a value that changes frequently, define a string format and its allowed values. JSON enum values are kebab-case strings; the generated gRPC reflection descriptor exposes CLR enums as numeric `int32` fields, not protobuf enum declarations.
3. Use a request DTO to receive required sensitive material and omit it from detail and summary DTOs. Model optional secret presence explicitly with a separately controlled output field.
4. Model an effective value as a client-owned input plus a separately named server-owned output. Do not overwrite client input in a mapper or serializer.
5. Store range endpoints in same-type fields, document the interval boundary convention, and enforce ordering in application validation.
6. Use `ExpireTime` only with a component that acts on it. `IExpiration` declares data; no built-in resource or repository advisor expires, hides, or removes an entity because that value is set.

### Server-owned values and state transitions

1. Treat the Resource sanitize wrap's server-managed field set as server-owned resource fields. The dispatcher clears them before the handler maps the request, rather than the serializer removing them.
2. Put an allowed lifecycle transition in one custom-method handler. The handler owns its preconditions, state update, persistence action, and domain error. Keep declarative field edits in Update.
3. Use an instance-scoped custom method when the transition targets one resource. Its HTTP route is `{collectionPath}/{name}:{verb}` and its gRPC name is `{Verb}{Singular}`. The application chooses the request and response DTO shapes.

## Failure cases to avoid

| Failure | Why it is wrong | Preferred design |
| --- | --- | --- |
| Treating `Uid` as the public resource name | `Uid` is the server-assigned AIP-148 `uid` UUID; public addressing uses `CanonicalName`, aliased to `name`. | Address and reference resources by `name`; expose `uid` when clients need the system-assigned UUID. |
| Reading a response DTO’s CLR properties as its contract | Traits and serializers hide and rename properties. | Inspect the HTTP JSON and gRPC descriptor paths before publishing a field name. |
| Calling a static detail/summary split partial response | Registration fixes the two shapes; the client cannot request a field mask or view. | Document it as endpoint-specific projection and add a real per-request mechanism only if required. |
| Relying on a field type to enforce a domain rule | `string` state, maps, ranges, codes, and expiration are data slots. | Add application validation and lifecycle logic at the owning business boundary. |
| Writing server values through a Create or Update body | Resource sanitizers clear listed server-owned values before mapping. | Set them in the appropriate repository or resource advisor, or return a separate effective output field. |
| Calling an ordinary resource a singleton | Registered resources require a trailing placeholder; a static terminal singleton pattern is rejected. | Use an application-specific endpoint or extend the resource system; do not claim AIP-156 support. |
| Assuming JSON and protobuf maps preserve nullable values identically | JSON and proto3 maps have different null behavior. | Avoid nullable map values when cross-transport equivalence matters, or document each wire behavior. |

## Source map

- `src/Schemata.Abstractions/Resource/ResourceAttribute.cs` defines the four resource type roles.
- `src/Schemata.Abstractions/Entities/CanonicalNameAttribute.cs`, `ICanonicalName.cs`, and `IChild.cs` define resource identity and parent DTO channels.
- `src/Schemata.Common/ResourceNameDescriptor.cs` derives patterns, parent predicates, route components, and registration-facing addressability.
- `src/Schemata.Common/ResourceWireNameRules.cs` owns the shared `name`, `etag`, and plural-list aliases.
- `src/Schemata.Resource.Foundation/Extensions/ServiceCollectionExtensions.cs` registers Resource handlers and wraps and rejects unaddressable resource patterns.
- `src/Schemata.Resource.Foundation/Advisors/ResourceSanitizePipelineAdvisor.cs` and `AdviceApplyChildParent.cs` implement request sanitization and parent application.
- `src/Schemata.Transport.Http/SchemataJsonTraits.cs` and `src/Schemata.Core/Features/SchemataJsonSerializerFeature.cs` define the HTTP JSON transformation.
- `src/Schemata.Transport.Grpc/Proto/SchemataProtoModelConfigurator.cs` and `src/Schemata.Resource.Grpc/FileDescriptorBridge.cs` define protobuf-net fields and reflection descriptors.
- `src/Schemata.Resource.Http/ResourceControllerConvention.cs`, `Internal/ResourceHttpConventionHelper.cs`, and `ResourceMethodControllerConvention.cs` build HTTP resource/custom-method routes.
- `src/Schemata.Resource.Grpc/Internal/GrpcResourceNaming.cs` and `ResourceServiceMethodProvider.cs` register gRPC service and method names.

## See also

- [Entity Overview](../entity/overview.md)
- [Entity Traits](../entity/traits.md)
- [Resource Naming](resource-naming.md)
- [JSON Serialization](../core/json-serialization.md)
- [Modeling Overview](../../modeling/overview.md)
- [Modeling Fields](../../modeling/fields.md)
- [Modeling Types](../../modeling/types.md)
- [Modeling Enums](../../modeling/enums.md)
