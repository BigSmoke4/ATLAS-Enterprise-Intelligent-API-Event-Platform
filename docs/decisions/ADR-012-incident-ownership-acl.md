# ADR-012: Per-incident ownership on top of role and tenant

**Status:** Accepted — implemented.

## Context

Authorization had two layers: tenant scope (query filters + the
`SameOrganization` policy + explicit body checks) and role policies (e.g.
`Role:SRE` for incident writes). Neither answers "may this caller change *this*
incident". With only those layers, any SRE could transition, annotate or write a
postmortem for any incident in their organization — including one another SRE
declared, or one created automatically from an alert. That is broader than the
platform's own security posture claims, and it is the kind of gap a reviewer
finds by reading the write endpoints rather than the docs.

## Decision

Add the smallest per-record rule that is defensible during an incident review.

- `Incident.DeclaredByUserId` records who declared the incident (`Guid?`, nullable
  because automated paths — the alert sink — declare incidents without a user).
- `IncidentAccessPolicy.CanAct(incident, actorUserId, actorIsPrivileged)`:
  - `PlatformAdmin` / `OrganizationAdmin` may act on any incident in scope —
    someone must be able to take over when the declarer is unavailable;
  - an `SRE` may act on incidents they declared;
  - an incident with **no declarer on record** is closed to a plain `SRE`: the
    rule fails closed and the privileged roles remain the escape hatch.
- The policy is enforced in `IncidentsController` before the state machine runs
  (`transition`, `root-cause`, `postmortem`), returning `403` with a
  ProblemDetails body whose `detail` names who can act
  (`IncidentAccessPolicy.ExplainDenial`), and the declarer is stamped from the
  authenticated principal (`ClaimTypes.NameIdentifier`) on declare.
- The decision half is a pure static class with its own unit tests
  (`IncidentAccessPolicyTests`), so the rule is verifiable without HTTP or a
  database.

## Consequences

- The write surface is now per-record for incidents, and the DTO carries
  `DeclaredByUserId` so the console and API clients can show who owns an
  incident rather than guessing.
- Automated incidents (alert-sourced, no declarer) can only be changed by the
  privileged roles. That is an intentional operational cost: an SRE must either
  be granted a privileged role or the incident must be re-declared with
  attribution. It is stated here and in `docs/security.md` rather than hidden in
  a "system" user that would defeat the audit trail.
- The other aggregates still authorize by role + tenant. Each needs its own
  ownership semantics (an API route's owner is not an incident's declarer), so
  this ADR deliberately does not generalize the rule; it establishes the pattern
  — column, pure policy, controller enforcement, unit tests.
- A future migration to true ownership transfer (assign/reassign) would extend
  this policy rather than replace it; no such endpoint exists today.

## Alternatives considered

- **Keep role-only authorization and document it.** Rejected: it leaves an
  obvious over-permission in the write path, and the docs would have to claim a
  granularity the code does not enforce.
- **Make every incident writable by any SRE (co-operative incident response).**
  Rejected as the *default*: it is a legitimate operating model, but choosing it
  silently is what the previous state did. Privileged roles can still do it,
  which makes it an explicit choice.
- **Auto-provision a "system" declarer for alert-sourced incidents.** Rejected:
  it would make automated incidents writable by every SRE while attributing them
  to an identity that cannot act, muddying the audit trail for a marginal
  convenience.
- **Row-level security in PostgreSQL.** Rejected for this step: the application
  is the only writer, the policy must also be visible to the UI and the audit
  trail, and a second enforcement point with its own model would need its own
  integration suite to be trustworthy. Revisit if a direct SQL path is ever
  introduced.
