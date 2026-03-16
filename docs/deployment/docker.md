# Running Safetch in Docker

The base `Safetch.Api` project is an ASP.NET Core app. To run it in Docker:

## Build the image
```bash
docker build -t safetch .
```

## Run the container
```bash
docker run -p 5000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  safetch
```

> ASP.NET Core containers default to listening on port **8080** inside the container. The `-p 5000:8080` maps host port 5000 to container port 8080.

## Test the endpoint
```bash
curl "http://localhost:5000/api/fetch?url=https://example.com"
```

## Environment variables reference

| Variable | Required | Description | Example |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | No | Set to `Development` to use in-memory rate limiting with a fixed identity. Omit or set to `Production` to use your own auth and rate limiting implementation. | `Development` |
| `FetchOptions:MaxResponseBytes` | No | Max upstream response body size in bytes. Default: `10485760` (10 MB). | `10485760` |
| `FetchOptions:TimeoutSeconds` | No | Per-request HTTP timeout. Default: `15`. | `15` |
| `Safetch:RateLimit:Limits__0__MaxFetchesPerWindow` | No | Max requests per window per caller identity. Default: `100`. | `100` |

## Notes

- The base `Safetch.Api` project ships with **no auth** and an `InMemoryRateLimiter` using a fixed `"local"` caller identity. Deployers who add an auth layer should replace the caller identity passed to `CheckAndIncrementAsync` with the authenticated user's identity.
