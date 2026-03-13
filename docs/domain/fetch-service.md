**Scope**: `Safetch.Core`
**Tags**: domain, fetch-service, guards, pipeline, content-processing, core
**Summary**: How FetchService orchestrates the guard pipeline, SafeHttpFetcher, and content processor pipeline to produce a FetchResponse.
**See Also**: security-pipeline.md, content-processing.md, ../api/post-fetch.md, ../architecture/overview.md

## Responsibilities

- Run all registered `IRequestGuard` instances in order; short-circuit on first block.
- Delegate the actual HTTP fetch to `SafeHttpFetcher`.
- Parse the raw `Content-Type` header from the fetch result and pass the MIME type to the content processor pipeline.
- Run the `ContentProcessorPipeline` on the response body.
- Map guard, fetcher, and processor results into a `FetchResponse`.
- Never throw for URL or guard failures — always return `FetchResponse { Success=false }`.

## Constructor dependencies

```csharp
public FetchService(
    IEnumerable<OrderedGuard> guards,
    SafeHttpFetcher fetcher,
    ContentProcessorPipeline pipeline,
    ILogger<FetchService> logger)
```

`SafeHttpFetcher` is a Singleton. `FetchService` and `ContentProcessorPipeline` are Scoped. Guards are also Scoped.

## Flow

```
FetchRequest
  │
  ├─ Guards (ordered, short-circuit on block)
  │    └─ Blocked → FetchResponse { Success=false, ErrorCode="BLOCKED" }
  │
  ├─ SafeHttpFetcher.FetchAsync
  │    └─ SafeFetchResult.Success=false → FetchResponse { Success=false, ErrorCode="FETCH_FAILED" }
  │    └─ Unexpected exception → FetchResponse { Success=false, ErrorCode="FETCH_FAILED" }
  │
  ├─ MIME type parsed from Content-Type header (strip parameters, lowercase, default "text/plain")
  │
  ├─ ContentProcessorPipeline.RunAsync(content, ProcessingContext(mimeType, url))
  │    └─ Returns ProcessorResult { Content, Warnings }
  │
  └─ FetchResponse { Success=true, Url, Content, StatusCode, Warnings }
```

## FetchResponse shape

| Field | Type | Notes |
|---|---|---|
| `Success` | bool | False on any guard or fetch failure |
| `Url` | string | Echo of request URL (only set on success) |
| `Content` | string | Processed body — HTML converted to Markdown, sanitised, spotlighted (only set on success) |
| `StatusCode` | int | Upstream HTTP status (only set on success) |
| `Warnings` | `IReadOnlyList<string>` | Accumulated from all processors — always `[]` on failure paths, never omitted |
| `ErrorCode` | string? | `"BLOCKED"` or `"FETCH_FAILED"` |
| `ErrorMessage` | string? | Human-readable reason for the failure |

## Upstream HTTP error handling

`FetchService` does **not** call `EnsureSuccessStatusCode`. All upstream status codes (4xx, 5xx) are surfaced in `FetchResponse.StatusCode` with `Success=true`. The caller decides how to treat non-2xx responses.

## MIME type parsing

`FetchService` strips parameters from the raw `Content-Type` header before building `ProcessingContext`:
- Split on `;`, take the first segment, trim, lowercase
- If null or empty: default to `"text/plain"`

Example: `"text/html; charset=utf-8"` → `"text/html"`
