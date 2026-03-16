# Hosting Safetch on Azure Functions

Safetch's core logic (`Safetch.Core`) is deployment-agnostic. The base `Safetch.Api` project is an ASP.NET Core Minimal API host.

If you want to run Safetch as an Azure Function, you can wrap `Safetch.Core` in an Azure Functions isolated worker project. A reference Azure Functions implementation is available in the [safetch GitHub repository](https://github.com/smidy/safetch) (see `safetch-functions/`) that demonstrates how to do this, including:

- `FetchFunction` — Azure Functions trigger wrapping `IFetchService`
- `TableApiKeyStore` — Azure Table Storage implementation of `IApiKeyStore`
- `TableApiKeyRateLimiter` — Azure Table Storage implementation of `IApiKeyRateLimiter`

These concrete Azure implementations are not included in this library — they are deployment-specific infrastructure. You are free to provide your own implementations of `IApiKeyStore` and `IApiKeyRateLimiter` from `Safetch.Core.Auth`.

## Key configuration for Azure Functions deployments

| Key | Purpose |
|---|---|
| `AzureWebJobsStorage` | Azure Storage connection string (for Table Storage rate limiting and key store) |
| `FUNCTIONS_WORKER_RUNTIME` | Must be `dotnet-isolated` for .NET 9 |
| `Safetch:RateLimit:Limits[0]:MaxFetchesPerWindow` | Max requests per window per caller |
| `FetchOptions:MaxResponseBytes` | Max upstream response body size |
| `FetchOptions:TimeoutSeconds` | Per-request timeout |

> For local Functions development, use `UseDevelopmentStorage=true` as the `AzureWebJobsStorage` value with [Azurite](https://github.com/Azure/Azurite).
