# Safetch

A self-hosted, security-hardened web fetch proxy for AI agents.

## Overview

Safetch is a minimal, auditable, and secure HTTP fetch service designed for AI agents that need to retrieve and process web content safely. It solves the problem of untrusted web fetching by centralising, validating, and sanitising all outbound requests — blocking SSRF, private IP access, prompt injection, and unsafe content before it reaches your LLM or agent logic.

## Why Safetch

- **SSRF protection**: DNS pinning, redirect validation, and strict URL scheme/host allowlisting
- **Content sanitisation pipeline**: HTML sanitisation, Unicode Tag stripping, injection detection, and spotlighting of suspicious patterns
- **Readable content extraction**: Mozilla Readability integration for clean article body extraction
- **LLM-ready output**: Markdown conversion of readable content — ideal for prompt context
- **Per-session rate limiting**: Prevent abuse without requiring global auth state
- **Structured audit telemetry**: All fetches emit structured logs with warnings, blocks, and metadata

## Architecture

Safetch is an Azure Functions v4 isolated worker built on .NET 9. It follows a three-project solution structure: `Safetch.Core` (domain logic), `Safetch.Api` (HTTP triggers), and `Safetch.Tests`. It uses `System.Text.Json` exclusively — no Newtonsoft.Json — and avoids unnecessary abstractions for observability and security control.

## Self-Hosting

### Prerequisites

- .NET 9 SDK
- Azure Functions Core Tools v4
- Git

### Steps

1. Clone: `git clone https://github.com/smidy/safetch.git && cd safetch`
2. Build: `dotnet build`
3. Navigate: `cd Safetch.Api`
4. Create `local.settings.json`:

```json
{
  "IsEncryptedValues": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
  }
}
```

5. Start: `func start`

### Test it

```bash
curl "http://localhost:7071/api/fetch?url=https://example.com&mode=markdown"
```

## API Reference

### GET /fetch

Query parameters: `url` (required), `sessionId` (optional), `mode` (optional: `raw` \| `readable` \| `text` \| `markdown`, default `raw`)

```bash
curl "http://localhost:7071/api/fetch?url=https://example.com&mode=markdown"
```

**Response (success):**

```json
{
  "success": true,
  "url": "https://example.com",
  "content": "# Example Domain\n...",
  "statusCode": 200,
  "sessionId": "abc123",
  "warnings": []
}
```

**Response (failure):**

```json
{ "error": "URL scheme 'ftp' is not permitted.", "errorCode": "BLOCKED" }
```

> ⚠️ Note: GET has URL length limits for very long target URLs — use POST for those.

### POST /fetch

JSON body: `{ "url": "...", "sessionId": "...", "mode": "..." }` (`sessionId` and `mode` optional)

```bash
curl -X POST http://localhost:7071/api/fetch \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com", "mode": "markdown"}'
```

### Response modes

| Mode | Description |
|---|---|
| `raw` | Full sanitised HTML/content |
| `readable` | Article body extracted via Mozilla Readability |
| `text` | Readable extraction, HTML tags stripped — plain text |
| `markdown` | Readable extraction converted to Markdown — best for LLM consumption |

### HTTP status codes

| Code | Condition |
|---|---|
| 200 | Success (or upstream 4xx/5xx — check `statusCode` in body) |
| 400 | Invalid request or URL blocked (`BLOCKED`) |
| 502 | Fetch failed at network level (`FETCH_FAILED`) |

## Authentication

> ⚠️ Safetch ships with **no authentication**. Before exposing this service to any network, operators must implement their own authentication layer (e.g. API keys, JWT, mutual TLS). This is intentional — auth requirements vary by deployment context.

## Configuration

| Setting | Config key | Default | Description |
|---|---|---|---|
| Max response size | `FetchOptions:MaxResponseSizeBytes` | 5242880 (5 MB) | Maximum size of upstream response body |
| Max redirects | `FetchOptions:MaxRedirects` | 5 | Maximum HTTP redirects to follow |
| Rate limit window | `Safetch:RateLimit:WindowSeconds` | 60 | Rolling window in seconds |
| Rate limit max requests | `Safetch:RateLimit:MaxRequests` | 100 | Max requests per session per window |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md)

## License

[MIT](LICENSE)