# Schemata DSL (Schemata Modeling Language, aka SKM)

Writing Schemata DSL for business entities, and generate database models, view models, mapping, validation, and so on.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cyprincess/Schemata.svg)](https://codecov.io/gh/Cyprincess/Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)

## Quick Start

```shell
dotnet add package --prerelease Schemata.DSL
```

## Basic Grammar

```csharp
Namespace Example

Trait Identifier {
  long id [primary key]
}

Trait Timestamp {
  timestamp? creation_date
  timestamp? modification_date
}

Trait Entity {
  Use Identifier, Timestamp
}

Entity User {
  Use Entity

  string email_address [b tree]
  string phone_number [b tree]
  string password

  string nickname

  Object response {
    id
    nickname
    email_address [omit]
    obfuscated_email_address [omit] = obfuscate(email_address)
    phone_number [omit]
    obfuscated_phone_number [omit] = obfuscate(phone_number)
  }
}
```
