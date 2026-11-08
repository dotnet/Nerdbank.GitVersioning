# Recommended versioning workflow

Nerdbank.GitVersioning intentionally derives a commit's version from files committed with that commit.
Creating a branch or tag does not change its base version or remove a prerelease tag.
This makes historical builds repeatable: checking out the same commit with the same repository history and equivalent public-release context produces the same version.

Update and commit `version.json` when your intent for the product changes:

| Intent | Example committed `version` |
| --- | --- |
| Begin work on the next release | `1.3-alpha` |
| Stabilize a release candidate | `1.3-rc` |
| Declare the product stable | `1.3` |

Do not wait until packaging or deployment to inject a different version.
In particular, tagging a commit whose `version.json` says `1.3-rc` does not turn it into `1.3`.
The [`publicReleaseRefSpec`](public-vs-stable.md) setting only controls whether the git commit ID is included in the package version; it does not add, remove, or replace the prerelease tag.

## Choose the refs from which you publish

Configure `publicReleaseRefSpec` to match the branches and tags from which CI may publish packages (regardless of whether they are stable or prerelease).
For example, a repository that publishes prereleases from `main`, stabilizes on `release/vX.Y` branches, and may rebuild tags could start with:

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json",
  "version": "1.2-alpha",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/heads/release/v\\d+\\.\\d+$",
    "^refs/tags/v\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?$"
  ],
  "release": {
    "branchName": "release/v{version}",
    "tagName": "v{version}",
    "versionIncrement": "minor",
    "firstUnstableTag": "alpha"
  }
}
```

Adapt these regular expressions to your actual publishing policy.
For example, omit `main` if builds from `main` are never published, or omit tags if CI always publishes the artifact built from a release branch.
Any topic branch not matched by these expressions receives a git commit ID in its package version, which prevents its packages from colliding with packages from your public release refs.

Each simultaneously maintained public release branch should have a distinct base version in its committed `version.json`.
For example, `release/v1.2` should retain `1.2` while `release/v1.3` retains `1.3`.

## Recommended release-branch workflow

This workflow lets development continue on `main` while a release stabilizes and provides a long-lived branch for servicing an older release.
The [`nbgv prepare-release`](nbgv-cli.md#preparing-a-release) command automates the branch creation, version changes, and commits.

### 1. Develop the next release

Set the version on `main` as soon as you know which release you are developing, and include a prerelease tag while the product is unstable:

```ps1
nbgv set-version 1.2-alpha
git add version.json
git commit -m "Set version to 1.2-alpha"
```

Ordinary commits after this version change receive increasing version heights.
CI builds from `main` might produce public versions such as `1.2.18-alpha`, while builds of topic branches include a commit ID to remain unique.

### 2. Create the release branch

Start with a clean working tree on `main`.
When the release is ready for stabilization, run:

```ps1
nbgv prepare-release beta
```

Given the sample configuration above, this command:

1. Creates `release/v1.2` and commits `version.json` with `1.2-beta`.
2. Updates `main` to `1.3-alpha` and commits that change.
3. Merges the release branch back into `main`, resolving the intentional `version.json` difference in favor of `main`.

That initial merge records the relationship between the branches.
Later fixes can be merged from `release/v1.2` into `main` without repeatedly conflicting over the version change.
Push both updated branches after reviewing the result:

```ps1
git push origin main release/v1.2
```

Run `nbgv prepare-release` without a prerelease argument if the new branch should be stable immediately.
Use `--nextVersion` or `--versionIncrement` when the next version on `main` should differ from the configured default.
If your team exclusively cherry-picks fixes instead of merging them forward, use `--no-merge`.

### 3. Stabilize and release

Make stabilization fixes on the release branch.
When the quality designation changes, commit that decision rather than changing the version only in CI.
Running `prepare-release` from a branch that already matches `release.branchName` updates that branch without creating another branch:

```ps1
git switch release/v1.2
nbgv prepare-release rc
nbgv prepare-release
```

Each command above represents a possible promotion stage; use only the stages your project needs.
The final command removes the prerelease tag and commits the stable `1.2` version.
Removing the prerelease tag resets the version height, so use `nbgv get-version` to see the precise version produced by the stable commit.

Build, test, and publish the exact commit approved for release.
After choosing that commit, `nbgv tag` creates a tag containing its calculated version:

```ps1
nbgv tag
git push origin <tag-name-reported-by-nbgv>
```

The tag records which commit was released and can trigger publishing CI, but it does not alter the committed base version or prerelease tag.
As described above, matching `publicReleaseRefSpec` may only cause a tag build to omit the git commit ID.

### 4. Service a released version

Apply a servicing fix to the corresponding release branch, then build and release that commit using the same process:

```ps1
git switch release/v1.2
# Merge or cherry-pick the fix and commit it.
nbgv get-version  # tells you which version the new commit will build as
nbgv tag          # tags the commit with the version number for easier reference
```

Leave the base version as `1.2` for fixes in that release line.
With the default two-component version scheme, version height supplies the third component, so later commits produce later `1.2.x` versions without editing `version.json` for every fix.

#### Servicing flow suggestion
Merge fixes from older release branches into every newer maintained release branch and then into `main`.
This keeps released fixes in future versions.
If your repository uses a cherry-pick-only policy, cherry-pick the fixes in the opposite direction instead.
#### Searching for the commit behind some version
Have a bug report that version 1.2.3.4 has a bug in it? Finding the commit for that exact version is easy:

```
nbgv get-commits 1.2.3.4
```
This works with shorter 1.2.3 versions as well, but there may be multiple commits that qualify and you'll have to decide which one is correct.
If the customer reported version has a `-prerelease` suffix, omit it from the `nbgv get-commits` argument.
## Simpler single-branch workflow

Projects that do not need parallel development or servicing branches can release directly from `main`.
The same principle still applies: commit each change in release intent.

```ps1
# Begin the release line early.
nbgv set-version 1.2-alpha
git add version.json
git commit -m "Set version to 1.2-alpha"

# When stabilization reaches release-candidate quality.
nbgv set-version 1.2-rc
git add version.json
git commit -m "Set version to 1.2-rc"

# When the product is stable.
nbgv set-version 1.2
git add version.json
git commit -m "Set version to 1.2"
```

Build and publish the stable commit, and optionally mark it with `nbgv tag`.
Then advance `main` before accepting work intended for the next release:

```ps1
nbgv set-version 1.3-alpha
git add version.json
git commit -m "Set version to 1.3-alpha"
```

Advancing the version immediately prevents new development from being mistaken for servicing work on `1.2`.

## CI responsibilities

Keep release policy in source control and let CI build the checked-out commit:

1. Clone the repository with enough history to calculate version height. See [cloud build requirements](cloudbuild.md#requirements).
2. Do not rewrite `version.json` or strip prerelease tags in the build.
3. Build release artifacts only from refs allowed by your publishing policy.
4. Use `nbgv get-version` when a script needs the calculated version, and optionally use `nbgv cloud` to expose it to later build steps.
5. Publish an already-approved artifact or rebuild the exact tagged commit in an equivalent public-release ref context.

This separation keeps the version decision reviewable in git, the build deterministic, and the tag useful as a record of what was shipped.
