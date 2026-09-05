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
- `AiController.ExecuteAction` now records every action attempt (success or
  denial) via `IAuditLogger` — actor display name, tool name, and the
  outcome summary. The actor's user id isn't populated yet (`actorUserId:
  null`) pending a standardized user-id claim across Identity.
