# Contributing to Sideport

Thanks for your interest! Sideport is a .NET 10 service that keeps sideloaded iOS
apps signed. Bug reports, docs fixes, and focused PRs are all welcome.

## Before you start

- Search existing [issues](https://github.com/dragoshont/sideport/issues) first.
- For anything security-related, follow [SECURITY.md](SECURITY.md) — **do not**
  open a public issue.
- For a larger change, open an issue to discuss it before writing code.

## Build and test

You need the **.NET 10 SDK**. Development is supported on **Linux and macOS**
(the signing path uses Unix file modes and a Linux `zsign` binary, so Windows is
not a supported build target).

```bash
dotnet build -c Release        # build the solution
dotnet test  -c Release        # run the full suite — must stay green
```

The shipped container is **linux/amd64**, built from `deploy/Dockerfile`.

## Project layout

| Path | What |
|---|---|
| `src/Sideport.Api` | HTTP API + scheduler + composition root |
| `src/Sideport.*` | signing, device, Apple developer-services, GrandSlam libraries |
| `tests/` | unit + smoke tests (xUnit) |
| `deploy/` | Dockerfile, Docker Compose, and Kubernetes examples |
| `samples/` | tiny sample apps you can sign end-to-end |

## Pull requests

- Keep PRs small and focused — one logical change per PR.
- **Tests must pass** (`dotnet test`), and new behaviour needs a test.
- Never commit secrets, real device UDIDs, `.ipa` files, or private hostnames.
- Match the existing code style; avoid unrelated reformatting.
- Update the README/docs when you change user-facing behaviour.

## Licensing

By contributing, you agree your contributions are licensed under the
repository's [MIT License](LICENSE).
