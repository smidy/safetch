# Safetch Local Development Guide

## Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Start the API Locally
1. Navigate to `Safetch.Api/`
2. Run:
   ```bash
   dotnet run
   ```
3. Test the `/api/fetch` endpoint:
   ```bash
   curl "http://localhost:5000/api/fetch?url=https://example.com"
   ```

> ✅ Expected: `200 OK` with JSON response containing fetched content.
> ⚠️ `400 Bad Request`: missing or invalid `url` query param — expected for empty GET.

## Configuration

Safetch uses standard ASP.NET Core configuration (`appsettings.json` / `appsettings.Development.json` / environment variables).

| Setting | Config key | Default | Description |
|---|---|---|---|
| Max response size | `FetchOptions:MaxResponseBytes` | 10485760 (10 MB) | Maximum upstream response body size |
| Max redirects | `FetchOptions:MaxRedirects` | 3 | Maximum HTTP redirects to follow |
| Fetch timeout | `FetchOptions:TimeoutSeconds` | 15 | Per-request timeout in seconds |
| Rate limit | `Safetch:RateLimit:Limits[0]:MaxFetchesPerWindow` | 100 | Max requests per window per caller identity |
| Rate limit window | `Safetch:RateLimit:Limits[0]:Window` | `01:00:00` | Rolling window duration (HH:mm:ss) |

> 💡 The base `Safetch.Api` project uses `InMemoryRateLimiter` with a fixed `"local"` identity — no cloud storage required for local development.

> 🔐 If you add auth to the host, update the caller identity passed to `CheckAndIncrementAsync` to reflect the authenticated user rather than `"local"`.
