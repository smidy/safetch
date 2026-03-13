# Safetch Docs Index

**Purpose**: Safe web content fetching service for AI agents — Azure Functions v4, .NET 9.

---

## Architecture

| File | Summary | Tags |
|---|---|---|
| [architecture/overview.md](architecture/overview.md) | Solution map, technology choices, error handling contract, security pipeline summary | architecture, solution-structure, azure-functions, dotnet |
| [config/azure-functions.md](config/azure-functions.md) | Azure Functions runtime configuration: host.json and local.settings.json semantics, Safetch-specific values, security notes | config, azure-functions, host.json, local.settings.json, security |
| [development/local-dev.md](development/local-dev.md) | Local development setup guide: prerequisites, how to run func host start, testing the /fetch endpoint, config file roles | development, local-dev, azure-functions, func-cli, debugging |

---

## Domain

| File | Summary | Tags |
|---|---|---|
| [domain/fetch-service.md](domain/fetch-service.md) | How FetchService orchestrates guards, SafeHttpFetcher, and the content processor pipeline to produce a FetchResponse | domain, fetch-service, guards, pipeline, content-processing, core |
| [domain/content-processing.md](domain/content-processing.md) | Content processor pipeline design — ordered, affinity-filtered processors, the five built-in processors, Warnings contract | domain, content-processing, pipeline, prompt-injection, html-sanitisation, unicode, spotlighting |
| [domain/security-pipeline.md](domain/security-pipeline.md) | Guard pipeline design, IpValidator scope and known limits, SafeHttpFetcher DNS pinning, redirect SSRF, body size enforcement, error codes | security, ssrf, guards, dns-pinning, ip-validation, domain, backend |

---

## API

| File | Summary | Tags |
|---|---|---|
| [api/post-fetch.md](api/post-fetch.md) | API reference for the POST /fetch endpoint | api, http, fetch, endpoint |
| [api/get-fetch.md](api/get-fetch.md) | API reference for the GET /fetch endpoint — query params, validation, URL-length note | api, http, fetch, endpoint, get |

---

## Security

| File | Summary | Tags |
|---|---|---|
| [security-report.md](security-report.md) | Full threat model and remediation list for the fetch pipeline | security, threat-model |

---
