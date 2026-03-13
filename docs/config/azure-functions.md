# Azure Functions Configuration for Safetch

Safetch uses Azure Functions v4 isolated worker runtime. Its configuration is split across two files:

| File | Scope | Managed by |
|------|--------|-------------|
| `host.json` | Runtime-wide (all functions, logging, timeout, telemetry) | Safetch team — versioned in Git |
| `local.settings.json` | Local dev only (secrets, feature flags, storage) | Developers — excluded from Git |

---

## `host.json` Reference

### `version`
- Required: `"2.0"` for isolated worker model.
- Safetch uses this — no change needed.

### `functionTimeout`
- Default: `"00:05:00"`. Safetch sets `"00:10:00"` to safely handle large HTML payloads or slow upstreams.
- ⚠️ Must stay ≤ `"00:10:00"` on Consumption plan.

### `logging`
- Safetch configures granular log levels:
  - `Safetch.Api.FetchFunction`: `Information` (full request/response trace)
  - `Safetch.Core.SafeHttpFetcher`: `Warning` (only errors/warnings)
  - `Safetch.Core.ContentProcessor`: `Warning` (suppresses verbose sanitisation logs)
- Application Insights sampling enabled (`maxTelemetryItemsPerSecond: 20`) — reduces noise without losing signal.

---

## `local.settings.json` Reference

Safetch defines these keys for local development:

| Key | Value | Purpose |
|-----|-------|---------|
| `AzureWebJobsStorage` | `UseDevelopmentStorage=true` | Enables local Storage Emulator |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` | Required for .NET 9 isolated worker |
| `Safetch:RateLimit:Enabled` | `false` | Disables rate limiting locally (easier testing) |
| `Safetch:Telemetry:LogLevel` | `Debug` | Enables detailed telemetry during local debugging |
| `FetchOptions:MaxResponseBytes` | `10485760` | Max response body size in bytes (default 10 MB) |
| `FetchOptions:MaxRedirects` | `3` | Max HTTP redirects before the request is aborted |
| `FetchOptions:TimeoutSeconds` | `15` | Per-request HTTP timeout in seconds |

> 🔐 `local.settings.json` is git-ignored. Never commit secrets here.

---

## Security Notes
- `host.json` is **not secret** — it’s safe to version in Git.
- `local.settings.json` **must never be committed** — it may contain keys or override production behavior.
- All config values are loaded via `IConfiguration` and validated at startup. Invalid JSON causes immediate host failure.