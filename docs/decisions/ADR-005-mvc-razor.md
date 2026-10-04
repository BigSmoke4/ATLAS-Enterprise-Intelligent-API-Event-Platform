# ADR-005: ASP.NET Core MVC + Razor instead of a SPA

**Status:** Accepted — implemented.

## Context
The primary users are operators in a control room: dense tables, live readouts,
a topology map, explicit refresh and confirmation on destructive operations.
The platform also must stay debuggable by backend engineers and work without a
second API contract built purely to feed a browser application.

## Decision
ASP.NET Core MVC with Razor views, page-specific view models, thin controllers,
and **vanilla ES modules** for interactivity — no React/Angular/Vue/Blazor.

- Each console page is server-rendered from a typed view model, so the first
  paint contains real data (and the honest empty state when there is none).
- Client behaviour is one ES module per page (`wwwroot/js/pages/*.js`), imported
  lazily by `site.js` from `data-page`; shared behaviour lives in
  `wwwroot/js/components/*` (charts, gauges, topology, tables, command bar).
- Writes go through the same JSON APIs the platform exposes to machines, with an
  antiforgery token issued into a meta tag.
- SignalR is used only where genuinely push-shaped (incident updates), and the
  browser degrades to bounded polling when the client script cannot load.

## Consequences
- One rendering pipeline, one place where a page's data shape is defined, and no
  serialized hydration contract to keep in sync with a client router.
- Rich interaction must be written, not imported: the page modules are small and
  explicit, and CI parses every module (`node --check`) because there is no
  bundler to catch a syntax error.
- Reusing the console elsewhere (mobile app, third party) means using the JSON
  API surface, which is already versioned under `/api/v1`.

## Alternatives considered
- **SPA (React/Angular/Vue)** — rejected: a second contract, a client build
  pipeline, and client-side state for data the server already knows how to
  render.
- **Blazor** — rejected: the requirement explicitly excludes it, and the
  interactivity needed here is DOM-shaped, not component-tree-shaped.
