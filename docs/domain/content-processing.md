**Scope**: `Safetch.Core/Processing/`
**Tags**: domain, content-processing, pipeline, prompt-injection, html-sanitisation, unicode, spotlighting
**Summary**: The content processor pipeline — design, processor registration, affinity filtering, and the five built-in processors.
**See Also**: fetch-service.md, security-pipeline.md, ../security-report.md

## Design

The pipeline is composable and ordered. Each processor:
- Implements `IContentProcessor` — one method: `ProcessAsync(string content, ProcessingContext ctx, CancellationToken ct) → ProcessorResult`
- Returns the (possibly modified) content and a list of warnings
- Is registered with a **content-type affinity** and an **order** at DI registration time — not on the interface

`ContentProcessorPipeline` runs processors in ascending order. A processor only runs if its affinity matches `ProcessingContext.MimeType` exactly, or its affinity is `"*"` (runs for all types).

## Key types

```csharp
record ProcessingContext(string MimeType, string SourceUrl);
// MimeType is the parsed MIME type only — e.g. "text/html", never "text/html; charset=utf-8"

record ProcessorResult(
    string Content,
    IReadOnlyList<string> Warnings,                    // backward-compat flat strings
    IReadOnlyList<InjectionWarning> InjectionWarnings  // structured, per-match
);

public enum InjectionSeverity { Informational, Medium, High }

record InjectionWarning(string Category, string PatternMatched, InjectionSeverity Severity);

record OrderedProcessor(int Order, string ContentTypeAffinity, IContentProcessor Processor);
```

## Registration

```csharp
services.AddContentProcessor<T>(contentType: "text/html", order: 1);
// Registers T as IContentProcessor and as OrderedProcessor with the given affinity and order
```

## Built-in processors

| Order | Name | Affinity | Behaviour |
|---|---|---|---|
| 1 | `ReadableContent` | `text/html+readable`, `text/html+text`, `text/html+markdown` | Extracts primary article body via SmartReader (Mozilla Readability). `readable` returns clean HTML; `text` strips tags to plain text; `markdown` passes extracted HTML to downstream processors. Falls back with warning if `IsReadable = false`. |
| 1 | `HtmlSanitizer` | `text/html`, `text/html+markdown` | Removes CSS-hidden elements, `data-*` attributes, `<svg>`, `<meta http-equiv>` using HtmlAgilityPack |
| 2 | `HtmlToMarkdown` | `text/html`, `text/html+markdown` | Converts HTML to Markdown using ReverseMarkdown |
| 3 | `UnicodeTagStrip` | `*` | Removes Unicode Tags block characters (U+E0000–U+E007F) via surrogate-pair regex; emits warning if found |
| 4 | `InjectionPattern` | `*` | Scans for known prompt injection phrases; **detection only** — does not modify content; emits one warning per match |
| 5 | `Spotlighting` | `*` | Wraps final content in `[BEGIN UNTRUSTED EXTERNAL CONTENT ...]` / `[END UNTRUSTED EXTERNAL CONTENT]` boundary markers |

## Response mode MIME affinity convention

`FetchRequest.Mode` influences content processing by adjusting `ProcessingContext.MimeType` before the pipeline runs (in `FetchService`):

| Mode | Effective MimeType (for HTML pages) |
|---|---|
| `Raw` | `text/html` (unchanged) |
| `Readable` | `text/html+readable` |
| `Text` | `text/html+text` |
| `Markdown` | `text/html+markdown` |

This keeps `ProcessingContext` signature stable and lets affinity filtering route modes to the correct processor without any interface changes. `HtmlSanitizer` and `HtmlToMarkdown` have affinity `text/html` and are **skipped** for readable/text modes (SmartReader does its own sanitisation). Processors with affinity `"*"` (`UnicodeTagStrip`, `InjectionPattern`, `Spotlighting`) run regardless of mode.

## CSS-invisible element matching (HtmlSanitizer)

Style attribute values are normalised before comparison: whitespace around `:` and `;` is stripped, then lowercased. This ensures `opacity: 0`, `OPACITY:0`, and `opacity:0` all match.

Detected patterns: `opacity:0`, `display:none`, `visibility:hidden`, `color:white`, `color:#fff`, `color:#ffffff`, `width:0`, `height:0`, `font-size:0`.

## Injection pattern detection

Detection-only by design. Removal is easily bypassed by split-word attacks and can corrupt legitimate content. Callers receive warnings and decide how to handle them. The pattern list is a best-effort baseline — not a complete defence.

`InjectionPatternProcessor` emits one `InjectionWarning` per pattern match, covering 8 categories:

| Category | Severity | Description |
|---|---|---|
| `InstructionOverride` | Medium | Classic "ignore previous instructions" phrasing |
| `PersonaHijacking` | Medium | "act as", "you are now", "new persona" |
| `ModelFormatMarker` | Informational | Tokenizer markers from open-source models (`[INST]`, `<|im_start|>`, etc.) |
| `DataExfiltration` | High | Phrases directing data to an attacker URL |
| `ToolCallCoercion` | High | Phrases instructing the agent to invoke tools directly |
| `AuthorityOverride` | High | False system-level authority assertions |
| `MemoryPoisoning` | High | Phrases designed to write persistent instructions into AI memory (MITRE AML.T0080.000) |
| `JailbreakFraming` | Medium | Well-known jailbreak triggers (DAN, god mode, developer mode, etc.) |

Structured warnings are surfaced via `FetchResponse.InjectionWarnings` (`IReadOnlyList<InjectionWarning>`). The flat `FetchResponse.Warnings` string list is also populated for backward compatibility.

**Arms-race limitation**: Static pattern matching raises the bar but cannot close the gap against adaptive or encoded attacks. Treat warnings as signals to scrutinise, not proof of safety when absent.

## Warnings contract

`FetchResponse.Warnings` accumulates all warnings from all processors as flat strings. It is always present in the response as an array — never omitted, even when empty (`[]`). `FetchResponse.InjectionWarnings` carries the structured injection warnings separately. Callers (e.g. `WebFetchTool`) should log or surface both for scrutiny.

## Unicode Tag surrogate encoding

U+E0000–U+E007F are supplementary plane characters that cannot be expressed as `\uXXXX` in .NET regex. The correct pattern uses surrogate pairs: lead `\uDB40`, trail `\uDC00–\uDC7F`.
