# Understanding version calculation

This package calculates the version based on a combination of the version.json file,
the git 'height' of the version, and the git commit ID.
The height can optionally be incremented only for those [commits that change certain paths](path-filters.md).

## Version generation

Given the same settings as used in the discussion above, a NuGet or NPM package may be
assigned this version:

    1.0.24-alpha-g9a7eb6c819

When built as a public release, the git commit ID is dropped:

    1.0.24-alpha

Learn more about [public releases versus prereleases](public-vs-stable.md).
