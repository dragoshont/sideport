# Tournament of Options

## Option A — Minimal Safe Fix

Documentation-only SDD plan.

Pros: low risk. Cons: leaves durable operation gap unresolved. Risk/blast radius: very low. Verification: markdown/config only. Loses because the user asked to implement autonomously.

## Option B — Proper Architectural Fix

Full durable operations platform: background queue, cancel/rerun, scheduler integration, diagnostics, known-device inventory, UI timelines, deploy persistence.

Pros: closest to end state. Cons: too much blast radius and likely overbuilds before the contract is proven. Risk: high. Verification burden: broad. Loses on YAGNI.

## Option C — Defer / Ask More

Pause after plan and wait for sign-off.

Pros: conservative. Cons: user explicitly asked for autonomous implementation. Risk: low but stalls. Loses because no blocking ambiguity exists.

## Decision Matrix

Winner is a contract-first foundation slice: canonical contract and implementation plan, then a durable operation/preflight wrapper around the existing synchronous refresh path. This resolves drift and gives future UI/backend work a stable artifact without inventing a full job system yet.

## Winner

Implement the SDD foundation slice.
