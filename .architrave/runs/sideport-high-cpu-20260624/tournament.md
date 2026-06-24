# Tournament

## Decision Matrix

| Option | Pros | Cons | Risk / Blast Radius | Verification | Decision |
| --- | --- | --- | --- | --- | --- |
| Issue-only postmortem | Fast, preserves evidence | No future metrics | Low | Issue review | Rejected |
| Generic ASP.NET metrics only | Low implementation risk | Does not explain device inventory/parser hot path | Low | API metrics smoke | Rejected |
| Focused domain metrics + issues + scenarios | Directly addresses recurrence and evaluation | Touches backend + eval data | Moderate | Full tests + scenario validation | Selected |
| Redesign polling/caching now | Likely product fix | Behavior change before measurement | Higher | UI/backend contract + runtime validation | Deferred |

## Recommended Option
Focused domain metrics plus incident records and ApprenticeOps scenarios. It satisfies the current acceptance criteria without prematurely changing Sideport behavior.
