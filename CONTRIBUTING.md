# Contributing to Safetch

Thank you for your interest in contributing.

## Prerequisites

- **.NET 9 SDK** — [download](https://dotnet.microsoft.com/download)
- **Azure Functions Core Tools v4** — [install guide](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- Git

## Build

```bash
dotnet build Safetch.sln
```

## Test

```bash
dotnet test
```

## Run Locally

```bash
cd Safetch.Api
func start
```

The host reads settings from `local.settings.json` (not committed to Git). See the Self-Hosting section in the [README](README.md) for a minimal template.

## Conventions

- **JSON serialisation**: Use `System.Text.Json` only — do not introduce Newtonsoft.Json.
- **HTTP clients**: `SafeHttpFetcher` intentionally manages its own `HttpClient` directly via `SocketsHttpHandler` to enable `ConnectCallback` for DNS pinning. This is by design — do not refactor it to use `IHttpClientFactory`. For any new services that require outbound HTTP (and do not need socket-level control), prefer `IHttpClientFactory`.
- **Project structure**:
  - New domain services, models, and interfaces go in `Safetch.Core`.
  - New HTTP triggers (Azure Functions) go in `Safetch.Api/Functions/`.
  - Unit tests go in `Safetch.Tests`.
- **Error handling**: Do not swallow upstream HTTP errors; surface status codes to the caller.
- **No auth in this repo**: Authentication is out of scope here — operators apply their own before deployment.

## Pull Requests

- Keep PRs focused — one concern per PR.
- All new code must have test coverage.
- Run `dotnet test` before submitting.
- Write clear commit messages.

## Licence

By contributing you agree your contributions are licensed under the [MIT Licence](LICENSE).