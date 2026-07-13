# Runtime observer

No production mutation or runtime claim is part of Phase 1. Existing production
state was not changed while repository gates ran. Phase 2 will record the
immutable image digest, GitOps revision, health/readiness, authentication-options
projection, and invitation-flow observations without storing invitation tokens,
OIDC subjects, API tokens, or provider secrets.
