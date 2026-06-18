# Schemata.Authorization.Integration.Tests

This project contains component-level endpoint tests for the authorization package. The suite uses `WebApplicationFactory` to exercise endpoint binding, JSON serialization, and the ASP.NET Core middleware pipeline while replacing authorization backing managers with mocks.

These tests do not validate persistence-backed OAuth/OpenID Connect integration behavior.
