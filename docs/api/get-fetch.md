**Scope**: `Safetch.Api`
**Tags**: api, http, fetch, endpoint, get
**Summary**: API reference for the GET /fetch endpoint.
**See Also**: post-fetch.md, ../domain/fetch-service.md, ../domain/security-pipeline.md, ../architecture/overview.md

## Endpoint
`GET /fetch`

## Authentication

Authentication is not part of the core library contract — it is implemented at the host/deployment layer. Self-hosters may use any mechanism: API keys, JWT, mutual TLS, or no auth at all.

> See your deployment's documentation for the specific auth model in use.

## Request

Query parameters:

| Parameter | Required | Description |
|---|---|---|
| `url` | Yes | Absolute HTTP or HTTPS URL to fetch. |
| `mode` | No | Response mode. One of `raw` (default), `readable`, `text`, `markdown`. See [Response Modes](#response-modes) below. |

```
GET /fetch?url=https://example.com&mode=readable
```

**Note**: GET is unsuitable for very long target URLs. The Azure Functions host imposes query-string length limits that the POST (body) endpoint does not share. Use `POST /fetch` for long URLs.

## Response — success

| Field | Type | Description |
|---|---|---|
| `success` | bool | Always `true` on success |
| `url` | string | The fetched URL |
| `content` | string | Processed content |
| `statusCode` | integer | Upstream HTTP status code |
| `injectionWarnings` | array | Structured injection warnings detected during processing. Empty array if none. |

Each `injectionWarnings` item:

| Field | Type | Description |
|---|---|---|
| `category` | string | One of: `ScriptInjection`, `DataExfiltration`, `AuthorityOverride`, `PersonaHijacking`, `InstructionOverride`, `JailbreakFraming`, `MemoryPoisoning`, `ModelFormatMarker`, `ToolCallCoercion` |
| `patternMatched` | string | The text fragment that triggered the warning |
| `severity` | integer | `0` = Informational, `1` = Medium, `2` = High |

```json
{
  "success": true,
  "url": "https://example.com",
  "content": "# Example Domain\n...",
  "statusCode": 200,

  "injectionWarnings": []
}
```

## Response — failure

| Field | Type | Description |
|---|---|---|
| `error` | string | Human-readable reason |
| `errorCode` | string\|null | `"BLOCKED"`, `"FETCH_FAILED"`, `"UNAUTHORIZED"`, or `null` for validation errors |

```json
{ "error": "url is required and must be a valid absolute HTTP/HTTPS URL", "errorCode": null }
```

## HTTP Status Codes

| Code | Condition |
|---|---|
| `200` | Successful fetch. Returns `FetchResponse` with `success: true`. |
| `400` | Missing or invalid `url` param; URL blocked by a guard (`BLOCKED`). |
| `429` | Per-caller rate limit exceeded. Response includes `Retry-After` header. Maximum is configurable via `Safetch:RateLimit:MaxFetchesPerWindow`. |
| `502` | Fetch failed at network level — DNS rebinding, redirect SSRF, too many redirects, response too large, network error (`FETCH_FAILED`). |

Valid upstream HTTP errors (4xx, 5xx) are returned as `200 OK` with the upstream `statusCode` in the body.

All responses are `application/json; charset=utf-8`.

## Response Modes

| Mode | Description |
|------|-------------|
| `raw` | Default. Returns the full processed HTML/Markdown content as today. |
| `readable` | Extracts the primary article body using Mozilla Readability (SmartReader). Returns clean HTML without nav, headers, footers. Falls back to sanitised HTML if extraction fails. |
| `text` | Same as `readable` but strips remaining HTML tags, returning plain text. Useful for LLM consumption. |
| `markdown` | Same as `readable` but converts the extracted article HTML to Markdown. Best format for LLM consumption where structure matters. |

## Example

```bash
# Default (raw)
curl "http://localhost:7071/api/fetch?url=https://example.com"

# Readable extraction
curl "http://localhost:7071/api/fetch?url=https://example.com&mode=readable"

# Plain text
curl "http://localhost:7071/api/fetch?url=https://example.com&mode=text"

# Markdown (article extraction + Markdown conversion)
curl "http://localhost:7071/api/fetch?url=https://example.com&mode=markdown"
```
