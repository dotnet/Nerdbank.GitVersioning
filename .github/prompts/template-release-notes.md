# Template release notes

This file will describe significant changes in Library.Template as they are introduced, especially if they require special consideration when merging updates into existing repos.
This file is referenced by update-library-template.prompt.md and should remain in place to facilitate future merges, whether done manually or by AI.

## Solution rename

Never leave a Library.slnx file in the repository.
You might even see one there even though this particular merge didn't bring it in.
This can be an artifact of having renamed Library.sln to Library.slnx in the template repo, but ultimately the receiving repo should have only one .sln or .slnx file, with a better name than `Library`.
Delete any `Library.slnx` that you see.
Migrate an `.sln` in the repo root to `.slnx` using this command:

```ps1
dotnet solution EXISTING.sln migrate
```

This will create an EXISTING.slnx file. `git add` that file, then `git rm` the old `.sln` file.
Sometimes a repo will reference the sln filename in a script or doc somewhere.
Search the repo for such references and update them to the slnx file.
