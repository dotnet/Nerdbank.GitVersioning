# Public vs. stable releases

There is sometimes confusion around Nerdbank.GitVersioning's concept of a "public release"
and SemVer/NuGet's concept of a "stable release".

Let's start with a clear distinction: public and stable releases are (mostly) orthogonal:

1. [SemVer defines a prerelease](https://semver.org/#spec-item-9) as a version with any hyphenated suffix (e.g. `-prerelease`).
1. Nerdbank.GitVersioning uses the term "public release" to connotate a version suited for public consumption because it participates in linear history. A public release does *not* include the `-gc0ffee` commit hash.

## SemVer pre-releases

From semver.org:

> A pre-release version indicates that the version is unstable and might not satisfy the intended compatibility requirements as denoted by its associated normal version.

The unstable nature of a product might be in functional resilience, or that its API isn't finalized, or lack of adequate testing.
Any and all of these are based on the assessment of the software engineers responsible for the project.

Like the version *number*, the `-prerelease` tag (if there is one) is recorded in a git source tree for Nerdbank.GitVersioning to use when building.
A given commit in a repo represents software that builds v1.2 of a product or v1.2-beta of a product, depending on how its owner(s) felt about the commit at the time they authored it.
When a branch becomes stable, the `-prerelease` tag can be removed by adding a commit to the branch that strips the tag.

**There is no way to remove the `-prerelease` tag from an existing commit** that has a `-prerelease` tag expressed in its committed version.json.
To remove the `-prerelease`, the version.json file must be changed to remove it.
Committing this change communicates to everyone looking at the repo that this software is stable.

The natural evolution of a product usually includes entering and exiting a `-prerelease` stage many times, but within a branded release (usually recognized by an intentional version number like "1.2") the progression usually transitions only one direction: from `-prerelease` to stable quality.
For example, an anticipated version 1.2 might first be released to the public as 1.2-beta before releasing as 1.2 (without the `-beta` suffix).
If the product is undergoing significant changes that warrant downgrading the stability rating to pre-release quality, the version number tends to be incremented at the same time.
So a 1.2 product's subsequent release might appear as 1.3-beta or 2.0-beta.
But for a particularly stable product, it's possible for releases to remain stable from one release (1.2) to the next (1.3) without ever publishing a pre-release version.

**Tip**: To aid in the common workflow of stabilizing for a release including branching and updating `version.json`, and mitigating merge conflicts in that file, we have the [`nbgv prepare-release`][nbgv_prepare-release] command to automate the process.

In all this, to consumers of the product there is never any question regarding which of two releases is newer.
[SemVer formalizes version comparisons](https://semver.org/#spec-item-11) but, in essence, the larger the number the newer it is such that there is never ambiguity between two versions.
This is what I refer to as "linear" history.
Every version is a point along a line of versions.
It's possible to ship a servicing release "in the middle" of your line, but it's still a line and the servicing release is not as new as your latest release.

## Nerdbank.GitVersioning and *public* releases

The SemVer-world of linear history is a fantasy enjoyed by the outside world.
If you live in a services world and deploy constantly from one branch yet never ship packages to others, your development might even resemble this.
For those of us who actually share software packages with others, your world of software development may not resemble such "linear" history at all.
You may have many topic branches where concurrent development is occurring (even if those branches are short lived).
Or you may have servicing branches where you can patch already shipped software while you continue development of your next major version.
All these branches may not resemble anything close to what might be called "linear".
And that's OK. We just need tools that support our real-world development flow.
That's what Nerdbank.GitVersioning's "public release" flag is for. Let's dive in.

There are traces of linear history in your repo.
Any commit in git can be formally shown to be either older or newer than any other commit belonging to the same branch, similar to any two versions in SemVer can.
Within a single branch then, you have linear history.
If you always ship from `main` for example, then `main` can act as your linear parallel to your semver-world of public releases.
To capture this, you can tell Nerdbank.GitVersioning that you ship out of main in your version.json file:

```json
{
  "version": "1.2",
  "publicReleaseRefSpec": [
      "^refs/heads/main$"
  ]
}
```

But what exactly does this `publicReleaseRefSpec` property do?
It tells Nerdbank.GitVersioning which branch(es) to assume belong to your publicly visible linear history.
When building such a branch, it's safe to build packages that have only a version number.
So building either of a couple of commits along the main branch where 1.2 is the specified version might produce a package versioned as 1.2.5 for the 5th commit and 1.2.9 for the 9th commit.

When you're *not* building from a "public release" branch, Nerdbank.GitVersioning delivers on several requirements:

1. Because you're *not* participating in linear history, the version stamp should make this clear.
1. The version should be sufficiently unique so as to guarantee that no two commits in two arbitrary branches in git can collide. This is particularly important when building packages that might be shared or expanded into a local cache no more than once based on the version.
1. Even if the base of your topic branch is considered "stable", your incomplete work in a topic branch certainly shouldn't be considered stable or confused with something from the mainline branch, so anything built from it should be forcibly interpreted as unstable.

Nerdbank.GitVersioning accomplishes these objectives by appending a special pre-release suffix to _everything_ built in a non-public release branch. This prerelease tag is based on the git commit ID being built.
For example if you're building a topic branch from version 1.2 with a commit ID starting with c0ffeebeef, the SemVer-compliant version produced for that build would be `1.2-c0ffeebeef`. If the version.json indicated this is `-beta` software, the two prerelease tags would be combined to form `1.2-beta-c0ffeebeef`.

If in addition to shipping out of `main` you also service past releases, you might name those branches with a convention of v*Major*.*Minor* (e.g. v1.2, v1.3) and then add the pattern to your version.json file's `publicReleaseRefSpec` array:

```json
{
  "version": "1.2",
  "publicReleaseRefSpec": [
      "^refs/heads/main$", // main releases ship from main
      "^refs/heads/v\\d+\\.\\d+$" // servicing releases ship from vX.Y branches
  ]
}
```

When you specify multiple branches as public release branches, it is very important that each of these branches have a *unique* version specified in the `version` property of the version.json file.
This guarantees that versions built from any two of these public release branches never collide in version number.
Naming most/all your public release branches after the version they build can help folks to find the right branch as well as help maintain unique versions for each branch.

In development of a topic branch, you might find a need to share packages before merging into one of these public release branches.
That's just fine -- you can share your `-gc0ffeebeef` suffixed packages.
This suffix will make it clear to those you share the package with that these are unofficial packages whose version do not participate in linear history and thus are not necessarily older or newer than another public release.

A commit may belong to multiple branches in git at once.
If some of those branches are "public release" branches and some are not, will building that commit result in a public release version or not?
The public release flag is determined by the *ref* (i.e. branch or tag) being built -- not the commit.
The same commit can be built as a public release or a non-public release depending on which branch is checked out during the build.

### Overriding the public release flag for a branch

The public release flag *can* be overridden during a build by setting the `PublicRelease` MSBuild property.
To force public release versioning, you can add the `/p:PublicRelease=true` switch to your msbuild or `dotnet build` command line.
To force a *non*-public release build, you can similarly specify `/p:PublicRelease=false`.

This can be useful when testing a topic branch will build successfully after merging into a stable, public release branch by forcing a local build to build as a public release.
For example suppose `main` builds a stable 1.2 package, and your topic branch builds `1.2-c0ffeebeef` because it's a non-public release.
In your topic branch you've made some package dependency changes that *might* have introduced a dependency on some other unstable package.
Your package manager didn't complain because your package version was unstable anyway due to the `-c0ffeebeef` suffix.
But you know once you merge into `main`, it will be a stable package again and your package manager might complain that a stable package shouldn't depend on a prerelease package.
You can force such warnings to show up in your topic branch by building with the `/p:PublicRelease=true` switch.

### More on why and when git commit hashes are useful

Consider that main builds a 1.2 version, and has a version height of 10. So its package version will be 1.2.10. Now imagine a developer branches off a "fixBug" topic branch from that point and begins changing code. As part of changing and testing that code, a package is built and consumed. Note the developer may not have even committed a change yet, so the version and height is *still* 1.2.10. We *don't* want a package version collision, so the topic branch produces a package version of `1.2.10-gc0ffee`. Now *both* the official main version and the topic branch version can both be restored and populate the nuget cache on a machine without conflicting and causing bizarre inconsistent behaviors that boggle the mind. :)

Or, if the topic branch *has* committed and moved onto 1.2.11, that could still collide because `main` may have moved on as well, using that same version. But since the topic branch always adds `-gc0ffee` hash suffixes to the package version, it won't conflict.
Also: you don't want a topic branch to be seen as newer and better than what's in the main branch unless the user is explicitly opting into unstable behavior, so the `-gc0ffee` suffix is useful because it forces the package to be seen as "unstable". Once it merges with `main`, it will drop its `-gc0ffee` suffix, but will retain any other `-prerelease` tag specified in the version.json file.

[nbgv_prepare-release]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/doc/nbgv-cli.md#preparing-a-release
