# AI Operations Architecture

## Implemented

The flow User → AI → tool calls → real ATLAS data → evidence → answer is
real for both the read and (one) action path:

- `IAtlasTool` — every tool declares a `ToolKind` (`Read` or `Action`).
- Read tools: `GetServiceHealthTool`, `GetIncidentHistoryTool`,
  `GetSLOStatusTool` — real, backed by `IServiceHealthService`,
  `IIncidentService`, `ISloService` respectively. None fabricate data.
- Action tool: `DeactivatePolicyTool` — genuinely deactivates a
  PolicyEngine rule via `IPolicyManagementService.DeactivateAsync`.
- `AiOperationsAssistant.AnswerAsync` — calls requested read tools, collects
  `Evidence` only from successful results, returns
  `AiAnswer.InsufficientEvidence()` if none produced usable evidence. Every
  `AiAnswer` carries its `Citations`.
- `AiOperationsAssistant.ExecuteActionAsync` — the **only** path that can
  run an Action-kind tool. Every call passes through `ActionToolGuard`
  first: unauthorized or unconfirmed calls are denied and the tool is never
  invoked (unit-tested for both denial paths and the success path).
- `AnthropicCompletionClient` — a real HTTP integration with the Anthropic
  Messages API (genuinely calls out over the network, parses the response),
  registered only when `AI:AnthropicApiKey` is configured. Its prompt
  instructs the model to answer only from supplied evidence and say what's
  missing otherwise — mirroring `AnswerAsync`'s own rule at the
  prose-generation layer.
- `AiController`: `/api/v1/ai/ask` (evidence-gathering, any authenticated
  user) and `/api/v1/ai/actions` (action execution, `Role:PlatformAdmin` +
  `explicitConfirmation: true` in the body — the controller cannot bypass
  `ActionToolGuard` even if it wanted to, since the guard re-checks inside
  `ExecuteActionAsync`).

## Not yet implemented

- Without `AI:AnthropicApiKey` configured, `AnswerAsync` falls back to a
  deterministic evidence-only summary rather than fluent prose — this is a
  graceful degradation, not a crash, but it's not "real" natural-language
  output.
- Only one Action tool exists (`DeactivatePolicy`). Others described
  conceptually in the master prompt (restart an instance, roll back a
  deployment) aren't built.
- `AiController.ExecuteAction` records every action attempt (success or denial)
  through `IAuditLogger` with the actor's display name, the tool name and the
  outcome summary. The actor **user id** is still written as `null`: the
  controller reads `User.FindFirst(ClaimTypes.NameIdentifier)`, and the cookie
  pipeline's claim set is not yet guaranteed to carry it for every principal
  (API-key principals carry a different id claim). Until that claim is
  standardised across Identity, the audit record identifies the actor by name
  and by the request's correlation id rather than by user id — deliberately
  visible rather than silently wrong.
