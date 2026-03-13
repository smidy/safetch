# Safetch Local Development Guide

## Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Azure Functions Core Tools v4+](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local?tabs=v4,windows,csharp,portal,bash#install-the-azure-functions-core-tools)
  ```bash
  npm install -g azure-functions-core-tools@4 --unsafe-perm true
  ```
- (Optional) VS Code with C# and Azure Functions extensions

## Start the API Locally
1. Open PowerShell in `Safetch.Api/`
2. Run:
   ```powershell
   func host start --port 7071
   ```
3. Test the `/api/fetch` endpoint:
   ```bash
   curl "http://localhost:7071/api/fetch?uri=https://example.com"
   ```

> ✅ Expected: `200 OK` with JSON response containing fetched content.
> ⚠️ `400 Bad Request`: missing or invalid `uri` query param — expected for empty GET.

## Configuration Files
| File | Purpose |
|------|---------|
| `host.json` | Global Functions runtime config (timeout, logging, telemetry) |
| `local.settings.json` | Local dev settings (storage, worker runtime, feature flags) |

> 🔒 `local.settings.json` is excluded from Git — safe for secrets.
