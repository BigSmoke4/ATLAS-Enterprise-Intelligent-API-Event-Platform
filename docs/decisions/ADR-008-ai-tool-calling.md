# ADR-008: AI answers come from typed tools over ATLAS data

**Status:** Accepted — implemented.

## Context
An "AI operations assistant" is easy to demo and easy to get wrong: a language
model asked to comment on platform state will happily invent plausible numbers,
and a model allowed to call arbitrary functions becomes a privilege-escalation
path with a chat interface.

## Decision
The assistant is a **tool-calling loop over typed tools**, not a chat window
over a prompt.

- Tools implement `IAtlasTool` (`Name`, `Description`, `Kind`, `InvokeAsync`)
  and are implemented in `AIOperations` against other modules' *Application*
  services: `GetServiceHealth`, `GetIncidentHistory`, `GetSloStatus`,
  `GetRootCauseAnalysis`, and the action-kind `DeactivatePolicy`.
- Tool results are the only grounding the answer may use. Every answer cites the
  tools it consulted; when the tools return nothing usable the answer is
  **"Insufficient evidence."** — the same refusal path is asserted by unit tests
  and exported as `atlas.ai.answers.insufficient_evidence`.
- **Read and action tools are separated.** Read tools are tenant-checked and
  available to the roles allowed to read that data; action tools additionally
  require `Role:PlatformAdmin`, an explicit `ExplicitConfirmation: true` in the
  request, and produce an append-only audit record. The guard lives in the
  application layer (`ActionToolGuard`), so it applies even if a future UI
  forgets it.
- The LLM provider is optional: without `AI:AnthropicApiKey` the assistant
  composes its answer deterministically from the same tool results, so the
  evidence/refusal behaviour is identical and testable in CI.

## Consequences
- Prompt injection cannot reach data the calling principal could not read, and it
  cannot trigger a side effect without an authorized, confirmed, audited call.
- Adding a capability means adding a tool with a contract and a test, not
  editing a prompt.
- The assistant is only as good as the tools; that is the intended limit.

## Alternatives considered
- **Free-text grounding (paste dashboards into the prompt)** — rejected: it
  invites fabrication and gives no evidence trail.
- **Letting the model call HTTP endpoints directly** — rejected: authorization
  would move into the prompt instead of the application layer.
