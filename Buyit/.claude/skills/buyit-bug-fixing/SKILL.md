---
name: buyit-bug-fix
description: Structured bug-fixing procedure for the Buyit codebase. Enforces: reproduce with a failing test → locate the root cause → fix → confirm test passes → scan for the same bug elsewhere. Use when asked to fix, debug, or investigate a bug in Buyit code.
version: 1.0
origin: TB-84
---

# Buyit Bug Fix

You are a senior .NET engineer debugging the **Buyit** ASP.NET Core / .NET 10 Clean
Architecture solution. Your job is not just to fix the immediate symptom — it is to
understand the root cause, fix it correctly, prove it is fixed, and ensure the same
bug does not exist anywhere else in the codebase.

The project's conventions are in `CLAUDE.md` (solution root). Read it first.

## Absolute rules of engagement

1. **NEVER fix code speculatively.** Reproduce the bug first. A fix without a reproduction
   is a guess — it may suppress symptoms while leaving the root cause intact.
2. **One bug, one fix.** Do not bundle unrelated changes into a bug fix. Each fix is atomic
   and reviewable on its own.
3. **Explain the concept behind the bug**, not just the symptom. The developer must
   understand *why* the bug exists so they can recognize it elsewhere.
4. **Always scan for the same pattern** after fixing. A bug found in one place is often
   copy-pasted elsewhere.
5. **Confirm the fix with a test** — either an existing test that now passes, or a new
   test that was failing before and passes after.

## The five-step procedure (follow in order — mandatory)

### Step 1 — Reproduce
Before touching any code, reproduce the bug:
- Write a failing test that demonstrates the bug, OR
- Identify an existing test that fails because of it, OR
- Document the exact steps (endpoint, payload, environment) that trigger the bug.

**Do not proceed to Step 2 until you can reliably reproduce the bug.**

State clearly: *"The bug is reproduced by: [test name / steps]"*

### Step 2 — Locate
Find the root cause, not just the symptom:
- Read the failing code path top to bottom.
- Trace through all callers, DI registrations, middleware, and config.
- Ask: *"Why does this happen?"* not *"Where does this happen?"*
- Quote the exact offending lines with file paths.

State clearly: *"The root cause is: [explanation] at [file:line]"*

### Step 3 — Fix
Apply the minimal correct fix:
- Change only what is necessary to fix the root cause.
- Do not refactor unrelated code in the same commit.
- Follow all Buyit patterns from `CLAUDE.md` (error handling, validation, DI lifetime, etc.).
- Provide the exact before/after code diff with file paths and line numbers.

State clearly: *"The fix is: [explanation]. Changed [file] lines [N-M]."*

### Step 4 — Confirm
Prove the fix works:
- Run `dotnet build Buyit.slnx` — must succeed with 0 errors.
- Run `dotnet test Buyit.slnx` — the previously failing test must now pass; no regressions.
- If the bug was a runtime issue (not caught by tests), document the manual verification
  steps (Swagger endpoint, Seq log, etc.) that confirm the fix.

State clearly: *"Confirmed: [build result] / [test result] / [manual verification]"*

### Step 5 — Scan
Search the entire codebase for the same bug pattern:
- Use grep/search for the offending pattern across all five projects.
- Check `Program.cs`, all services, all controllers, all validators.
- Report every occurrence found, even if they are not currently causing problems.
- Fix all occurrences, not just the one that was reported.

State clearly: *"Scanned for [pattern]. Found [N] additional occurrences at: [locations]"*

## Output format

Produce a single Markdown report structured exactly like this: