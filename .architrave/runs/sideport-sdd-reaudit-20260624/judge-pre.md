# Judge Gate 1

## Verdict

REVISE.

## Findings

- Major: first implementation only recovered expiry when the latest operation
	overall was succeeded. A newer failed/blocked operation could hide the latest
	durable successful expiry.
- Major: run artifacts were initialized but still empty/in-progress.
- Major: tests missed the failed-after-success durable renewal edge.
- Minor: diagnostics page header said "Live evidence" for mixed live and derived
	evidence.

## Revision Response

- `OperationService` now tracks latest operation and latest successful operation
	separately.
- Added `Renewals_KeepDurableExpiryWhenNewerOperationFailsAfterRestart`.
- Clarified contract wording: status/blocker/operationId describe the latest
	operation; expiresAt falls back to latest durable success.
- Updated diagnostics header copy.
- Filled intake, tournament, recommended plan, phase ledger, and gates artifacts.
