---
name: bundle-dependency-prs
description: Fix broken dependency update PRs and aggregate the ones that work into one PR.
disable-model-invocation: true
---

# Instructions

You have two goals:

1. Get all dependency PRs to a state where their PR checks pass.
2. Aggregate dependency PRs with passing checks into just one PR.

You can identify dependency update PRs by those authored by `dependabot` or `renovate`.

You'll find instructions for building and validating the repo in the [CONTRIBUTING.md](../../../CONTRIBUTING.md) doc.
Always validate your changes locally before pushing them to the remote repository.

When writing PR bodies or comments, avoid unmatched markdown code fences. Keep markdown well-formed.

For purposes of assessing PR readiness by its PR checks, consider docfx related checks to be irrelevant.
If a docfx check fails but all other checks succeed, then that is a 'successful' dependency update PR.

## Fix up dependency PRs with failing checks

Before aggregating PRs, first try to fix any individual dependency update PRs with failing build/test checks.

1. For the dependency PRs with failing build or test PR checks, check out their source branch and fix any issues.
2. Push your fixes as fresh commits to the individual dependency PRs.
3. If you can't fix a particular PR, add a comment to the PR describing your attempt and outcome.

## Group dependency PRs that are ready to go

Your next goal is to collect all the dependency updates that are ready to go into a single PR.

1. Prepare a local branch called `bulkDepUpdates`.
   1. Consider that a remote branch by the same name may already exist. If it does, base your local branch on it.
   2. Merge `origin/main` into this branch.
   3. Resolve any conflicts.
2. For the dependency PRs whose build and test PR checks already pass, merge them into the `bulkDepUpdates` branch.
   Consider that your local branch may have already merged an equivalent PR in the past (from a past run). If so, you should skip merging that PR.
   Resolve any conflicts.
   Build and run tests to validate your branch.
3. Push the branch.
4. Create a PR, if one does not already exist.
