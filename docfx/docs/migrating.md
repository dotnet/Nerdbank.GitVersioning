# Migrating to Nerdbank.GitVersioning

## Dealing with legacy version.txt or version.json files

When you already have a version.txt or version.json file in your repo and want to use Nerdbank.GitVersioning,
you may find that your build breaks when NB.GV tries to parse your version.txt or version.json file(s).

Any such version.txt or version.json files in project directories with NB.GV installed, or any parent directory up to the repo root, must be removed in a commit *prior* to the commit that defines the new NB.GV-compliant version.json file. This will ensure that NB.GV will not discover the legacy files and try to parse them.

It is important that you maintain that clean break where no commit with a NB.GV version.json file has an immediate parent commit with a legacy version file. A merge commit (like a pull request with your migration changes would create) will defeat your work by having the new version.json file and an immediate parent with the version.txt file (the one from your base branch). It is therefore mandatory that if you have such a legacy file, that when you're done validating the migration that you *directly push your 2+ commits to your branch* rather than complete a pull request. If your team's policy is to use pull requests, you can create one for review, but complete it by pushing the commits directly rather than letting the git service create the merge commit for you by completing the pull request. If the push is rejected because it is not a fast-forward merge, *rebase* your changes since a local merge commit would similarly defeat your efforts.

Also note that any other open pull requests that are based on a commit from before your changes may also introduce the problematic merge commit by providing a direct parent commit path from the new version.json file to the legacy one. These open PRs must be squashed when completed or rebased.

## Maintaining an incrementing version number

When defining your new [version.json file](versionJson.md), you should set the version to be same *major.minor* version that you used before or higher.
If you are matching the prior *major.minor* version and need the build height integer (usually the 3rd integer) of your version to start higher than the last version from your legacy mechanism, set the `"buildNumberOffset"` field in the version.json file to be equal to or greater than the 3rd integer in the old version. Note that the first build height will be *one more than* the number you set here, since the commit with your changes adds to the offset you specify.

For example, suppose your last release was of version 1.2.4. When you switch to NB.GV, you may use a `version.json` file with this content:

```json
{
  "version": "1.2",
  "buildNumberOffset": 4
}
```

After commiting this change, the first version NB.GV will assign to your build is `1.2.5`.

When you later want to ship v1.3, remove the second field so that the 3rd integer resets:

```json
{
  "version": "1.3"
}
```

This will make your first 1.3 build be versioned as 1.3.1.
