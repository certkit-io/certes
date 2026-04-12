# Certes Agent Guide

## Purpose
This repository is the Certes.CertKit ACME client library fork.  
Current priority: add functionality to the ACME client on `.NET 8` while keeping existing behavior stable.

## Repository Layout
- `src/Certes.CertKit`: core ACME library.
- `test/Certes.CertKit.Tests`: unit tests plus a few tests that hit a live ACME endpoint.
- `test/Certes.CertKit.Tests.Integration`: integration tests that require external/local ACME test infrastructure.
- `docs`: API and usage docs (`docs/APIv2.md`, `docs/README.md`).

## Technology Baseline
- Target framework: `net8.0` (all projects).
- Language style is enforced by `.editorconfig` and shared props in `misc/certes.props`.
- Warnings are treated as errors (`TreatWarningsAsErrors=true`).

## Build and Test Commands
Run from repo root:

```powershell
dotnet restore Certes.CertKit.sln
dotnet build Certes.CertKit.sln -m:1
dotnet test test/Certes.CertKit.Tests/Certes.CertKit.Tests.csproj -m:1
dotnet test test/Certes.CertKit.Tests.Integration/Certes.CertKit.Tests.Integration.csproj -m:1
```

If signing fails in your environment, use:

```powershell
dotnet build Certes.CertKit.sln -m:1 -p:SkipSigning=true
```

## ACME Implementation Hotspots
When adding functionality, start here:

- `src/Certes.CertKit/AcmeContext.cs`: high-level ACME flows (account/order/revoke/key change).
- `src/Certes.CertKit/Acme/AcmeHttpClient.cs`: HTTP transport, nonce handling, user-agent headers, response parsing.
- `src/Certes.CertKit/Acme/IAcmeHttpClient.cs`: badNonce retry behavior and post/get extensions.
- `src/Certes.CertKit/Extensions/*.cs`: high-level extension methods used by consumers.

## Test Environment Notes
- `test/Certes.CertKit.Tests/IntegrationHelper.cs` is currently wired to local Pebble at `https://127.0.0.1:14000/dir`.
- Test HTTP client intentionally accepts self-signed certs for local testing.
- Some integration scenarios still depend on historical `certes-ci.dymetis.com` infrastructure (not fully local), so full integration runs may fail even when core changes are correct.
- Prefer validating new logic with focused unit tests first, then run the smallest relevant integration subset.

## Project Guardrails
- Keep Azure-specific code out unless explicitly requested. Azure Functions code has been removed from this repo.
- Preserve ACME protocol compatibility and existing public API behavior unless a breaking change is intentional and documented.
- Keep `User-Agent` behavior intact for ACME HTTP calls.
- Avoid broad refactors unrelated to the feature being implemented.

## Definition of Done for Feature Work
- Code compiles for the solution on `net8.0`.
- Relevant tests are added/updated and executed.
- Public-facing behavior changes are reflected in docs (`docs/APIv2.md` and/or `docs/README.md`).
- Any known infra-dependent test failures are called out explicitly in PR notes.
