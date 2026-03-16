# Safetch

A self-hosted, security-hardened web fetch proxy for AI agents.

## Overview

Safetch is a minimal, auditable, and secure HTTP fetch service designed for AI agents that need to retrieve and process web content safely. It solves the problem of untrusted web fetching by centralising, validating, and sanitising all outbound requests — blocking SSRF, private IP access, prompt injection, and unsafe content before it reaches your LLM or agent logic.

## Why Safetch

- **SSRF protection**: DNS pinning, redirect validation, and strict URL scheme/host allowlisting
- **Content sanitisation pipeline**: HTML sanitisation, Unicode Tag stripping, categorised injection detection, and spotlighting of suspicious patterns
- **Readable content extraction**: Mozilla Readability integration for clean article body extraction
- **LLM-ready output**: Markdown conversion of readable content — ideal for prompt context
- **Structured audit telemetry**: All fetches emit structured logs with warnings, blocks, and metadata

## Architecture

Safetch is a .NET 9 solution with three projects: `Safetch.Core` (domain logic), `Safetch.Api` (ASP.NET Core Minimal API host), and `Safetch.Tests`. It uses `System.Text.Json` exclusively — no Newtonsoft.Json — and avoids unnecessary abstractions for observability and security control.

## Self-Hosting

### Prerequisites

- .NET 9 SDK
- Git

### Steps

1. Clone: `git clone https://github.com/smidy/safetch.git && cd safetch`
2. Build: `dotnet build`
3. Navigate: `cd Safetch.Api`
4. Run:

```bash
dotnet run
```

The API starts on `http://localhost:5000` by default.

### Test it

```bash
# GET
curl "http://localhost:5000/api/fetch?url=https://example.com&mode=markdown"

# POST
curl -X POST http://localhost:5000/api/fetch \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com", "mode": "markdown"}'
```

## API Reference

### GET /api/fetch

Query parameters: `url` (required), `mode` (optional: `raw` \| `readable` \| `text` \| `markdown`, default `raw`)

```bash
curl "http://localhost:5000/api/fetch?url=https://example.com&mode=markdown"
```

**Response (success):**

```json
{
  "success": true,
  "url": "https://example.com",
  "content": "# Example Domain\n...",
  "statusCode": 200,
  "injectionWarnings": []
}
```

**Response (failure):**

```json
{ "error": "URL scheme 'ftp' is not permitted.", "errorCode": "BLOCKED" }
```

> ⚠️ Note: GET has URL length limits for very long target URLs — use POST for those.

### POST /api/fetch

JSON body: `{ "url": "...", "mode": "..." }` (`mode` optional)

```bash
curl -X POST http://localhost:5000/api/fetch \
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

### Injection warnings

When the content processor detects a potential prompt-injection pattern, the response includes structured `injectionWarnings` — each warning carries a `category`, `severity`, and the matched `patternMatched` string.

```json
{
  "injectionWarnings": [
    {
      "category": "MemoryPoisoning",
      "severity": "High",
      "patternMatched": "in future conversations"
    }
  ]
}
```



**Detection categories:**

| Category | Severity | Description |
|---|---|---|
| `InstructionOverride` | Medium | Phrases instructing the agent to ignore prior instructions |
| `PersonaHijacking` | Medium | Phrases attempting to redefine the agent's identity or persona |
| `ModelFormatMarker` | Informational | Tokenizer prefix/suffix tokens from known model formats |
| `DataExfiltration` | High | Directives to send data to an external URL |
| `ToolCallCoercion` | High | Directives to invoke agent tools or functions directly |
| `AuthorityOverride` | High | Phrases asserting false system-level or operator authority |
| `MemoryPoisoning` | High | Phrases designed to persist malicious instructions in AI memory (MITRE AML.T0080.000) |
| `JailbreakFraming` | Medium | Well-known jailbreak trigger phrases |

> ⚠️ Pattern detection raises the bar against known attack patterns but cannot prevent adaptive or encoded attacks. Treat `injectionWarnings` as a signal — not a guarantee of safety.

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
| Max response size | `FetchOptions:MaxResponseBytes` | 10485760 (10 MB) | Maximum size of upstream response body |
| Max redirects | `FetchOptions:MaxRedirects` | 3 | Maximum HTTP redirects to follow |
| Fetch timeout | `FetchOptions:TimeoutSeconds` | 15 | Total timeout for a fetch call (seconds) |
| Rate limit max requests | `Safetch:RateLimit:MaxFetchesPerWindow` | 100 | Max requests per hour per caller identity (configurable) |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md)

## License

[MIT](LICENSE)
