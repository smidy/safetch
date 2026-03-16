# Running Safetch in Docker

The `Safetch.Api` project ships with a multi-stage `Dockerfile` at the repo root. The runtime stage uses `mcr.microsoft.com/dotnet/aspnet:9.0` and publishes a framework-dependent build to `/app`, with `WORKDIR /app` set before the entrypoint so `dotnet Safetch.Api.dll` resolves correctly.

## Build the image
```bash
docker build -t safetch-api .
```

## Run the container
```bash
docker run -p 5000:8080 safetch-api
```

> ASP.NET Core listens on port **8080** inside the container. `-p 5000:8080` maps host port 5000 to container port 8080.

## Test the endpoint
```bash
curl "http://localhost:5000/api/fetch?url=https://example.com&mode=markdown"
```

## Environment variables reference

| Variable | Required | Description | Example |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | No | Set to `Development` to use in-memory rate limiting with a fixed identity. Omit or set to `Production` for your own auth/rate-limiting implementation. | `Development` |
| `FetchOptions:MaxResponseBytes` | No | Max upstream response body size in bytes. Default: `10485760` (10 MB). | `10485760` |
| `FetchOptions:TimeoutSeconds` | No | Per-request HTTP timeout in seconds. Default: `15`. | `15` |
| `Safetch:RateLimit:Limits__0__MaxFetchesPerWindow` | No | Max requests per window per caller identity. Default: `100`. | `100` |

You can pass variables individually with `-e` or via a file:

```bash
docker run -p 5000:8080 --env-file .env safetch-api
```

## Notes

- The `Dockerfile` sets `WORKDIR /app` in the runtime stage so the `dotnet` entrypoint resolves `Safetch.Api.dll` relative to that directory.
- `Safetch.Api` ships with **no authentication** and an `InMemoryRateLimiter` using a fixed `"local"` caller identity. Deployers who add an auth layer should replace the caller identity passed to `CheckAndIncrementAsync` with the authenticated user's identity.
