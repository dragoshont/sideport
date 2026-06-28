# Security Policy

Sideport automates Apple's **free** app-signing flow on hardware you control. It
handles **Apple ID credentials**, signing certificates, and provisioning
profiles, so security reports are taken seriously.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Use GitHub's private reporting instead:

1. Go to the repository's **Security** tab → **Report a vulnerability**
   ([how to privately report](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)).
2. Describe the issue, its impact, and a minimal reproduction.

You will get an acknowledgement, and a fix or mitigation will be coordinated
privately before any public disclosure.

## Supported versions

Sideport is pre-1.0 and ships from `main`. Security fixes land on the latest
`0.1.x` release and the `:latest` / `:edge` images. Pin an exact version (or a
digest) in production and update promptly when an advisory is published.

| Version | Supported |
|---|---|
| latest `0.1.x` | ✅ |
| older tags | ❌ (please upgrade) |

## How Sideport handles secrets

- The Apple password is read from the environment (or a configured secret
  source); it is **never** stored in the app registry and **never** returned by
  the API.
- Error messages and logs are redacted to keep credentials, tokens, and file
  paths out of responses (see the redaction tests).
- Every `/api/*` call requires a bearer token (`SIDEPORT_API_TOKEN`). Do **not**
  expose the API to the public internet without an authenticating reverse proxy.
- The anisette identity volume is sensitive (your trusted-device material) —
  back it up and protect it like a credential.

## Responsible use

Sideport is for signing apps **you are allowed to install, on devices you own,
with your own Apple ID**. It does not crack, bypass, or distribute apps, and it
ships no apps. Using it against devices or accounts you do not control, or to
violate Apple's terms, is out of scope and unsupported.
