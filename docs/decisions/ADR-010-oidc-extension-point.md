# ADR-010: OAuth/OIDC as an executable extension point, not a half-flow

**Status:** Accepted — implemented. The configuration contract shipped first
(with the handler deliberately unregistered); the handler itself was registered
afterwards, with the provisioning policy this ADR required. See the update at the
end of this document.

## Context

The platform brief lists OAuth/OIDC federation under security, and allows a
"planned, but marked" treatment. The dangerous middle ground is a connector
that *looks* implemented: a sign-in button that redirects to a provider, an
identity that arrives with claims nobody maps, and an account that exists in the
provider but not in ATLAS — or worse, an account auto-created with a guessed
tenant and role. That would be functionality that appears to work while
silently bypassing the `org_id` + seven-role model every authorization decision
depends on (see ADR-009).

At the same time, "documented as planned" is not enough if configuration is
ignored: an operator who sets `Oidc:ClientId` and sees no error, no warning and
no working sign-in has been misled by the platform.

## Decision

1. **Bind and validate the configuration contract now.**
   `Atlas.Shared.Security.OidcOptions` (`Oidc` section) is bound in
   `Program.cs` and validated before the host is built:
   - absent → valid; ATLAS runs on cookie + hashed API keys;
   - partially filled (for example `ClientId` without `Authority`, or scopes
     without `openid`) → startup **fails** with a message naming the missing
     setting;
   - complete → the host logs a warning that no `OpenIdConnect` handler is
     registered, so external sign-in remains disabled.
2. **Do not register the handler until provisioning is specified.** The
   `Microsoft.AspNetCore.Authentication.OpenIdConnect` package and the
   `AddOpenIdConnect` call are documented verbatim in
   `docs/security.md#oauthoidc-extension-point`; adding them is a small,
   reviewable change. What must accompany them is a provisioning policy:
   which claim identifies the ATLAS user, whether unknown identities are
   rejected or provisioned, which role and `org_id` they receive, and a
   tenant-scoping test that proves one organization cannot enter another.
   Those are product/security decisions, not wiring.
3. **Keep the seam visible in three places**: `.env.example` (commented
   environment variables), `appsettings.json` (empty section), and the startup
   validation path above. Unit tests (`OidcOptionsTests`) pin the
   absent/partial/complete behaviour so the contract cannot rot.

## Consequences

- A security reviewer sees exactly what is and is not implemented; no claim in
  the docs outruns the code (`README.md`, `docs/security.md`).
- A half-configured provider cannot run unnoticed, and a fully configured one
  produces a loud, actionable log line rather than silent inaction.
- Operators who need federation must complete the documented registration
  *and* decide the provisioning policy — the platform refuses to make that
  decision implicitly.
- Remaining gap, stated plainly: ATLAS does not authenticate against an external
  identity provider today. Enterprise SSO requires the follow-up above.

## Update — 2026-10-05: the extension point became a handler

The follow-up this ADR called for is implemented, with the provisioning decision
it demanded made explicit and safe:

- `IdentityOidcExtension.AddAtlasOidc` registers the handler when `OidcOptions`
  is complete (scheme `oidc`, authorization code + PKCE, `SaveTokens = false`),
  and `GET /account/oidc` is the entry point; the login page offers the button
  only when the handler is registered, and the endpoint answers `404` otherwise.
- The provisioning policy is **none for unknown identities**: the external
  principal is resolved to an existing, active local account by verified e-mail
  claim (`ExternalIdentityMapper`), roles/organization/security stamp always come
  from this database, and the local principal *replaces* the external one before
  the cookie is issued — so session revocation (`IdentitySessionValidation`) and
  the authorization pipeline keep working unchanged.
- Partial configuration still fails startup, exactly as required below.

The decision recorded here (no silent, half-configured federation) is unchanged;
federation is now real rather than marked. See `docs/security.md` for the
operating detail and `ExternalIdentityMapperTests` for the pinned rules.

## Alternatives considered

- **Register the handler behind the configuration flag without provisioning.**
  Rejected: an authenticated external identity with no mapped role/organization
  is either a broken login (rejected silently) or an unauthorized account
  (accepted mistakenly) — both are worse than a documented absence.
- **Document the gap without any code.** Rejected: it recreates the original
  problem (a configuration section that does nothing) and makes the security
  posture depend on someone reading the docs before the config.
