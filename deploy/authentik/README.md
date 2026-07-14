# Sideport Authentik plan

This directory is a reviewed **plan artifact**. Do not apply it automatically.

The blueprint creates one invitation-only enrollment flow. A new user provides
basic account details, Authentik writes an external user, then Authentik—not
Sideport—requires WebAuthn/passkey setup before logging the user in. The stage
allows platform, synced/hybrid, and security-key authenticators and requires
user verification plus a discoverable credential.

Deployment inputs that must remain in the secret/configuration systems:

- `Sideport__Identity__Mode=oidc`
- `Sideport__Oidc__Authority`
- `Sideport__Oidc__ClientId`
- `Sideport__Oidc__ClientSecret`
- `Sideport__Oidc__ProviderId=authentik`
- `Sideport__Oidc__ProviderLabel=<plain-language provider name>`
- `Sideport__Oidc__LoginLabel=<existing-account button label>`
- `Sideport__Authentik__BaseUrl`
- `Sideport__Authentik__ApiToken`
- `Sideport__Authentik__EnrollmentFlowSlug=sideport-enrollment`
- `Sideport__Authentik__EnrollmentFlowId=<flow UUID from Authentik>`
- exact `Sideport__ReverseProxy__KnownProxies` or `KnownNetworks`
- `Sideport__PublicOrigin=https://sideport.example/`

The Authentik OAuth2/OIDC provider redirect URI must be exactly
`https://sideport.example/signin-oidc`, with the matching post-logout URI.
The Sideport service should receive traffic only through the configured Traefik
proxy/network; direct forwarded headers are ignored.

The API token should be scoped only to adding and viewing invitations. Sideport
binds each invitation to the configured enrollment flow and does not need user,
group, flow-edit, or invitation-delete permission. It is never added to the
blueprint, Kubernetes example secret, browser bundle, workspace store, or logs.
