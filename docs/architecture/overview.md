**Scope**: Entire solution
**Tags**: architecture, solution-structure, azure-functions, dotnet
**Summary**: Solution map, technology choices, and key design decisions.
**See Also**: ../INDEX.md

---

## Solution map

| Project | Purpose | Dependencies |
|---------|---------|--------------|
| `Safetch.Core` | Models, interfaces, guards, service implementations. No Azure dependencies. | None (pure .NET 9 library) |
| `Safetch.Api` | Azure Functions isolated worker host, HTTP triggers, DI wiring. | `Safetch.Core` |
| `Safetch.Tests` | xUnit + Moq unit and integration tests. | `Safetch.Core`, `Safetch.Api` |

- **Dependency direction**: `Api` → `Core`, `Tests` → `Core` + `Api`.
- All three projects are part of the same solution (`Safetch.sln`).

---

## Technology choices

- **.NET 9** – intent to migrate to .NET 10 when tooling matures.
- **Azure Functions v4 isolated worker model** – not the deprecated in-process model.
- **`System.Text.Json`** – no Newtonsoft.Json used.
- **`SafeHttpFetcher` owns its `HttpClient`** – built directly on `SocketsHttpHandler` (not `IHttpClientFactory`) to enable `ConnectCallback` for DNS pinning. See `docs/domain/security-pipeline.md`.

---

## API surface

Two endpoints, same pipeline:

| Method | Parameters | Doc |
|---|---|---|
| `POST /fetch` | JSON body: `{ "url": "..." }` | [api/post-fetch.md](../api/post-fetch.md) |
| `GET /fetch` | Query string: `?url=...` | [api/get-fetch.md](../api/get-fetch.md) |

Both accept a URL, run the full security pipeline, and return a `FetchResponse`:

| Response (`FetchResponse`) — success | Response — failure |
|---|---|
| `{ "success": true, "url": "...", "content": "...", "statusCode": 200, "injectionWarnings": [] }` | `{ "success": false, "errorCode": "BLOCKED"\|"FETCH_FAILED", "error": "..." }` |

- Content is returned as Markdown (HTML pages are converted via ReverseMarkdown).
- `injectionWarnings` is present when injection patterns are detected — each item has `category`, `severity`, and `patternMatched`.

- GET is unsuitable for very long URLs — prefer POST for those.

---

## Error handling

| Condition | HTTP response |
|-----------|---------------|
| Invalid JSON body | `400 Bad Request` |
| Missing or blank `url` field | `400 Bad Request` |
| URL blocked by guard (scheme, IP, SSRF) | `400 Bad Request` (`ErrorCode="BLOCKED"`) |
| Fetch failed (DNS rebinding, redirect SSRF, too large, network error) | `502 Bad Gateway` (`ErrorCode="FETCH_FAILED"`) |
| Valid upstream HTTP error (4xx, 5xx) | `200 OK` with `StatusCode` reflecting upstream status |

`FetchService` never throws for URL or guard failures — it returns `FetchResponse { Success=false }`. See `docs/domain/security-pipeline.md` for guard details.

---

## Security pipeline

**Guards (Sprint 1)** — run before every fetch:

1. `UrlSchemeGuard` — rejects non-http/https schemes
2. `EncodedIpGuard` — rejects literal private IPs in the URL host
3. `SsrfGuard` — resolves DNS and rejects private IP targets

`SafeHttpFetcher` adds DNS pinning at the socket level and per-hop redirect SSRF validation. See `docs/domain/security-pipeline.md`.

**Content processors (Sprint 2)** — run after every successful fetch:

1. `HtmlSanitizerProcessor` — removes CSS-hidden elements, `data-*` attributes, `<svg>`, `<meta http-equiv>` (HTML only)
2. `HtmlToMarkdownProcessor` — converts HTML to Markdown (HTML only)
3. `UnicodeTagStripProcessor` — strips Unicode Tags block characters (U+E0000–U+E007F)
4. `InjectionPatternProcessor` — detects known prompt injection phrases; detection-only, emits warnings
5. `SpotlightingProcessor` — wraps content in untrusted-content boundary markers

See `docs/domain/content-processing.md`.
