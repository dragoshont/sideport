# Sample apps

Tiny iOS demo apps for trying out Sideport's free-tier auto-signing. They are the
source behind the prebuilt IPAs in the
[`sample-apps` release](https://github.com/dragoshont/sideport/releases/tag/sample-apps).

| App | Bundle ID | What it is |
|---|---|---|
| **CertCountdown** (shows as *CertClock*) | `ro.hont.certcountdown` | A live countdown to your signing certificate's expiry — the best one for *seeing* Sideport keep an app alive. |
| **DiceRoll** | `ro.hont.diceroll` | A one-tap dice roller. |

A third sample, **Concentration** (a card-matching memory game), lives in its own
repo: [dragoshont/concentration-ios](https://github.com/dragoshont/concentration-ios).

## Build them yourself

They build **unsigned** — Sideport re-signs each one with *your* Apple ID when it
installs them, so you need no paid developer account and no signing identity.

```bash
brew install xcodegen        # one-time
./build.sh                   # builds every app here -> ./dist/*.ipa
./build.sh DiceRoll          # ...or just one
```

Then point Sideport at the `.ipa` (see the main
[tutorial](https://github.com/dragoshont/sideport#tutorial-your-first-auto-signed-app)).

## Notes

- Built with [XcodeGen](https://github.com/yonaskolb/XcodeGen) from `project.yml`;
  the `.xcodeproj` is generated, not committed.
- `DEVELOPMENT_TEAM` is intentionally blank — set your own Apple Team ID only if
  you want to build *signed* locally. For Sideport you don't need to.
- A free Apple ID allows **3 sideloaded apps per device** at a time.
