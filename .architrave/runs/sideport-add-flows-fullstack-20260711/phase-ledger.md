# Phase Ledger

## Repair-first rules

- This file is the canonical phase ledger for the current branch. Do not create
  a second implementation ledger for the shell, family access, containers, or
  deployment.
- Keep exactly one phase `in-progress`. Do not begin the next phase until the
  current gate is recorded as passing here.
- No family principal may be admitted to Sideport until Phases 8–10 establish
  durable membership and server-enforced authorization. The current behavior
  makes every authenticated OIDC principal owner-equivalent.
- No image publication or homelab change may begin until Phases 7–15 pass. The
  live `0.1.12` deployment remains the rollback baseline.
- Storybook is the UI approval boundary. Net-new or consolidated UI is mocked,
  reviewed at desktop and mobile sizes, and confirmed before runtime binding.
- Infrastructure remains plan-only. A human merge/reconcile is the homelab
  apply boundary; never apply the repo-local generic Kubernetes example.

| Phase | Status | Scope | Out of scope | Gate |
| ---: | --- | --- | --- | --- |
| 1. Approved mockups | completed | First-run fixes, global Add menu, signed-in Add iPhone/Add app dialogs, source/private-permission stories | Live endpoint binding | UI build/tests, responsive QA, semantic PASS |
| 2. Branch and contract delta | completed | Create requested branch, reconcile contract/file map/security rules | Runtime behavior | Clean ownership check, contract review |
| 3. Explicit device enrollment | completed | Non-pairing reads, durable bounded pair/trust/accept operation, accepted inventory | MDM, Wi-Fi first pairing | Device/API idempotency/recovery tests, semantic PASS |
| 4. Managed app imports | completed | Upload replay, rooted server import, managed copy, safe DTO | Remote sources | Catalog security/rollback tests |
| 5. GitHub releases | completed | Public/private selected-repo discovery/import, ephemeral credentials, redirect/SSRF/size controls | Arbitrary URLs/branches/actions artifacts | Auth/import/redaction/security tests, semantic PASS |
| 6. Runtime UI binding | completed | API clients, polling/resume, live capability gates, managed first Apple credential, persisted team choice, empty/context actions | Signer revocation automation or navigation rewrite | API/UI security tests, Storybook/Playwright/a11y/reconcile, semantic PASS |
| 7. Canonical product-shell mock | completed | Storybook-only replacement for the rejected operator console: setup outside the signed-in shell; exactly Home, Apps, Devices, Family, Activity, Settings; role-aware navigation; global Add/Search; one canonical first-run and add-device assistant; plain-language default with technical disclosure | Runtime routing/binding, family backend, deletion of production components, deployment | Human visual confirmation; desktop/mobile/keyboard/a11y checks; semantic PASS; approved deletion map |
| 8. Family security contract | completed | Contract and threat model for explicit owner bootstrap, durable members/invitations, immutable OIDC issuer+subject identity, owner/family capabilities, per-resource ownership, suspension/offboarding, last-owner safety, bearer recovery, antiforgery, audit/redaction, idempotency and migration/rollback; Storybook-only security deltas for short-lived Owner claim, explicit post-login invitation consent, and Authentik-owned recovery | Runtime implementation, Authentik mutation, Apple login, CalDAV | Updated contract and authorization matrix; security tournament/review; focused Storybook desktop/mobile/a11y checks; human sign-off |
| 9. Server-enforced family authorization | completed | Implement member/invitation stores; reject unknown OIDC principals; bind one-time invitations; enforce every read/mutation by capability and resource owner; replace free-text device owner with stable member reference; protect all cookie mutations; immediate suspension; owner recovery | Passkey credential lifecycle, Authentik UI/flows, email delivery | Invite replay/expiry/revocation, CSRF, privilege-escalation, cross-member isolation, last-owner, suspension and migration tests; full backend gate; semantic PASS |
| 10. Authentik passkey enrollment and proxy trust | completed | Sideport-specific Authentik enrollment/authentication flow; single-use identity invitation handoff; WebAuthn/passkey setup and validation for iCloud Keychain, Touch ID, Face ID, Windows Hello and password managers; recovery; Sideport branding; scoped adapter; trusted Traefik proxy/network configuration; first release copy/share link | CalDAV identity proof, official Sign in with Apple without eligible paid configuration, SMTP delivery, changing shared Microsoft flows | Blueprint/API plan review; no secret materialization; HTTPS OIDC callback and secure-cookie proof; invite/passkey/recovery acceptance; homelab plan/policy PASS; human apply boundary recorded |
| 11. Canonical runtime shell and deletion pass | completed | Bind the approved six-destination shell; route completed setup to Home; hide owner-only surfaces from family; merge Renewals into Home/Apps, Operations+Diagnostics into Activity, Apple Access+Teams into Settings/Signing, and Users into Family; remove legacy onboarding, rejected prototype, dead routes, fictional roles, stale stories and screenshot assertions; collapse API/base-path/signer/usbmux/OTLP details | New device transport behavior, app-source backend expansion, deployment | No permanent Onboarding route; no unreachable/dead UI; role-aware route tests; UI build, Storybook, desktop/mobile/keyboard/a11y/reconcile; semantic PASS |
| 12. Owner signing and team maintenance | completed | Owner-only Settings/Signing flow to reauthenticate the active Apple account, select among teams returned by Apple, and deliberately replace the single active account/team after an exact impact preflight over registrations, certificates and profiles; preserve credential redaction, fresh-auth/2FA requirements, lineage, recovery and audit | Multiple simultaneous signers, per-member signers, arbitrary Team ID entry, implicit certificate revocation or destructive cutover | Contract delta and impact review; owner/family authorization; stale auth/2FA/team mismatch/replay/recovery tests; no unintended certificate mutation; UI/backend gates; semantic PASS |
| 13. App library and one-cable journey | completed | Keep one assistant open through USB detection, Trust, automatic acceptance, Developer Mode/restart/reconnect guidance, approved app choice, one-action preflight/sign/install/device verification, automatic refresh, best-effort browser chime, and explicit `Installed — you can unplug`; separate importing from installing; search catalog apps; support manual IPA upload, browsable managed/server artifacts, public GitHub releases, and private selected-repository import with exact read-only permission handling; return real app icons/name/version/source; role-aware global Add/Search | Claiming Developer Mode, profile trust, app launch, or audio delivery as device-verified; arbitrary URLs; broad GitHub `repo` tokens; MDM; remote USB companion | No chime/unplug state before verified receipt; reload/recovery tests; upload/server/public/private-GitHub permission and import acceptance; app-source/search/icon contract tests; owner/family authorization tests; UI/backend deterministic gates; semantic PASS |
| 14. Device transport and physical acceptance | completed | Repair issue #3 class failures: bounded Wi-Fi bulk transfer, cancellation/timeouts, lease release after stalls, honest reconciliation, USB preference/fallback, and verification semantics; validate node pinning, socket permissions and pairing-record custody; physically prove first USB pair/install and later paired-Wi-Fi refresh | Wi-Fi first pairing, automatic remote cable switching, MDM, unverifiable launch claims | Physical iPhone matrix records USB pair/install/readback, restart/reconnect, paired-Wi-Fi success/failure, bounded timeout, released single-flight lock and USB recovery; no secret output; semantic/ops PASS |
| 15. Fresh-install Docker and Apple Container paths | in-progress | Bootable clean Docker/Compose setup and experimental official Apple Container launcher; clear state/work/anisette/usbmux/auth/proxy requirements; durable volumes, Rosetta/network notes, failure remediation in setup UI, startup/upgrade/rollback docs; run packaged onboarding through server, signer and catalog readiness; prove state survives restart/upgrade; prove physical usbmux pair/install on each claimed supported host or explicitly block and label unsupported device-install capability | Native Windows/macOS desktop app, native arm64 claim without evidence, live infrastructure apply | Clean-volume end-to-end onboarding; restart/upgrade persistence; Docker and Apple Container plan/startup checks; claimed-host usbmux/physical acceptance or explicit unsupported result; docs/UI consistency; IaC render/policy; secrets scan; semantic PASS |
| 16. Immutable release candidate and homelab handoff | not-started | Re-run every gate; establish traceable commit; publish immutable RC image; prepare only the clean homelab GitOps digest pin plus required trusted-proxy/Authentik plan; validate Flux and rollback; verify live state only after human merge/reconcile | Agent-applied GitOps, second signer, state reset, generic repo K8s apply, stable-release claim | Phases 7–15 green; immutable digest/SBOM/provenance; homelab PR checks; human merge recorded; live 2/2 containers, health/ready/OIDC/UI/USB smoke and rollback evidence |
| 17. Final evidence and cleanup | not-started | Record deterministic and semantic results, physical evidence, residual limitations, rollback/handoff, worktree ownership, and accepted follow-ups; reconcile canonical docs and learning artifacts | New features or deferred non-goals | Full Architrave checks/reconcile/backend checks; independent Copilot/GPT, Claude-family and Codex PASS; no stale ledger claims; user handoff |

## Explicit non-goals for this repair run

- Do not build CalDAV or app-specific-password login in Sideport. It is not
  Sign in with Apple and does not return a signed, stable Apple identity.
- Do not reuse GrandSlam, anisette, Developer Services, or the owner signing
  credential as family authentication.
- Do not build per-member Apple signers or a multi-signer/team architecture.
  This run keeps one owner-managed active signer and truthfully represents one
  selected Apple team.
- Do not automate certificate revocation or replacement without a separately
  approved exact-impact contract.
- Do not claim Sideport can verify Developer Mode, developer-profile trust,
  successful app launch, browser-audio delivery, or Wi-Fi transfer success
  without the corresponding evidence.
- Do not add SMTP, directory sync, MDM, a desktop USB relay, or native desktop
  applications to make the first family release appear complete.

## Phase 14 current evidence

- Repository transport repair is implemented and deterministically green:
  owned AFC/installation-proxy sockets close on cancellation; any deadline
  remains `install-outcome-unknown`; a confirmed terminated task releases the
  process-wide lease; a still-live task keeps the lease; no automatic retry was
  introduced.
- Focused tests pass: Devices 65/65 and Orchestrator 55/55. Full backend checks
  pass with API 479/479, Developer API 102/102, GrandSlam 50/50, zero build
  warnings/errors, valid Kubernetes 6/6, and secret scan PASS. Full UI checks
  pass 86/86; reconcile transparently skips because token build is not wired.
- Independent Copilot Architrave adversarial review returned PASS with no
  findings. The Claude launcher is unavailable (`No connected db`) and no
  verdict is claimed from it.
- Homelab audit: live `0.1.12` is healthy `1/1` with `2/2` pod containers on
  node `home`; usbmux socket, read-only pairing records, and durable state PVC
  are mounted. There is no explicit node selector. After the owner connected
  the phone, USB pairing validation, identity reads, bounded CertClock refresh,
  100-percent install progress, and authoritative USB bundle/version readback
  passed. After USB was unplugged, Sideport continued to discover the phone over
  its merged netmuxd socket; bounded Wi-Fi CertClock refresh completed in about
  four seconds with 100-percent install progress, and the managed device read
  returned the bundle as CertClock 0.1.0. Normal USB and paired-Wi-Fi success
  paths are physically proven. Only the repaired-release forced-stall,
  lease-release, reconciliation, and USB recovery matrix remains open.
- Phase 14 remains `in-progress`; Phase 15 remains `not-started`. No image,
  deployment, restart, secret read, cluster mutation, commit, or push occurred.

## Transition log

- 2026-07-11: Phase 1 started after explicit user authorization. Only UI and
  run-artifact files may change until mockups pass and the requested branch is
  created. Backend, API, and deployment phases remain not-started.
- 2026-07-11: Phase 1 completed. The onboarding judge and signed-in Add-flow
  judge returned PASS; Architrave checks passed 119/119 stories, the production
  Storybook build passed, reconcile skipped transparently, and desktop/390px
  browser QA passed. The requested branch `codex/apple-like-add-flows` was
  created with the pre-existing dirty tree preserved. Phase 2 is now the sole
  in-progress phase; runtime code remains not-started until its contract delta
  is reviewed.
- 2026-07-11: Phase 2 completed after independent review drove the GitHub App
  callback/state, exact-permission, provider-capability, V2 path-free catalog,
  V2 upload/import, and configured-root contracts to PASS. Phase 3 is now the
  sole in-progress phase. No deployment or infrastructure mutation occurred.
- 2026-07-11: Phase 3 completed after repairing first-pairing defects in the
  vendored transport, separating passive trust reads from the authenticated
  enrollment mutation, adding durable accepted inventory and recovery-safe
  retry, and making open mode read-only. The final solution gate passed 319/319
  tests and three independent reviews returned PASS. Physical USB/Wi-Fi
  acceptance remains explicitly deferred. Phase 4 is now the sole in-progress
  phase.
- 2026-07-11: Phase 4 completed with additive path-free V2 catalog endpoints,
  configured-root confinement, bounded IPA staging/inspection, immutable
  managed artifacts, optimistic versioning, durable replay protection, and
  rollback-safe publication. The full solution passed 344/344 tests and the
  independent adversarial review returned PASS with no release blockers.
  Phase 5 is now the sole in-progress phase.
- 2026-07-11: Phase 5 completed with public and selected-repository private
  GitHub sources, hashed single-use setup state, repository-scoped ephemeral
  credentials, redacted release discovery, pinned/manual redirect transport,
  bounded download and inspected managed import. The adversarial judge first
  found an IPv6 transition-address SSRF edge; the repair rejects transition and
  special address ranges and directly tests the production connect path.
  Concurrent exact imports now share one download. The full solution passed
  386/386 tests and the re-review returned PASS. Phase 6 is now the sole
  in-progress phase.
- 2026-07-11: Phase 6 audit found that a fresh deployment still sent the owner
  back to host configuration for the first Apple credential, while the approved
  UI promised in-context setup. Phase 6 therefore includes the smallest secure
  managed-credential and returned-team persistence needed to bind that screen
  truthfully. Certificate replacement remains explicitly gated; Sideport will
  not automate revocation without the separate exact-impact contract.
- 2026-07-12: Phase 6 first-install review found a crash window between marking
  the operation terminal and writing the immutable onboarding receipt. The sole
  in-progress slice is now the recovery boundary: persist verified bundle and
  expiry evidence, activate the registration, write the receipt, and only then
  mark the operation succeeded. Restart and exact replay must resume only this
  finalization and must never repeat signing or installation. UI and deployment
  files remain out of scope until this recovery gate passes.
- 2026-07-12: The owner added family invitations and member login as a required
  product outcome. This is now covered by Phases 8–10 and remains not-started
  until the Phase 7 product-shell gate passes. Sideport will keep authentication provider-neutral through
  OIDC, persist and enforce workspace membership by immutable issuer/subject,
  and expose Apple login only when Authentik has an official paid Sign in with
  Apple configuration. Apple Developer/GrandSlam credentials will never be
  reused as portal login. Per-member signing accounts remain a separate future
  architecture, not an implied part of invitations.
- 2026-07-12: The Phase 6 recovery boundary now compiles and its previously
  failing focused receipt/workflow integrations pass. Runtime UI binding may
  proceed inside Phase 6 while the full solution gate remains pending. An
  independent audit then found and assigned three required repairs before the
  phase can close: expose finalization retry instead of polling `waiting`
  forever, persist and bind exact catalog selection into preflight, and expose
  scheduler settings with operational prerequisites. The broken fresh Compose
  path is recorded under not-started Phase 15 rather than being mixed into this
  runtime slice.
- 2026-07-12: The owner explicitly requested a homelab deployment. A read-only
  audit found the live Flux-owned `default/sideport` healthy at release 0.1.12,
  one replica with `Recreate`, durable state intact, and an immutable rollback
  digest recorded. The release-candidate phase must publish a traceable immutable image and
  change only the homelab GitOps image pin from a clean worktree. The generic
  repo-local manifest must never be applied because it targets a second
  namespace/signer. Architrave remains plan-only for infrastructure: the agent
  may prepare and validate the homelab PR, while a human merge is the apply
  boundary before live rollout verification.
- 2026-07-12: Before any image was published or cluster state changed, the owner
  rejected the old signed-in information architecture: too many overlapping
  screens and onboarding presented as a permanent destination. Homelab rollout
  is therefore deferred to Phase 16. Phase 7 will first reproduce the existing
  components in a simplified Storybook shell with setup shown only before the
  durable completion receipt. After setup the primary destinations are Home,
  Apps (library/search), Devices, Family, Activity (operations/logs), and
  Settings (Apple signer/team). Device, app, team, and member addition remain
  available through contextual/global Add flows. Official Sign in with Apple
  remains capability-gated because the web provider requires paid Apple
  Developer Program configuration; free Personal Team signing stays separate.
- 2026-07-12: Phase 6 completed at the authorized boundary. The backend build
  passed with zero warnings/errors; 487 backend tests, 135 Storybook
  interaction/accessibility tests, and 20 desktop/mobile Playwright checks
  passed. Architrave backend/UI/reconcile gates passed, the plan-only Kubernetes
  render validated 6/6 resources, and independent backend, Copilot/GPT,
  Claude-family, and Codex semantic reviews returned PASS with zero Blockers or
  Majors. Physical USB/paired-Wi-Fi acceptance remains explicitly deferred and
  no deployment, apply, secret read, staging, commit, or push occurred. The run
  is paused at this phase boundary; Phase 7 remains not-started rather than
  being silently begun.
- 2026-07-12: A repair-first product/security audit rejected deployment of the
  current shell. It found permanent/default Onboarding, eleven overlapping
  destinations, three onboarding sources, fictional family administration,
  owner-equivalent access for every OIDC principal, a fragmented cable-to-app
  journey, no verified-completion chime/unplug cue, incomplete catalog search,
  unresolved Wi-Fi transfer stalls, missing trusted-proxy configuration for the
  new OIDC path, and unvalidated fresh Docker/Apple Container setup. The owner
  requested one ledger covering every repair before release. Phases 7–17 now
  encode that order; Phase 7 is the sole `in-progress` phase and is limited to
  the canonical Storybook mock and deletion map. Backend, runtime deletion,
  Authentik/homelab changes, image publication, deployment, and apply remain
  `not-started`. CalDAV login and per-member signers are explicit non-goals.
- 2026-07-12: Phase 7 implementation and deterministic review reached the
  visual-approval boundary. The canonical Storybook mock now has exactly six
  destinations, role-aware Add/Search, setup and invitations outside the
  shell, invitation failure/recovery states, app import sources, and one shared
  three-stage cable assistant. Focused canonical tests passed 19/19; the full
  Storybook suite passed 154/154; lint, build, Architrave checks, diff checking,
  and reconciliation passed (reconciliation skipped transparently because no
  token build is configured). Independent Codex review returned PASS with zero
  Blockers or Majors. The in-app browser binding was unavailable, so human
  desktop/mobile visual confirmation remains open and Phase 7 stays the sole
  `in-progress` phase; no Phase 8 or runtime/deletion work has begun.
- 2026-07-12: Phase 7 produced the canonical Storybook-only shell and shared
  family journey without changing runtime routes or backend behavior. The
  focused canonical suite passed 19/19 and the full UI gate passed 154/154;
  production and Storybook builds passed, responsive/keyboard/browser QA was
  recorded, and the independent adversarial UI review returned PASS. Phase 7
  remains the sole `in-progress` phase until the owner visually confirms the
  local Storybook preview. Phase 8 remains `not-started`.
- 2026-07-12: The in-app browser became available after the initial binding
  failure. Desktop and 390 px mobile previews were inspected and left open for
  owner review. The owner then explicitly directed Sideport to continue through
  implementation of the stated nontechnical family journey. Phase 7 is
  completed at its Storybook-only boundary, and Phase 8 is now the sole
  `in-progress` phase. Runtime implementation remains `not-started` until the
  Phase 8 contract, security review, and human sign-off pass.
- 2026-07-12: Phase 8 independent review found that carrying raw invitation
  authority through OIDC in JavaScript storage, automatically joining before
  confirming the actual signed-in account, pasting the long-lived recovery
  bearer into a browser, and claiming Sideport creates an Authentik passkey
  were not safe contract/UI boundaries. Phase 8 therefore includes only the
  corresponding Storybook security deltas, reusing the approved invitation
  layout: opaque HttpOnly handoff, explicit post-login consent, bearer-minted
  short-lived Owner claim, and provider-owned recovery wording. Runtime and
  Authentik implementation remain `not-started`.
- 2026-07-12: Phase 8 completed after three independent review passes and the
  bounded GPT judge returned PASS. The Claude-family judge first found ambiguous
  JSON handoff-body logging, untrusted OIDC display-claim handling, and missing
  public-shell CSP; the contract/threat model now specify exact bounded JSON
  bodies, pre-routing body-log suppression, normalized text-only identity
  presentation, and a strict self-only/no-referrer shell policy. Its second pass
  returned PASS with zero Blockers or Majors. The full UI gate passed 158/158,
  the backend gate passed 487/487 with zero build warnings/errors and 6/6 valid
  Kubernetes resources, reconciliation passed by transparent not-configured
  skip, and `git diff --check` passed. Desktop and 390 px invitation/Owner-claim
  entry and confirmation states had no horizontal overflow and a fresh console
  check was clean. The owner's instruction to continue through implementation
  records the human phase-boundary approval. Phase 9 is now the sole
  `in-progress` phase; Authentik mutation, deployment, and infrastructure apply
  remain not-started.
- 2026-07-12: During Phase 9, the owner approved broader public product language
  suitable for family, friends, and small workplaces: the navigation destination
  is now **People**, limited access is shown as **Member**, and the invitation
  action is **Invite someone you trust**. The canonical Storybook component,
  stories, controls, assertions, and UI design specification were updated without
  changing the internal backend `family` authorization identifier. The focused
  canonical suite passed 23/23, the UI production build passed, `git diff --check`
  passed, Storybook was restarted, and the updated Owner Home preview was opened.
  Phase 9 remains the sole `in-progress` phase; no runtime-shell binding,
  deployment, infrastructure mutation, commit, or push occurred.
- 2026-07-12: The owner clarified that Sideport is an ongoing mobile-first,
  multi-user app library and device service; onboarding is only the first-use
  state. The canonical mock now prioritizes app search/install/update, shows
  three people with separately owned iPhones, presents a chronological Activity
  feed with attention-first device/app/member events, and truthfully states that
  the host USB port is continuously monitored for trusted or new iPhones. The
  mobile shell uses a persistent five-item bottom navigation with Settings in
  the top bar. Mobbin research informed search-first discovery, compact one-action
  app rows, explicit device ownership, and time-grouped updates; Sideport tokens
  and Apple-like restraint remain authoritative. The production UI build and
  focused canonical Storybook suite passed 25/25, including accessibility.
  Phase 9 remains the sole `in-progress` phase; the fixture has not been passed
  off as live runtime behavior.

- 2026-07-12: Phase 9 completed. Recovery/offboarding replay now precedes new
  impact work; new mutations verify exact impact while the workspace mutation
  gate is held; canonical tokens, rate limits, history caps, expiry/PII
  tombstoning, and persisted graph validation are enforced; exact verified
  counts are audited; and lost-response HTTP replay is covered for offboarding
  and Owner recovery. The final backend gate passed with zero build warnings or
  errors: API 461/461, Orchestrator 53/53, Developer API 98/98, Devices 64/64,
  and GrandSlam 50/50; Kubernetes plan/policy validated 6/6 resources and the
  secret scan passed without apply. Independent GPT and Claude-family reviews
  returned PASS with zero Blockers or Majors. Phase 10 is now the sole
  `in-progress` phase. Runtime-shell binding, physical transport acceptance,
  deployment, infrastructure apply, commit, and push remain not-started.

- 2026-07-13: Phase 10 completed at the plan-only boundary. Sideport now has an
  optional retry-safe Authentik enrollment adapter gated by the opaque invitation
  handoff, safe authentication-options contract, invitation-only WebAuthn
  blueprint, and exact trusted-proxy/OIDC configuration examples. Backend checks
  passed with API 468/468 and all sibling suites green; Kubernetes plan/policy
  validated 6/6 resources; independent semantic review returned PASS with zero
  findings. No Authentik API mutation, secret materialization, cluster apply, or
  deployment occurred. Phase 11 is now the sole `in-progress` phase.

- 2026-07-13: Phase 11 completed. The live runtime now has exactly Home, Apps,
  Devices, People, Activity, and Settings; explicit incomplete setup renders
  outside that shell, while completed or absent onboarding status lands on
  Home. Member capability coverage proves `devices.enroll` can expose Add
  iPhone without catalog import or Apple signer controls. The rejected
  prototype, unreachable legacy onboarding and Teams pages, fictional roles,
  dead routes/CSS, and overlapping Storybook aliases were removed. The final
  gates passed: 84/84 Storybook interaction/accessibility tests, 14/14
  desktop/mobile runtime checks, backend API 468/468, Orchestrator 53/53,
  Developer API 98/98, Devices 64/64, GrandSlam 50/50, zero backend build
  warnings/errors, Kubernetes 6/6 valid, secret scan PASS, reconcile PASS by
  transparent not-configured skip, and `git diff --check` PASS. Independent
  Copilot/GPT semantic review returned PASS with zero Blockers or Concerns.
  Claude launcher failures occurred before repository inspection and were not
  counted as verdicts. No deployment, apply, secret read, commit, or push
  occurred. Phase 12 is now the sole `in-progress` phase; no Phase 12
  implementation has begun.

- 2026-07-13: Phase 12 mock approval was recorded and implementation began
  immediately. The canonical Settings/Signing flow and live active-account /
  returned-team cutover slice now use exact certificate impact, durable
  authorization, a shared signer-authority gate, atomic registration/team
  rebinding, and persisted-identity recovery without repeat revocation. Current
  gates are green (API 470, Orchestrator 54, Developer API 101, Devices 64,
  GrandSlam 50, Storybook 85, Playwright 14). An independent review found and
  drove repairs for a lock-scope race and post-persist retry window; a later
  launcher emitted no verdict and is not counted. Phase 12 remains the sole
  `in-progress` phase because different-account candidate credential/2FA and
  atomic credential+registration cutover are not implemented. No deployment,
  apply, secret read, commit, or push occurred.

- 2026-07-13: Phase 12 completed. Settings/Signing now supports fresh
  reauthentication, Apple-returned team selection, exact certificate/app/device/
  profile impact, same-account team migration, and different-account candidate
  credential/2FA cutover without exposing or durably staging plaintext
  credentials. Certificate replacement is exact-ID only and shares one signer
  authority gate with install/refresh. A durable authority journal rolls back
  when the old credential remains active and completes lineage when the
  replacement credential is active; persisted-identity recovery never repeats
  revocation or minting. The final gates passed: Storybook 85/85, Playwright
  14/14, API 480/480, Orchestrator 54/54, Developer API 101/101, Devices 64/64,
  GrandSlam 50/50, zero build warnings/errors, Kubernetes 6/6, secret scan and
  diff PASS. The final independent semantic review returned PASS with zero
  Blockers or Concerns. No real Apple operation, deployment, apply, secret read,
  commit, or push occurred. Phase 13 is now the sole `in-progress` phase; no
  Phase 13 implementation has begun.

- 2026-07-13: Phase 13 completed. A newly accepted iPhone continues directly
  into the approved app library; Developer Mode remains explicit guidance;
  Apps and global search prioritize ready catalog items; existing upload,
  configured-storage, public-GitHub, and selected-private-GitHub sources remain
  capability-bound; installed-phone metadata is never treated as an IPA
  source. The runtime renders trusted same-origin bounded PNG icons from
  managed IPAs with safe fallbacks. Standalone and first-run completion attempt
  a best-effort browser chime and show `Installed — you can unplug` only after
  device-verified success or the immutable onboarding receipt. Final gates
  passed: Storybook 86/86, Playwright 14/14, API 479/479, Orchestrator 54/54,
  Developer API 102/102, Devices 64/64, GrandSlam 50/50, zero build warnings/
  errors, Kubernetes 6/6, secret scan and diff PASS. Independent review
  returned PASS with zero Blockers or Concerns. No deployment, apply, secret
  read, commit, or push occurred. Phase 14 is now the sole `in-progress` phase.
