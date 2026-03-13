**Scope**: `Safetch.Core` — Guards, Http
**Tags**: security, ssrf, guards, dns-pinning, ip-validation, domain, backend
**Summary**: Design of the request guard pipeline and SafeHttpFetcher — SSRF prevention, IP validation, redirect safety, and body size enforcement.
**See Also**: fetch-service.md, ../architecture/overview.md

---

## Guard Pipeline

Guards implement `IRequestGuard` and are registered as `OrderedGuard` records pairing a guard with an integer order value. `FetchService` runs them in ascending order and short-circuits on the first block.

```
FetchRequest → [Order 1: UrlSchemeGuard] → [Order 2: EncodedIpGuard] → [Order 3: SsrfGuard] → SafeHttpFetcher
```

| Guard | Order | Remediation | What it blocks |
|---|---|---|---|
| `UrlSchemeGuard` | 1 | #17 | Non-http/https schemes and unparseable URLs |
| `EncodedIpGuard` | 2 | #9 | Literal private IP addresses in the URL host |
| `SsrfGuard` | 3 | #1 | Hostnames that resolve (via DNS) to private IPs |

Guards are registered as **Scoped** via `ServiceCollectionExtensions.AddRequestGuard<T>(services, order)`. This is safe because `SafeHttpFetcher` (Singleton) does not depend on guards.

A blocked guard returns `FetchResponse { Success=false, ErrorCode="BLOCKED" }` — mapped to HTTP 400.

---

## IpValidator

`IpValidator.IsPrivate(IPAddress)` is a static utility used by both `EncodedIpGuard` and `SsrfGuard`. It covers:

- IPv4: RFC 1918 (10/8, 172.16/12, 192.168/16), loopback (127/8), link-local (169.254/16), this-network (0/8)
- IPv6: loopback (::1), ULA (fc00::/7), link-local (fe80::/10)
- IPv4-mapped IPv6 (::ffff:x.x.x.x) — unwrapped to IPv4 before range checks

**Known gap**: hex/decimal/octal IP literals (e.g. `0x7f000001`, `2130706433`) are not explicitly handled. In practice: .NET's `Uri` parser rejects most of them, and any that reach DNS resolution will be caught by `SsrfGuard`.

---

## SafeHttpFetcher

`SafeHttpFetcher` is a **Singleton** that owns its own `HttpClient` lifecycle. It is **not** registered via `IHttpClientFactory` — it builds a `SocketsHttpHandler` directly to gain access to `ConnectCallback`.

**Why not `IHttpClientFactory`?** `IHttpClientFactory` does not expose `ConnectCallback`, which is required for DNS pinning. Owning the handler directly is the only way to intercept the TCP connection after DNS resolution.

### DNS Pinning (`ConnectCallback`)

`PinToValidatedIpAsync` resolves DNS, validates all returned IPs via `IpValidator.IsPrivate`, and connects to the first valid public IP. If all IPs are private, it throws `SsrfException` (internal), which `FetchAsync` catches and converts to `SafeFetchResult.Blocked`.

This prevents **DNS rebinding attacks**: even if the initial DNS resolution passes `SsrfGuard`, a malicious server cannot cause a second DNS resolution (at connect time) to return a private IP — because `ConnectCallback` validates at the socket level.

> Note: the session ID is not available inside `ConnectCallback` — this is a .NET constraint. Log correlation is limited at this point.

### Manual Redirect Loop

`AllowAutoRedirect = false`. `FetchAsync` follows redirects manually, up to `FetchOptions.MaxRedirects` (default: 3). Each redirect target is validated with `CheckRedirectUrlAsync` before following — mirroring `SsrfGuard` logic but operating on a raw URL string rather than a `FetchRequest`.

Redirect SSRF failures return `SafeFetchResult.Blocked` → `FetchResponse { ErrorCode="FETCH_FAILED" }` → HTTP 502.

### Body Size Enforcement

`LengthLimitedStream` wraps the response body stream and throws `ResponseTooLargeException` (internal) once cumulative bytes read exceed `FetchOptions.MaxResponseBytes` (default: 10 MB). The exception is caught in `ReadBodyAsync` and converted to `SafeFetchResult.Blocked`.

`HttpCompletionOption.ResponseHeadersRead` is used so the body is streamed rather than buffered — the limit is enforced during reading, not after downloading the full response.

---

## Error Codes

| `ErrorCode` | HTTP status | Cause |
|---|---|---|
| `BLOCKED` | 400 | A guard rejected the URL (scheme, IP range, SSRF) |
| `FETCH_FAILED` | 502 | Network error, DNS rebinding at connect, too many redirects, redirect SSRF, response too large |

Unexpected exceptions from `SafeHttpFetcher` are caught in `FetchService` and also returned as `FETCH_FAILED`.

---

## FetchOptions defaults

| Option | Default | Notes |
|---|---|---|
| `MaxResponseBytes` | 10 MB | Enforced via `LengthLimitedStream` |
| `MaxRedirects` | 3 | Per fetch call (not per hop timeout) |
| `TimeoutSeconds` | 15 | Applies to the full redirect chain, not per hop |

Config section binding is not yet wired — defaults come from the `FetchOptions` class directly.
