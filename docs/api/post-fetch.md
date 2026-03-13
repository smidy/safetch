**Scope**: `Safetch.Api`
**Tags**: api, http, fetch, endpoint
**Summary**: API reference for the POST /fetch endpoint.
**See Also**: get-fetch.md, ../domain/fetch-service.md, ../domain/security-pipeline.md, ../architecture/overview.md

## Endpoint
`POST /fetch`

## Authentication
None (anonymous). **Authentication must be added before Azure deployment.**

## Request
JSON body:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `url` | string | Yes | Absolute HTTP or HTTPS URL to fetch. |
| `sessionId` | string | No | Session identifier for rate limiting and audit. Auto-generated if omitted. |
| `mode` | string | No | Response mode. One of `raw` (default), `readable`, `text`, `markdown`. See [Response Modes](#response-modes) below. |

```json
{ "url": "https://example.com", "mode": "readable" }
```

## Response — success

| Field | Type | Description |
|-------|------|-------------|
| `success` | bool | Always `true` on success |
| `url` | string | The fetched URL |
| `content` | string | Processed content (Markdown for HTML pages) |
| `statusCode` | integer | Upstream HTTP status code |
| `sessionId` | string | Session ID used for this request |
| `warnings` | string[] | Non-empty if injection patterns or Unicode Tag characters were detected |

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

## Response — failure

| Field | Type | Description |
|-------|------|-------------|
| `error` | string | Human-readable reason |
| `errorCode` | string\|null | `"BLOCKED"`, `"FETCH_FAILED"`, or `null` for validation errors |

```json
{ "error": "URL scheme 'ftp' is not permitted. Only http and https are allowed.", "errorCode": "BLOCKED" }
```

> **Breaking change notice**: Prior to this version the error envelope only contained `error`. The `errorCode` field was added to allow callers to handle `BLOCKED` vs `FETCH_FAILED` programmatically.

## HTTP Status Codes

| Code | Condition |
|------|-----------|
| `200` | Successful fetch. Returns `FetchResponse` with `success: true`. |
| `400` | Invalid JSON body; missing/blank `url`; URL blocked by a guard (`BLOCKED`). |
| `502` | Fetch failed at network level — DNS rebinding, redirect SSRF, too many redirects, response too large, network error (`FETCH_FAILED`). |

Note: valid upstream HTTP errors (4xx, 5xx) are returned as `200 OK` with the upstream `statusCode` in the body. The caller decides how to treat non-2xx upstream responses.

All responses are `application/json; charset=utf-8`.

## Response Modes

| Mode | Description |
|------|-------------|
| `raw` | Default. Returns the full processed HTML/Markdown content as today. |
| `readable` | Extracts the primary article body using Mozilla Readability (SmartReader). Returns clean HTML without nav, headers, footers. Falls back to sanitised HTML if extraction fails — a warning is added to `warnings`. |
| `text` | Same as `readable` but strips remaining HTML tags, returning plain text. Useful for LLM consumption. |
| `markdown` | Same as `readable` but converts the extracted article HTML to Markdown. Best format for LLM consumption where structure matters. |

## Example

```bash
# Default (raw)
curl -X POST https://localhost:7071/fetch \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com"}'

# Readable extraction
curl -X POST https://localhost:7071/fetch \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com", "mode": "readable"}'

# Plain text
curl -X POST https://localhost:7071/fetch \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com", "mode": "text"}'

# Markdown (article extraction + Markdown conversion)
curl -X POST https://localhost:7071/fetch \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com", "mode": "markdown"}'
```
