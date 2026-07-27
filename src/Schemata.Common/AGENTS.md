# Schemata.Common

## OVERVIEW

19 files, ~2447 LOC. Sits one level above `Schemata.Abstractions` (its only Schemata dependency); everything above the kernel depends on it. Two clusters dominate: the AIP canonical-name kernel and the fluent error-detail factories. No DI wiring, no ASP.NET.

## STRUCTURE

### Canonical names and query shaping

- [ResourceNameDescriptor.cs](ResourceNameDescriptor.cs) (389 LOC) — the canonical-name kernel. Parses `[CanonicalName]` patterns; exposes `Singular` / `Plural` / `Collection` / `CollectionPath` / `Package` / `Pattern` / `HasParent` / `SupportsReadAcross`; parses canonical and parent paths; builds parent-segment `WHERE` predicates. Cached per `RuntimeTypeHandle`.
- [ResourceWireNameRules.cs](ResourceWireNameRules.cs) — `ResolveWireName(Type, propertyName)` / `ResolveClrName(Type, wireSegment)`. Owns the AIP-122 `name`, AIP-154 `etag` and AIP-132/231-235 collection-plural aliases. Consumed by the gRPC proto model configurator and the HTTP transport, so a change here moves the wire contract for both.
- [ResourceRequestContainer.cs](ResourceRequestContainer.cs) — composable `Func<IQueryable<T>, IQueryable<T>>` chain: `ApplyWhere` / `ApplyOrdering` / `ApplyPaginating`.
- [ResourceIdentifiers.cs](ResourceIdentifiers.cs) — applies AIP-122 leaf-name and AIP-159 parent-scope predicates onto the container; throws `ValidationException` carrying a localized message plus a reason.
- [ResourceQueryAdvice.cs](ResourceQueryAdvice.cs) — typed per-query advice entries; `ApplyTo(AdviceContext)` dispatches through reflection over `AdviceContext.Use<T>`.
- [IPagination.cs](IPagination.cs) + [PaginationExtensions.cs](PaginationExtensions.cs) — `Skip`/`PageSize` contract and `WithPaginating(pagination, lookahead)`.

### Expression helpers

- [Predicate.cs](Predicate.cs) — `True<T>` / `False<T>` / `Cast<T,TResult>` / `And` / `Or` / `Combine`, with an `ExpressionReplacer` for parameter rebinding.
- [Evaluator.cs](Evaluator.cs) — LINQ partial evaluator (`PartialEval`) that folds constants before query translation.
- [MemberAccess.cs](MemberAccess.cs) — resolves a wire segment to a `MemberExpression`. `Schemata.Expressions.Order` resolves AIP-132 order paths through it.

### Errors

- [Errors/SchemataResourceErrors.cs](Errors/SchemataResourceErrors.cs) (222 LOC) — `NotFound<T>` / `AlreadyExists<T>` / `PreconditionFailed<T>` / `PermissionDenied<T>` / `Aborted<T>` factories that pre-populate `ErrorInfoDetail` + `ResourceInfoDetail` and default the reason to the matching `SchemataConstants.ErrorReasons` entry.
- [Errors/SchemataErrorDetailExtensions.cs](Errors/SchemataErrorDetailExtensions.cs) — `.WithRetryAfter(TimeSpan)` and `.WithHelp(description, url)` decorate a `SchemataException` at the throw site.

### Hashing and misc

- [Hash/CityHash.cs](Hash/CityHash.cs) (812 LOC) — vendored CityHash32/64/128 (knuppe/cityhash, MIT). [Hash/StringExtensions.cs](Hash/StringExtensions.cs) — `ToCacheKey(key, domain)`. [Hash/UInt128.cs](Hash/UInt128.cs) — the 128-bit result struct.
- [SchemataJson.cs](SchemataJson.cs) — internal `JsonSerializerOptions` (case-insensitive, enum-as-string) for flow and job variable persistence.
- [Identifiers.cs](Identifiers.cs) — `NewUid()`: `Guid.CreateVersion7()` on net10, `Guid.NewGuid()` fallback.
- [AppDomainTypeCache.cs](AppDomainTypeCache.cs) (174 LOC) — process-wide assembly / type / property cache. [CustomAttributeExtensions.cs](CustomAttributeExtensions.cs) — `HasCustomAttribute<T>`.

## GOTCHAS

- `SchemataJson` is NOT the transport serialization policy. It is deliberately independent of the HTTP/gRPC naming policies (which are snake_case with wire-name aliases). Reusing it for a transport payload silently changes the wire shape.
- `WithRetryAfter` / `WithHelp` REPLACE an existing detail of the same kind rather than appending. Calling either twice keeps only the last value.
- CityHash is non-cryptographic. It backs cache keys only — never use it for signatures, tokens, or anything security-bearing.
- `ResourceNameDescriptor` caches per `RuntimeTypeHandle`, so attributes mutated at runtime after first resolution are invisible.
- `ResourceWireNameRules` is shared by both transports; changing an alias breaks HTTP and gRPC consumers simultaneously.

Canonical docs: `docs/documents/resource/resource-naming.md`, `docs/documents/core/error-model.md`.
