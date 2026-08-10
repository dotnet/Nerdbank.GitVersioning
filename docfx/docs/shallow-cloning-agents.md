# Shallow cloning agents

Agents like Dependabot and GitHub Copilot Coding agent will operate on your repo using a shallow clone.
Since Nerdbank.GitVersioning dependencies will fail on shallow clones, this can break or at least slow down these agents.

For most repos, setting the `NBGV_GitEngine=Disabled` environment variable is an effective way to unblock these agents.
This does not completely remove Nerdbank.GitVersioning from your build, but it removes all access to git history and thus allows your builds to succeed in a shallow clone.

Note that the environment variable name and value are _case sensitive_.

A few caveats with this:

* Version stamps will be incorrect.
* The generated `ThisAssembly` class will still be generated, but with [fewer properties](https://github.com/dotnet/Nerdbank.GitVersioning/issues/1192) (e.g. `GitCommitId`) since that information is not available.

## GitHub Copilot Coding Agent

**As of Nerdbank.GitVersioning v3.9, the git engine is automatically disabled when running under GitHub Copilot**, eliminating the need for manual configuration in most cases.

Specifically, when the `GITHUB_ACTOR` environment variable is set to `copilot-swe-agent[bot]` and the `NBGV_GitEngine` environment variable is **not** set, Nerdbank.GitVersioning automatically behaves as if `NBGV_GitEngine=Disabled`. This ensures that GitHub Copilot runs succeed without any additional setup.

If you need to override this behavior for any reason, you can explicitly set the `NBGV_GitEngine` environment variable to your desired value, which will take precedence over the automatic GitHub Copilot detection.

### Manual configuration (optional)

If automatic detection doesn't work for your scenario, you can manually configure the Copilot Coding Agent to set the environment variable by following these steps to set environment variables for the `copilot` environment:

1. Navigate to your GitHub repo's Settings tab.
1. Select Environments from the list on the left.
1. Select the `copilot` environment. You may have to create this environment yourself if you have not yet assigned the Copilot Coding Agent an issue to work on in your repo.
1. Find the "Environment variables" section.
1. Add an environment variable. Give it the name `NBGV_GitEngine` and the value `Disabled`. Note these are _case sensitive_.

See also [GitHub Copilot Coding Agent docs](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent/customize-the-agent-environment#setting-environment-variables-in-copilots-environment) for setting environment variables.

## Dependabot

**As of Nerdbank.GitVersioning v3.9, the git engine is automatically disabled when running under Dependabot**, eliminating the need for manual configuration in most cases.

Specifically, when the `DEPENDABOT` environment variable is set to `true` (case-insensitive) and the `NBGV_GitEngine` environment variable is **not** set, Nerdbank.GitVersioning automatically behaves as if `NBGV_GitEngine=Disabled`. This ensures that Dependabot runs succeed without any additional setup.

If you need to override this behavior for any reason, you can explicitly set the `NBGV_GitEngine` environment variable to your desired value, which will take precedence over the automatic Dependabot detection.

### Background

Dependabot does not yet allow configuring custom environment variables for its runtime environment.
Consider up-voting [this issue](https://github.com/dependabot/dependabot-core/issues/4660).
Be sure to vote up the top-level issue description as that tends to be the tally that maintainers pay attention to.
But you may also upvote [this particular comment](https://github.com/dependabot/dependabot-core/issues/4660#issuecomment-3170935213) that describes our use case.

There is [a known workaround](https://github.com/dependabot/dependabot-core/issues/4660#issuecomment-3399907801), but with the automatic detection feature, this workaround should no longer be necessary for most users.
