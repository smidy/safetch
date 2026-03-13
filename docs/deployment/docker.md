# Running Safetch in Docker

## Build the image
```bash
docker build -t safetch .
```

## Run the container
```bash
docker run -p 7071:80 \
  -e AzureWebJobsStorage="UseDevelopmentStorage=true" \
  -e FUNCTIONS_WORKER_RUNTIME="dotnet-isolated" \
  -e RateLimit__WindowSeconds=60 \
  -e RateLimit__MaxRequests=30 \
  safetch
```

> **Note**: The Azure Functions runtime inside the container listens on port **80**. The `-p 7071:80` maps host port 7071 to container port 80, matching the default local Functions port for consistency.

## Test the endpoint
```bash
curl "http://localhost:7071/api/fetch?url=https://example.com"
```

## Using Azurite for local storage

If your HTTP-only functions don't actively use storage, `UseDevelopmentStorage=true` is sufficient. If you need a real Azurite instance, run it separately:
```bash
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

Then set `AzureWebJobsStorage` to the Azurite connection string:
```
DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=<key>;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;
```

## Environment variables reference

| Variable | Required | Description | Example |
|---|---|---|---|
| `AzureWebJobsStorage` | Yes | Storage connection. Use Azurite or a real connection string. | `UseDevelopmentStorage=true` |
| `FUNCTIONS_WORKER_RUNTIME` | Yes | Must be `dotnet-isolated` | `dotnet-isolated` |
| `RateLimit__WindowSeconds` | No | Rate limit window in seconds | `60` |
| `RateLimit__MaxRequests` | No | Max requests per window per session | `30` |

## Notes
- Authentication is not enforced in the container — treat the exposed port as internal/trusted only.
- `local.settings.json` is excluded by `.dockerignore` and must never be copied into the image.