# Schemata DSL (Schemata Modeling Language, aka SKM)

A Roslyn source generator that compiles `.skm` schema files into C# entity classes, DTOs, mappings, and validation rules at build time.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cyprincess/Schemata.svg)](https://codecov.io/gh/Cyprincess/Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)

## Quick Start

```shell
dotnet add package --prerelease Schemata.Modeling.Generator
```

Add `.skm` files to your project with **Build Action: `AdditionalFiles`** (or use one of the business/module target packages that configure this automatically). The generator runs at compile time and produces C# files in `obj/`.

## Grammar Overview

### Namespace

Every `.skm` file begins with a namespace declaration:

```
Namespace My.Project
```

### Traits

Traits are reusable field groups. Entities compose them with `Use`:

```
Trait Identifier {
  long id [primary key]
}

Trait Timestamp {
  timestamp? create_time
  timestamp? update_time
}

Trait SoftDelete {
  timestamp? delete_time
  timestamp? purge_time
}

Trait Entity {
  Use Identifier, Timestamp
}
```

### Entities

Entities map to database tables. They generate a C# entity class, one C# class per `Object` block:

```
Entity Book {
  Use Entity, SoftDelete

  string title [not null] { Length: 500 }
  string? author
  string? isbn [unique]
  int page_count

  Object detail {
    id
    title
    author
    page_count
    create_time
    update_time
  }

  Object summary {
    id
    title
    author
  }
}
```

### Enums

```
Enum BookStatus {
  BookStatusUnspecified = 0
  Draft                 = 1
  Published             = 2
  Archived              = 3
}
```

## Field Types

| SKM Type | C# Type |
|----------|---------|
| `string` | `string` |
| `text` | `string` |
| `int` / `integer` / `int32` / `int4` | `int` |
| `long` / `int64` / `int8` | `long` |
| `biginteger` / `bigint` | `System.Numerics.BigInteger` |
| `float` | `float` |
| `double` | `double` |
| `decimal` | `decimal` |
| `boolean` | `bool` |
| `datetime` / `timestamp` | `DateTimeOffset` |
| `guid` | `Guid` |

Append `?` for nullable: `string? author`, `timestamp? delete_time`.

## Field Options

Declared in square brackets after the field name. Multiple options are comma-separated.

| Option | Effect |
|--------|--------|
| `primary key` | Primary key constraint |
| `auto increment` | Auto-incrementing primary key |
| `not null` | Non-nullable column constraint |
| `required` | Required field |
| `unique` | Unique index |
| `b tree` | B-tree index |
| `hash` | Hash index |

```
long   id    [primary key]
string email [unique, not null]
string? bio  [b tree]
```

## Field Properties

Declared in curly braces after options. Provide column-level metadata:

| Property | Effect |
|----------|--------|
| `Length: N` | Maximum string length |
| `Precision: N` | Decimal precision |
| `Default: value` | Default column value |
| `Algorithm: name` | Hash algorithm (for hashed fields) |

```
string  title [not null] { Length: 500 }
decimal price            { Precision: 2 }
```

## Object Blocks

`Object` blocks define DTO projections. Each block generates one or more C# Record types (isomers) depending on the fields declared.

### Field kinds

| Syntax | Description |
|--------|-------------|
| `field_name` | Always present in every generated variant |
| `field_name [omit]` | Excluded from the base variant; each `[omit]` field adds a dimension of optional inclusion |
| `field_name [omit all] { ... }` | Only the fields listed in `{ ... }` are kept, all others in the referenced type are omitted |
| `field_name = expression` | Computed field; value derived via the given expression |

### Isomer generation (`[omit]`)

Fields marked `[omit]` are excluded from the base Object. The generator produces additional Record types for every combination of included/excluded omit fields:

- 1 omit field → base + 1 isomer = 2 types
- n omit fields → base + C(1,n) + C(2,n) + … = 2ⁿ − 1 + 1 types total

Each isomer is instantiated via a static factory method named after the fields it includes — `WithEmailAddress()`, `WithPhoneNumber()`, `WithEmailAddressAndPhoneNumber()`, etc. Because Object types are Records, you can also build custom variants with the `with` keyword.

```
; User.response has 4 [omit] fields:
; email_address, obfuscated_email_address, phone_number, obfuscated_phone_number
; → 1 (base: id + nickname only)
;   + C(1,4) + C(2,4) + C(3,4) + C(4,4) = 4 + 6 + 4 + 1
; = 15 isomers  (comment in vector1.skm: "1 + C[1,4] + C[2,4] + C[3,4]")

Entity User {
  Use Entity

  string email_address  [b tree]
  string phone_number
  string password
  string nickname

  Object response {
    id
    nickname
    email_address             [omit] { Note 'omitted by default' }
    obfuscated_email_address  [omit] = obfuscate(email_address)
    phone_number              [omit]
    obfuscated_phone_number   [omit] = obfuscate(phone_number)
  }
}
```

### Embedded fields with `[omit all]`

When an field references another Object type, `[omit all]` creates an inline isomer of that type containing only the fields explicitly listed in the `{ ... }` block.

```
Entity Post {
  Use Entity

  long category_id

  Object request {
    ; Embeds only the `id` field from Category.response, omits everything else
    Category.response category [omit all] {
      id
    }
    category_id [omit] = category.id
    title
    body
  }

  Object response {
    ; category_id is mapped to the embedded Category.response.id
    Category.response category [omit all] {
      id = category_id
    }
    title
    body
  }
}
```

## See Also

- [Schemata.Business.Complex.Targets](https://nuget.org/packages/Schemata.Business.Complex.Targets) — includes DSL for business libraries
- [Schemata.Module.Complex.Targets](https://nuget.org/packages/Schemata.Module.Complex.Targets) — includes DSL for module libraries
