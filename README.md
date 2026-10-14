# Ideal Points

A live leaderboard for my Sleeper fantasy league, ranked on total points, plus two extra stats: how consistent each team is, and how many points each team left on the bench. .NET 10 Azure Functions and React, for about $0.25 a month.

**[Live site](https://ff.jacktreadwell.com)** · **[Project write-up](https://jacktreadwell.com/FantasyFootballWebsite/)**

![The live site](site/images/overview.png)

## Run it locally

```bash
cd web && npm ci && npm run dev    # uses the deployed API, so no backend needed
```

For the API, put `League__CurrentLeagueId` in `src/FantasyLeague.Functions/local.settings.json` (git-ignored), start Azurite, then run `func start` in that folder. League and user IDs never go in the repo (A2).

Tests: `dotnet test` and `npm test --prefix web`.

## Docs

[Design](docs/design.md) · [Amendments](docs/amendments.md) · [Standards](docs/standards.md) · [Runbook](docs/runbook.md) · [Tasks](tasks/todo.md)
