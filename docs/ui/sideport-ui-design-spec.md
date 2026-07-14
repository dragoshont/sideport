# Sideport UI Design Spec

> **Canonical target:** The six-destination product shell and trusted-person journey in
> this document are the Phase 7 Storybook approval target. The current runtime
> still contains the older operator-console routes until the approved deletion
> and binding pass in Phase 11. Storybook fixtures must not imply reliable Wi-Fi
> bulk transfer or device launch verification that the runtime cannot prove.
>
> **Phase 8 security delta:** the same approved invitation layout now represents
> an opaque HttpOnly handoff and explicit confirmation of the actual signed-in
> account before membership. Owner bootstrap/recovery uses the same short-lived
> link pattern. Sideport never asks for the deployment recovery bearer in the
> browser. Native mode creates Sideport passkeys; OIDC mode delegates identity
> and any provider-side passkey recovery to the configured provider.

## Audience

Primary user: a nontechnical member who opens an invitation, signs in
with a passkey, connects their iPhone once, installs an approved app, and then
expects Sideport to keep it refreshed at home.

Secondary user: the Sideport owner who connects one Apple signing account,
chooses the active Apple-returned team, approves apps, invites people they trust, and can
open technical details when something needs attention.

Sideport is an ongoing **multi-user app library and device service**, not an
onboarding product. First-run setup is shown only until the durable completion
receipt exists. After that, the product is organized around three recurring
jobs: find approved apps, install or update them on a person's iPhone, and see
whether any person or device needs attention. Sideport continuously monitors
the configured host USB port: an already trusted iPhone is recognized when it
is connected, while a new iPhone opens the guided Trust and enrollment flow.

Mobile is the primary layout. The signed-in mobile shell keeps a persistent
five-item bottom navigation for Home, Apps, Devices, People, and Activity;
Settings remains available from the top bar. Search is prominent, app rows or
cards expose one clear install/update action, and the Activity feed groups
human-readable events by time with **Needs attention** first. Routine polling,
healthy background checks, raw transport events, and repeated per-app noise do
not enter the default feed.

Use only two roles in the first trusted-people release:

- **Owner:** manages signing, approved app sources, member access, settings, and
  every enrolled device.
- **Member:** browses approved apps, adds and uses their own iPhone, installs or
  refreshes approved apps on that iPhone, and sees only their relevant activity.
  Self-service covers the first active iPhone; the Owner can deliberately add
  another after capacity preflight.

## Information Architecture

The signed-in shell has exactly six destinations:

- Home
- Apps
- Devices
- People
- Activity
- Settings

Secondary surfaces:

- Global search covers approved apps, iPhones, members, and
  plain-language activity. Technical identifiers appear only after disclosure.
- Persistent, role-aware **Add** menu. Owners can add an iPhone, import an app,
  or invite someone they trust. Members can add their own iPhone. Context and empty
  states invoke the same assistants rather than page-specific variants.
- Owner account creation and operational setup are separate. After the Owner
  claim is accepted, Sideport lands on Home even while `setupState` is
  incomplete. Home shows one persistent **Continue setup** reminder with the
  next server-authoritative action. The Owner may enter or leave the existing
  setup assistant at any time; leaving never creates the durable completion
  receipt or claims that signing, an iPhone, installation, or automatic refresh
  is ready. After the receipt exists, the reminder disappears and Setup remains
  available only as a recovery/status surface in Settings.
- First-run setup and later **Add iPhone** share one cable-to-install assistant.
  The assistant stays open through Trust, Developer Mode guidance, restart and
  reconnect, approved app choice, install verification, and the explicit
  safe-to-unplug result.

Consolidation rules:

- Renewals become due-soon and blocked states in Home and Apps.
- Operations and Diagnostics become Activity, with raw logs behind technical
  details.
- Apple Access and Teams become the owner-only Signing section in Settings.
- Users becomes People.
- Install App is an action from Apps, Devices, Home, search, or the assistant;
  it is never a permanent destination.
- Onboarding disappears from primary navigation after completion.

## Home

Purpose: answer "Is Sideport healthy?" in under five seconds.

Content:

- Fleet status strip: reachable devices, apps healthy, due soon, blocked.
- Refresh preview: only items that need attention, sorted by urgency.
- Device health list: connection, last seen, app slots, nearest expiry.
- Recent activity: sign/install/auth/device events.
- Owner-only system summary behind **Show technical details** when attention is
  required. Healthy infrastructure does not compete with member tasks.

Avoid:

- Decorative analytics cards.
- Big charts for tiny counts.
- Hero sections.

## First-Run Onboarding Flow

Outcome: a fresh deployment becomes operable from the admin UI through one
truthful path: server readiness, a usable Apple signer, an accepted iPhone, a
device-verified first install, and automatic refresh enablement. Saving a
device or app, or merely queuing an operation, does not complete setup.

Truth model:

- `setupState=complete` is immutable historical evidence that the entire
  first-run path succeeded. It is written only after the server rechecks the
  completion conditions and creates the completion receipt. Later outages or
  configuration changes do not reset it.
- `readyNow` is current operational health. It may become false when Apple
  authentication is stale, anisette or another server dependency fails, or an
  accepted iPhone is unavailable. After setup, recovery belongs on Home or
  Activity; do not force the owner back through onboarding.
- `source` describes contract provenance and remains `live`, `derived`, `demo`,
  or `planned`. `evidenceOrigin` separately identifies who or what supplied the
  evidence: `operator`, `device`, `apple`, `artifact`, `system`, or `operation`.
  Never use an evidence origin as a source value.

Flow steps:

1. **Server:** check mutation protection, readable and writable durable state,
   writable work storage, provisioned anisette, executable signer, and operation
   storage. Record device-transport health for technical details, but do not
   block this step merely because no iPhone is connected and the host starts
   usbmuxd on demand; cable and Trust guidance belongs to the iPhone step. Show
   deployment-owned remedies for genuine server failures but do not imply the UI
   applied them.
2. **Apple signer:** if the deployment has no credential, collect the Apple
   Account email and password directly in this step and submit them once to the
   authenticated Sideport server over HTTPS or explicitly enabled loopback.
   Clear the password field immediately, handle 2FA, and show only a redacted
   account hint afterward. Deployments with environment/SOPS or Keychain
   custody skip the form. Show Sign in, Verify, Team, and Certificate
   subprogress inside this macro step. Auto-select one Apple-returned Personal
   Team and show a chooser only for multiple returned teams. Reuse a valid
   signing identity. If replacement is required, show the exact certificate
   impact and block installation. The current UI does not expose certificate
   revocation or cutover; never imply that Sideport will revoke implicitly.
3. **iPhone:** show the USB, unlock, **Trust This Computer**, passcode,
   Developer Mode, restart, and post-restart confirmation steps together.
   **Connect iPhone** starts one authenticated, five-minute enrollment session
   and immediately changes the assistant to its waiting state; onboarding never
   asks for a second Connect click. Sideport then waits for one USB iPhone, requests pairing automatically,
   waits for the user to trust it on the iPhone, verifies lockdown, and adds the
   trusted phone to inventory. There are no separate Pair, Trust-confirmation,
   or Add buttons. Wi-Fi-only discovery blocks first pairing and the first
   install, but the UI explains that later refreshes can use the saved pairing
   over Wi-Fi. Multiple USB candidates require a choice before pairing. The
   same explicit session is used by **Devices → Add iPhone**; passive discovery
   never enrolls a phone. After acceptance on the same screen, guide Settings →
   Privacy & Security → Developer Mode, turn it on, restart, then unlock, tap
   Enable, enter the passcode, and reconnect if needed. Developer Mode remains
   operator guidance until a physically validated read-only probe exists.
4. **App:** default to a unified Sideport library. Show each ready artifact's
   generated/extracted icon, name, one-line description, inspected version, and
   provenance such as **On this server** or **GitHub release**. Server catalog
   items and configured GitHub release discovery are live. A GitHub release
   asset becomes a selectable library item only after Sideport downloads,
   persists, and inspects that allowlisted asset. Keep **Import IPA
   file** and advanced server-path import secondary. List apps already on the
   iPhone in a separate read-only section: a bundle match points to the ready
   library item; an unmatched app says **IPA file needed** because Sideport
   cannot copy or sign it directly from the phone. Selection creates a pending
   registration tied to the accepted device, account profile, and team.
5. **Install:** keep read-only preflight review inside this step rather than
   adding a Review step to the rail. Group Server, Apple, iPhone, IPA, Limits,
   and automatic-refresh checks; show blockers, warnings, scarce limits, and
   exact planned mutations. When it is ready, one **Install and finish** action
   submits the confirmed `planVersion` with `finishOnboarding=true`. A stale
   plan stays in this step, highlights what changed, and requires a fresh press.
   Render the real durable sign/install stages, then device verification,
   registration activation, scheduler enablement, next-evaluation computation,
   and completion-receipt creation. The fixed order is verify → activate →
   enable automatic refresh → compute next evaluation → write the receipt last.
   Do not replace the pipeline with a spinner or simulate progress. After a
   post-verification failure, **Retry finishing setup** resumes only the
   idempotent finalization work and never signs or installs again.
6. **Ready:** show this step only after the immutable completion receipt exists.
   Summarize the installed app, device-observed version/profile expiry, automatic
   hourly due-only refresh, next evaluation, paired-Wi-Fi option, and immediate
   USB fallback. Keep conditional developer-profile trust and opening-the-app
   guidance here as a small non-blocking note; there is no separate post-install
   device phase and no launch-verification claim. Route to the accepted device
   detail with the new app selected and keep Setup reachable from Settings.

Interaction and accessibility:

- The canonical presentation intentionally replaces the rejected left rail and
  operator-style panel. Fresh owner setup shows one readiness screen, one Apple
  signing screen, and then the shared three-stage **Connect**, **Prepare**, and
  **Install** cable assistant. A compact progress bar advances at each macro
  stage. Every viewport has at most one primary action; while an observable
  external event is pending, show status rather than another decision.
- While the enrollment operation is waiting for USB, Trust, verification, or
  acceptance, keep focus stable and render a disabled status label in place of
  a second decision. Announce meaningful stage changes politely; never ask the
  user to confirm an event Sideport can observe itself.
- Personalize the primary live status from the current member, for example
  **Waiting for Dragos’s iPhone…**. Attempt three subtle, distinct Web Audio
  cues after the user's start action: listening started, iPhone detected/Trust
  verified, and attention/still not detected. Audio is best effort and never
  substitutes for visible text, `aria-live` status, or server evidence.
- If the iPhone is still absent after a short wait, keep the same operation open
  and show the physical checks together: unlocked, charging, data-capable cable,
  and direct USB. Do not show a generic red lockdown error for an ambiguous or
  temporarily disconnected post-pairing state.
- Present the flow as a calm setup assistant, not an operations dashboard. Each
  screen shows one goal, a short plain-language explanation, current outcome,
  consequential warnings, essential choices, and one primary action.
- Hide system checks, identifiers, timestamps, evidence provenance, raw
  operation stages, and the immutable receipt by default. A global **Show
  technical details** control reveals them without changing the workflow.
- Never hide certificate-replacement impact, the USB-only first-install
  requirement, scarce app-slot limits, stale-plan changes, the plain-language
  instruction not to retry an uncertain install, or the boundary between
  install verification and opening the app. Keep watchdog, quarantine, lease,
  and other internal recovery terms inside technical details.
- Keep the six durable backend workflow stages—**Check Sideport**, **Connect
  Apple**, **Connect iPhone**, **Choose app**, **Install**, and **Ready**—for
  contract evidence, recovery, and technical details. Do not expose them as a
  six-item rail. The member-visible cable assistant compresses them into three
  advancing macro stages and keeps preflight, verification, automatic refresh,
  and receipt creation as automatic states. Technical terms may appear in
  expanded details or when required for a safe decision.
- The live web API supports protected first-credential entry through
  `/api/apple-access/personal/connect` and the managed encrypted credential
  store. Runtime UI shows the form only when status says credential entry is
  supported and allowed on the current authenticated secure connection. It
  never stores a password in browser persistence, redisplays it, or sends it
  through logs/analytics. Public Storybook credential stories use only
  prefilled example values, disable password-manager autocomplete, warn users
  not to enter real credentials, label the action **Continue demo**, and make
  no live calls. A future native macOS/Windows shell should use the platform
  credential store behind the same redacted status contract.
- One-action enrollment and the `finishOnboarding=true` install/finalization
  contract are live runtime capabilities. The server owns durable stages,
  verified registration activation, scheduler settings, next evaluation, and
  the completion receipt. Storybook interaction stories remain deterministic
  demo fixtures; they demonstrate the same shape without mutating a deployment.
- Paired-Wi-Fi refresh is a supported target capability, not a reliability
  promise: current bulk upload can stall or end ambiguously. Keep USB as the
  immediate, plainly worded fallback and require USB for pairing and the first
  install.
- In technical setup details, distinguish Docker on x86-64 Linux from the
  experimental Apple `container` 1.1+ path on Apple silicon/macOS 26. Until
  Phase 15 passes, do not claim working Apple Container device installation.
  The target path uses the existing `linux/amd64` Sideport image under official Rosetta, a
  separate native anisette container, two persistent state roots, an explicit
  official-CLI network/FQDN, and a forwarded host usbmuxd socket. Do not imply
  released Compose support, native macOS execution, native arm64 Sideport, raw
  USB passthrough, or a default mount of protected macOS pairing files.
- Moving between steps moves focus to the new step heading. Failed submissions
  move focus to a linked error summary, and field errors are associated with
  their inputs.
- Dialogs trap focus while open and restore it to the trigger on close.
- Use `aria-live="polite"` for stage transitions and 2FA challenge expiry, not
  for polling ticks or elapsed-time updates. Respect reduced motion.
- Icons and text carry status meaning; color is never the only signal. The flow
  must reflow without loss of content or action at 320 CSS pixels and remain
  usable at 200% zoom.

## Devices

Desktop layout:

- Data table with sticky header.
- Search by name, UDID, bundle ID, team.
- Facets: connection, health, team, app slots, expiry risk.
- Sort: last seen, nearest expiry, app count, device name.

Columns:

- Device: name, product type, OS.
- Connection: USB, Wi-Fi, offline.
- Last seen.
- Apps: `0/3`, `1/3`, `2/3`, `3/3`.
- Nearest expiry.
- Health: healthy, warning, blocked, failed.
- Team.
- Last refresh.

Mobile layout:

- Search and filter button.
- Device cards grouped by health.
- Each card shows name, connection, last seen, app slots, nearest expiry, primary action.

Device empty states:

- No devices known yet: show setup action.
- Devices known but none reachable: show last known devices and troubleshooting.
- Device reachable over USB only: explain Wi-Fi pairing if relevant.

## Device Detail

Header:

- Device name, model, iOS version, connection status, last seen.
- Primary action: refresh due apps.
- Secondary actions: add app, diagnostics.

Tabs:

- Apps
- Signing
- Network
- Diagnostics
- Activity

Apps tab:

- Three explicit slots.
- Empty slot action: add app.
- Filled slot: icon/name/bundle/version, signature expiry, last refresh, status.
- Full slots state: explain the free-tier limit before the user tries to add another app.

Signing tab:

- Team.
- Certificate expiry.
- Profile expiry per app.
- Last signing identity reuse/mint.
- Single-signer queue state.

Network tab:

- Current connection type.
- Last USB seen.
- Last Wi-Fi seen.
- Pairing/trust state when available.
- Suggested actions.

Diagnostics tab:

- Last install and device-verification result.
- Device-observed bundle, version, and profile expiry when available.
- Recent invalid-signature, install, verification, or OpenTelemetry-correlated failure events.
- Link to raw logs only as a secondary escape hatch.

Activity tab:

- Timeline: device discovered, app registered, cert/profile ensured, signed,
  installed, device verified, failed, user action.

## Apps

Purpose: manage app definitions independent of a device.

Views:

- All registered apps.
- By device.
- By team.
- Due soon.
- Failed refresh.

App detail:

- IPA metadata.
- Installed devices.
- Bundle ID.
- Version.
- Team/App ID/Profile.
- Refresh history.
- Diagnostics.

## Add App Flow

The persistent **Add app** dialog asks one question first: where the IPA comes
from. The three choices are **Upload from this computer**, **On this server**,
and **GitHub release**. Upload and validated server sources continue into the
shared App Catalog. GitHub is interactive only when the live API advertises it;
runtime shows the server's blocked reason otherwise, while Storybook may use a
clearly labelled demo capability.

Private GitHub access uses a selected-repository permission flow. Plain UI says
exactly **Metadata: read**, **Contents: read**, and **Write access: none**. It
never asks for or stores a token in the browser. A repository field accepts
only `owner/repository`, never a URL.

After a ready catalog artifact is selected:

1. Select target iPhone.
2. Inspect app name, icon, bundle ID, version, and embedded profile if present.
3. Reuse the connected Apple team by default; show a chooser only when needed.
4. Preflight constraints:
   - Device registered or will register.
   - App ID exists or will create.
   - Certificate exists or will mint/reuse.
   - Slot available.
   - Signer ready.
5. Confirm once.
6. Show pipeline progress and verify the device-observed installed bundle,
   version, and profile expiry.

Preflight should say what will happen before the user commits. This is especially important because Apple free-tier cert and app limits are scarce.

## Refresh Health (Home And Apps)

Purpose: the queue and risk surface.

Sections:

- Blocked: cannot refresh without intervention.
- Due now: expires inside configured lead time.
- Upcoming: sorted by expiry.
- Healthy: no action needed.

Queue item fields:

- App/device.
- Expires in.
- Team.
- Last attempt.
- Blocker/error.
- Action.

Single-flight behavior:

- If a refresh is running, show the current operation from the backend operation
   record.
- Do not allow parallel manual refresh without explaining serialization.
- Do not invent queued items. Show cancel/rerun only after the backend exposes a
   safe background operation boundary and capability flags.

## Apple Signing (Owner Settings)

Apple signing and workspace membership must remain visually separate. Apple
Developer Team is the source of certificates and profiles; Member is Sideport
membership and never selects or owns a signing team.

Apple team view:

- Team ID/name/type.
- Apps using it.
- Devices registered through it.
- Certificate status.

## People

Roles:

- Owner: signing credentials, active Apple-returned team, app sources, settings,
  invitations, all devices, and deliberate destructive actions.
- Member: approved apps, their own devices, their own installs/refreshes, and
  relevant activity. Members may see the approved minimal people directory
  (active display names, roles, and coarse iPhone counts) but never manages
  signing, sources, or other members and never sees their emails, device IDs, or
  activity.

Flows:

- Invite user.
- Invite as Member. The initial release does not expose arbitrary role choices.
- Pending invite.
- Suspend or remove access with exact device/refresh impact.
- View audit trail.

Identity is deployment-configurable. Native mode owns passkey enrollment and
sessions inside Sideport; OIDC mode delegates identity and recovery to the
configured standards-compliant provider. Sideport owns durable membership and
authorization using the validated provider-neutral issuer + subject contract.
Unknown authenticated principals receive `403 membership-required`; they never
become owners implicitly. Native mode says **Create passkey** or **Sign in with
a passkey**. OIDC mode uses the configured generic provider label. Neither mode
labels this as **Sign in with Apple**. Official Sign in with Apple is
shown only when an eligible paid Apple configuration actually exists.

The private link is captured only long enough to exchange it for an opaque,
short-lived HttpOnly handoff before identity enrollment or sign-in. After sign-in, show the actual account,
workspace, and Member permissions and require **Join Sideport**. Recovery copy
sends the person to the configured identity recovery surface. A fresh Owner
opens a short-lived host-minted Owner link, creates or uses a passkey, and
confirms **Finish owner setup** before the Apple signer onboarding begins.

## Activity

Purpose: show installs and refreshes in plain language, and answer "why did
this app not work?" when the owner expands technical details.

Issue list backed by OpenTelemetry and Sideport's own event history:

- Grouped by app + device + failure type.
- Last seen / first seen.
- Severity: info, warning, error, fatal.
- Status: unresolved, investigating, resolved, ignored.
- Evidence: refresh result, install result, device-side verification, trace ID, operation ID, span timeline, structured log snippet.

Issue categories:

- Invalid signature / Code=85.
- Provisioning profile expired.
- Device unreachable.
- Install failed.
- Anisette unavailable.
- 2FA required.
- Apple rate limit.
- Apple Developer Services error.
- Signer process failed or timed out.
- Device bridge/usbmuxd unavailable.
- App crash or Jetsam/watchdog later, only after a real app/log source exists.

Do not infer a launch result from install success or an operator acknowledgement.
Until a truthful device/app log source exists, opening the app remains guided
operator work rather than diagnostic evidence.

Agent-assisted panel:

- "Explain this failure"
- "What should I try next?"
- "Create an investigation note"

Never upload logs, traces, crash logs, or device logs to external AI services without an explicit user decision.

## Settings

Sections:

- Account and passkey recovery for every member.
- Owner-only Signing: redacted Apple identity, Apple-returned active team,
  certificate impact, and deliberate reauthentication or replacement.
- Automatic refresh policy, on by default after the verified first install.
- Setup and deployment recovery.
- **Show technical details:** API authentication, anisette, signer, usbmuxd,
  storage, retention, and observability. These do not appear as peer navigation
  destinations.

## Visual Direction

Tone:

- Quiet, precise, confident.
- Apple-like hierarchy without imitating macOS chrome.
- Operational, not decorative.
- Approachable to a first-time, nontechnical owner; technical depth is available
  on demand instead of competing with the next action.

Theme:

- System font stack with Inter fallback.
- Light mode first, dark mode later.
- One primary accent: blue.
- Semantic status colors used sparingly.
- 8px spacing rhythm.
- Border radius 8-14px for controls/surfaces.
- Hairline separators and soft shadows, not glass blobs.
- Generous whitespace, one focused content surface, and a compact progress
  sidebar that can map to future macOS and Windows desktop shells.

Components:

- App shell with sidebar on desktop, bottom/tab or drawer navigation on mobile.
- Tables on desktop, cards/lists on mobile.
- Badges for statuses.
- Progress stage component for signing pipeline.
- Timeline component for activity.
- Empty-state component with one primary next action.
- Confirmation dialog for refresh/sign/install actions.

## Copy Rules

- Say "refresh" for renewing a signed app before expiry.
- Say "sign" only inside the pipeline or technical details.
- Say "Apple Developer Team" for Apple teams.
- Say "People" for the navigation destination, "Member" for limited access,
  and "Invite someone you trust" for the invitation action.
- Prefer exact blockers over generic failures.
- Keep `source` provenance separate from `evidenceOrigin`; an operator action is
  not automatically device evidence.
- Say "discovered" until lockdown trust succeeds, "queued" until an operation
  runs, and "verified" only after Sideport rereads the installed bundle and
  profile. Never claim launch verification.

Examples:

- Good: "2FA is required before Sideport can refresh apps for this Apple ID."
- Bad: "Authentication failed."
- Good: "This device has 3 of 3 app slots in use. Remove an app registration before adding another."
- Bad: "Limit exceeded."

## Storybook State Matrix

Minimum stories:

- Canonical product shell: exactly Home, Apps, Devices, People, Activity, and
  Settings; owner and member variants; role-aware Add; global search; desktop,
  390px mobile, 320px reflow, and keyboard paths. Assert that Onboarding,
  Renewals, Operations, Diagnostics, Apple Access, Teams, Users, and Install App
  are absent as permanent destinations.
- Member invitation: owner invites someone they trust; invitee opens the
  link outside the shell; passkey copy names Face ID, Touch ID, Windows Hello,
  or a password manager without claiming official Sign in with Apple; raw link
  authority is replaced by an opaque handoff before OIDC; the actual signed-in
  account and permissions are confirmed with **Join Sideport**; expired, used,
  suspended, and Authentik-owned recovery states.
- Passkey enrollment asks only for the person's name and email. Sideport
  generates the provider's opaque internal username; the UI never asks a person
  to invent one and never reuses email as an infrastructure identifier.
- Identity is deployment-configurable without changing the product journey.
  Native mode creates/signs in with a Sideport-owned passkey and has no
  Authentik dependency. OIDC mode redirects to a generically labelled provider
  and may offer provider-owned enrollment. The private-link screen shows only
  actions the configured backend can actually perform; it never shows an OIDC
  fallback in native-only mode or implies that an external provider is required.
- Owner claim: native first-run setup opens directly on an unclaimed private
  deployment with no link or API-key field. Later Owner recovery/replacement
  still uses a short-lived private link and confirms the actual account and
  impact before replacement continues.
- One-cable member assistant: cable/unlock/Trust/passcode guidance together;
  detection, pairing, Trust verification, and Sideport acceptance automatic
  after one start action; Developer Mode/restart/reconnect guidance before app
  choice; no **Check Trust** recovery action and Continue stays disabled until
  server acceptance; personalized waiting/detected/attention sound cues;
  unified approved app library; one **Install** action; verified chime attempt
  plus **Installed — you can unplug**; automatic home-Wi-Fi refresh with honest
  cable fallback.
- Owner setup outside the shell: fresh Docker and Apple Container explanations
  in plain language, deployment-specific repair disclosure, one Apple signer,
  Apple-returned team selection only when multiple teams exist, and handoff to
  the same one-cable assistant.
- Home: healthy, due soon, blocked, no devices.
- Device table: many devices, no devices, offline devices, filtering.
- Device card: USB, Wi-Fi, offline, blocked, full slots.
- Device detail: one app, three apps, failed app, no apps.
- Onboarding shell: true fresh deployment; persistent footer; Back plus one
  valid next action; desktop and mobile interaction paths.
- Onboarding server: checking; configuration blocked; missing mutation
  protection or permission; partial request failure with successful evidence
  preserved.
- Onboarding Apple access: credential missing with direct email/password form
  and disabled-until-complete **Sign in** action; credential configured; 2FA
  required; 2FA expired; authentication failed; throttled; no returned teams;
  returned-team selection; stale selection. Verify username/current-password
  autocomplete, Enter submission, immediate password clearing, and 2FA focus.
- Onboarding signer: identity reuse; mint with no existing certificate; and a
  replacement-required blocker with exact impact and no in-UI revocation or
  cutover claim.
- Onboarding iPhone: ready to connect; waiting for USB; awaiting Trust; locked;
  Wi-Fi-only; verifying; adding automatically; accepted; multiple-device
  selection before pairing.
- Onboarding app: three-item demo library with icons, descriptions, versions,
  merged server/GitHub provenance and working selection; installed-iPhone
  matches and unmatched **IPA file needed** rows; secondary file import; upload
  too large or unsupported; inspection failure; catalog conflict; app slots
  full; pending registration; legacy registration verification required.
- Onboarding install: blocked, ready, and stale inline preflight; one **Install
  and finish** submission; queued; running; capability-driven cancel; canceled;
  failed; timeout/unknown and quarantined; verification failed; reconnect and
  resume after reload; device-verified; registration activation; scheduler
  enable/next-evaluation failure; finalization-only retry with no reinstall.
- Onboarding ready: conditional developer-profile trust/open-app note with no
  launch claim; automatic hourly due-only policy with paired-Wi-Fi support and
  explicit USB fallback; immutable receipt; `setupState=complete` with
  `readyNow=true`; completed setup with regressed `readyNow=false`; device-detail
  handoff with the installed app selected.
- Onboarding accessibility: full keyboard journey; step-heading focus; linked
  error summary and field errors; dialog focus trap and restoration; polite
  stage-transition and 2FA-expiry announcements; reduced motion; non-color
  status meaning; 320 CSS-pixel reflow; 200% zoom.
- Add app outside onboarding: preflight healthy, app slots full, signer missing,
  device offline.
- Refresh health in Home/Apps: empty, running, queued, failed, 2FA required.
- Activity issue: invalid signature, install or verification failure,
  anisette failure, developer-services failure, signer timeout, resolved.
- Settings: token configured, token missing, anisette unprovisioned.

## Implementation Guardrails

- Do not call refresh automatically from route load.
- Do not hide destructive or scarce-limit actions behind hover-only controls.
- Do not expose `/api/*` in dev without making auth state visible.
- Do not create frontend-only status semantics that the API cannot eventually support.
- Do not add a chart unless it helps choose an action.
- Do not treat discovery as trust, Wi-Fi as a supported first-install transport,
  a queued operation as installation, or install success as launch verification.
- Do not reset `setupState=complete` when `readyNow` regresses, and do not merge
  `evidenceOrigin` into the `source` vocabulary.
