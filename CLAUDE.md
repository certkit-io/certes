# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a fork of the [Certes](https://github.com/fszlin/certes) ACME client library for .NET, maintained as `CertKit.Certes` to avoid conflicts with the upstream package. It implements the ACME v2 protocol (RFC 8555) for automated certificate management (Let's Encrypt, etc.). Azure-specific code has been removed; keep it out.

## Build Commands

```bash
# Restore and build (use -m:1 to avoid strong-name signing race conditions)
dotnet restore Certes.sln
dotnet build Certes.sln -m:1

# Skip signing during local development
dotnet build Certes.sln -m:1 -p:SkipSigning=true
```

## Packaging

Use `build.ps1` to create NuGet packages with proper versioning:

```powershell
# Auto-increment build number from current version in certes.props
.\build.ps1 -Project ".\src\Certes\Certes.csproj" -BaseVersion "3.0.0" -Configuration Release

# Explicit version
.\build.ps1 -Project ".\src\Certes\Certes.csproj" -Version "3.0.0-certkit.2" -Configuration Release

# With signing (for release)
.\build.ps1 -Project ".\src\Certes\Certes.csproj" -Version "3.0.0-certkit.2" -Sign
```

The package version can also be set via the `CERTES_PACKAGE_VERSION` environment variable.

## Running Tests

```bash
# Unit tests
dotnet test test/Certes.Tests/Certes.Tests.csproj -m:1

# Integration tests (requires local Pebble ACME server at https://127.0.0.1:14000/dir)
dotnet test test/Certes.Tests.Integration/Certes.Tests.Integration.csproj -m:1

# Run a single test
dotnet test test/Certes.Tests/Certes.Tests.csproj -m:1 --filter "FullyQualifiedName~ClassName.MethodName"

# With code coverage
dotnet test test/Certes.Tests/Certes.Tests.csproj --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
```

Integration tests may reference stale external infrastructure (`certes-ci.dymetis.com`) and can fail for infrastructure reasons unrelated to code correctness. Prefer unit tests for validating new logic.

## Architecture

### Core Library (`src/Certes/`)

The library uses a **fluent context chain** pattern:

- **`AcmeContext`** (`IAcmeContext`) — main entry point. Manages account keys, HTTP transport, and ACME directory. All high-level operations start here (account creation, order placement, revocation, key change).
- **`AcmeHttpClient`** (`IAcmeHttpClient`) — HTTP transport layer handling nonces, JWS signing, badNonce retries (default: 5), POST-as-GET semantics, and User-Agent headers. Do not bypass this for ACME requests.
- **Context objects** (`AccountContext`, `OrderContext`, `AuthorizationContext`, `ChallengeContext`) all extend `EntityContext` and form a chainable API: `context.NewOrder(...) → order.Authorizations() → authz.Dns() → challenge.Validate()`

Key subsystems:
- `Crypto/` + `Jws/` — Key generation (ES256 default, RS256, PS256) and JWS signing via BouncyCastle
- `Extensions/` — Public consumer API surface (`AcmeContextExtensions`, `IKeyExtensions`, `CertificateChainExtensions`)
- `Pkcs/` — PKCS operations for certificate CSR and PFX generation
- `Json/` — ACME-specific JSON serialization with Newtonsoft.Json

### CLI Tool (`src/Certes.Cli/`)

- **Autofac** DI with assembly scanning auto-registers all `ICliCommand` implementations at startup (`Program.ConfigureContainer()`)
- Commands group into `CommandGroup` categories (Account, Order, Certificate, etc.)
- **System.CommandLine** (beta) handles argument parsing and help generation
- **NLog** for logging; enable debug output via `CERTES_DEBUG=true`

## Package Identity (Fork-Specific)

| Item | Value |
|------|-------|
| Library Package ID | `CertKit.Certes` |
| CLI Package ID | `dotnet-certkit-certes` |
| CLI command name | `certes-certkit` |

These differ from upstream `Certes` to prevent conflicts.

## Code Style

- `TreatWarningsAsErrors: true` — all warnings are errors; fix them, don't suppress
- `LangVersion: Latest` — use modern C# features
- 4-space indentation, Unix line endings, UTF-8 with BOM for `.cs` files
- Prefer `var`, expression-bodied properties, pattern matching, null-coalescing operators
- Block bodies (not expression bodies) for methods and constructors

## Key Files

- `misc/certes.props` — shared MSBuild properties including base version number
- `build.ps1` — packaging script with version management
- `src/Certes/AcmeContext.cs` — start here for ACME workflow changes
- `src/Certes/Acme/AcmeHttpClient.cs` — start here for HTTP/nonce/retry changes
- `src/Certes/Extensions/` — public consumer API extensions
