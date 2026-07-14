# Tournament of Options

| Option | UX | Truthfulness | Recovery | Cost | Decision |
| --- | ---: | ---: | ---: | ---: | --- |
| Copy-only error rewrite | 2 | 3 | 1 | 5 | Reject: the operation still stops after a transient pairing exception. |
| Browser-only polling/retry | 3 | 2 | 2 | 3 | Reject: browser lifetime would own a server/device safety transition. |
| Durable automatic Trust recovery + UI/audio cues | 5 | 5 | 5 | 4 | Select: server remains authoritative; UI presents observable states. |

No audio files or dependency are needed. Reuse Web Audio, existing dialog
anatomy, durable operation polling, and current server stages.
