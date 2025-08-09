# Shallow cloning agents

Agents like Dependabot and GitHub Copilot Coding agent will operate on your repo using a shallow clone.
Since Nerdbank.GitVersioning dependencies will fail on shallow clones, this can break or at least slow down these agents.

For most repos, setting the `NBGV_GitEngine=Disabled` environment variable is an effective way to unblock these agents.
This does not completely remove Nerdbank.GitVersioning from your build, but it removes all access to git history and thus allows your builds to succeed in a shallow clone.

A few caveats with this:

* Version stamps will be incorrect.
* The generated `ThisAssembly` class will still be generated, but with [fewer properties](https://github.com/dotnet/Nerdbank.GitVersioning/issues/1192) (e.g. `GitCommitId`) since that information is not available.

## GitHub Copilot Coding Agent

To configure the Copilot Coding Agent to set this environment variable, follow these steps to set environment variables for the `copilot` environment:

1. Navigate to your GitHub repo's Settings tab.
1. Select Environments from the list on the left.
1. Select the `copilot` environment. You may have to create this environment yourself if you have not yet assigned the Copilot Coding Agent an issue to work on in your repo.
1. Find the "Environment variables" section.
1. Add an environment variable. Give it the name `NBGV_GitEngine` and the value `Disabled`.

See also [GitHub Copilot Coding Agent docs](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent/customize-the-agent-environment#setting-environment-variables-in-copilots-environment) for setting environment variables.

## Dependabot

Dependabot does not yet allow configuring custom environment variables for its runtime environment.
Consider up-voting [this issue](https://github.com/dependabot/dependabot-core/issues/4660).
Be sure to vote up the top-level issue description as that tends to be the tally that maintainers pay attention to.
But you may also upvote [this particular comment](https://github.com/dependabot/dependabot-core/issues/4660#issuecomment-3170935213) that describes our use case.
