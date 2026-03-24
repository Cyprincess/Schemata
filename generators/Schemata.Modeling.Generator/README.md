# Schemata DSL (Schemata Modeling Language, aka SKM)

A Roslyn source generator that compiles `.skm` schema files into C# entity records, trait interfaces, and enums at build time.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cyprincess/Schemata.svg)](https://codecov.io/gh/Cyprincess/Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)

## Quick Start

```shell
dotnet add package --prerelease Schemata.Modeling.Generator
```

Add `.skm` files to your project with **Build Action: `AdditionalFiles`** (or use one of the business/module target packages that configure this automatically). The generator runs at compile time and produces C# files in `obj/`.

## What Gets Generated

For each `.skm` file the generator produces C# source files placed in `obj/` and automatically compiled into your assembly:

- **Entity record** — a C# `record` with all declared fields as properties.
- **Trait interfaces** — a C# `interface` per `Trait` block.
- **Enums** — a C# `enum` per `Enum` block.

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

Entities map to database tables. They generate a C# entity class and one C# record per `Object` block:

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

| SKM Type                             | C# Type                      |
| ------------------------------------ | ---------------------------- |
| `string`                             | `string`                     |
| `text`                               | `string`                     |
| `int` / `integer` / `int32` / `int4` | `int`                        |
| `long` / `int64` / `int8`            | `long`                       |
| `biginteger` / `bigint`              | `System.Numerics.BigInteger` |
| `float` / `float32` / `float4`       | `float`                      |
| `double` / `float64` / `float8`      | `double`                     |
| `decimal` / `numeric` / `number`     | `decimal`                    |
| `boolean` / `bool`                   | `bool`                       |
| `datetime` / `timestamp`             | `DateTimeOffset`             |
| `guid` / `uuid`                      | `Guid`                       |

Append `?` for nullable: `string? author`, `timestamp? delete_time`.

## Field Options

Declared in square brackets after the field name. Multiple options are comma-separated.

| Option                  | Effect                        |
| ----------------------- | ----------------------------- |
| `primary key`           | Primary key constraint        |
| `auto increment`        | Auto-incrementing primary key |
| `not null` / `required` | Non-nullable constraint       |
| `unique`                | Unique index                  |
| `b tree`                | B-tree index                  |
| `hash`                  | Hash index                    |

```
long   id    [primary key]
string email [unique, not null]
string? bio  [b tree]
```

## Field Properties

Declared in curly braces after options. Provide column-level metadata:

| Property         | Effect                             |
| ---------------- | ---------------------------------- |
| `Length N`       | Maximum string length              |
| `Precision N`    | Decimal precision                  |
| `Default value`  | Default column value               |
| `Algorithm name` | Hash algorithm (for hashed fields) |

```
string  title [not null] { Length 500 }
decimal price            { Precision 2 }
```

## See Also

- [Schemata.Business.Complex.Targets](https://nuget.org/packages/Schemata.Business.Complex.Targets) — includes DSL for business libraries
- [Schemata.Module.Complex.Targets](https://nuget.org/packages/Schemata.Module.Complex.Targets) — includes DSL for module libraries
