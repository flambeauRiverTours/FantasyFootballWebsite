---
description: Fan out every review agent over the current diff and merge their reports into one gated verdict.
---

Run the repo's review agents against the current change and report once.

Optional argument `$ARGUMENTS` narrows the scope (a path, a task ID, or `all`
for the whole tree). Default scope is `git diff HEAD` plus staged files.

## Step 1 — decide who runs

Always: `a2-auditor`, `code-reviewer`, `code-style`, `doc-sync`.

Conditionally:
- `api-contract` — only if the diff touches `src/`, `web/src/api/types.ts`, or
  the deployed API is known to have changed. It needs the API reachable.
- `layout-verifier` — only if the diff touches `web/src/index.css` or a
  component's markup, **and** a dev server is running. It needs a live page.
  Do not start one to satisfy this; say it was skipped and why.

Skipping is a normal outcome. Say which agents did not run and what would make
them runnable.

## Step 2 — spawn

Spawn the selected agents **in one message** so they run in parallel. They are
all read-only and share no state, so ordering buys nothing.

Give each one the scope explicitly — a list of paths, not "the repo". Each spawn
starts cold and will otherwise sweep the whole tree and spend its budget on
files nobody touched.

## Step 3 — merge

Do not paste six reports. Produce one:

1. **Gate.** `a2-auditor` is pass/fail, not advisory. Any hit is a stop — an
   identifier in a public repo cannot be fixed by a later commit. State it first
   and state it alone if it fails.
2. **Defects**, from `code-reviewer`, most severe first, each with its failing case.
3. **Shape**, from `code-style`, collapsed to the two or three changes worth
   making now.
4. **Drift**, from `doc-sync` and `api-contract`, as specific edits.
5. **Geometry**, from `layout-verifier`, as numbers.

Then **reconcile**: where two agents flag the same line, say so once. Where two
disagree — `code-style` wants a function split, `code-reviewer` says the split
would duplicate a guard — resolve it and say which you chose.

Close with the smallest set of changes that clears the gate, and nothing else.

## Step 4 — do not fix

Report only. Jack writes the code (see CLAUDE.md). Offer to apply a fix only if
it is mechanical, and ask first.
