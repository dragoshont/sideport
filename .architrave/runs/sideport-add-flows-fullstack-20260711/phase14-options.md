# Phase 14 Options — Bounded Device Transfer

## Option A — Keep the lease until restart

Preserve the current cancellation-token-only behavior and document the pod
restart. Rejected: it leaves a known recurring transport stall as a service-wide
availability failure and does not meet Phase 14.

## Option B — Release the lease immediately at the deadline

Return `unknown` but allow another mutation while the old managed transfer may
still be running. Rejected: this violates single-flight and can overlap two
device mutations.

## Option C — Automatic USB retry after a Wi-Fi timeout

Switch transports and issue another install. Rejected: once bytes or the
installation command may have reached the phone, retry is ambiguous and can
duplicate a mutation. USB is a recovery transport only after reconciliation.

## Option D — Owned-socket hard abort, truthful unknown outcome

Register cancellation against both transport-owning services: close the AFC
socket during upload and the installation-proxy socket during command/result
wait. Preserve `unknown` after any deadline. Release the lease only if the
managed transfer task actually terminates; otherwise keep the existing observer
and held-lease safety boundary.

## Decision

Choose Option D. It is the smallest local repair, adds no dependency or new
abstraction, preserves the contract's ambiguity and reconciliation rules, and
removes the restart requirement when the owned socket close successfully
terminates the transfer.

## Focused gates

- A real loopback-backed vendored AFC upload stalls, cancellation closes the
  socket, and the install task terminates within one second.
- A hard-aborted orchestrator install returns `install-outcome-unknown`, releases
  the active lease, and permits a later USB-mode operation.
- A deliberately cancellation-ignoring fake still holds the lease until its
  actual transfer task ends.
- Existing USB-over-Wi-Fi duplicate transport tests remain green.
