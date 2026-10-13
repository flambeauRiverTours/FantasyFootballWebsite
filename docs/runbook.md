# Running this for your own league

The site reads one Sleeper league and renders it as a web page. This guide deploys a copy and assumes no prior deployment experience.

Scope: the regular season only. Fantasy playoffs and the NFL postseason are out of scope (Q4).

First setup: ≈ 2 h, mostly form entry. Upkeep afterwards: ≈ 10 min/year.

---

## What you need first

Four accounts, all free.

1. **A Sleeper league.** The one to display.
2. **A GitHub account.** Hosts the code. github.com.
3. **A Microsoft Azure account.** Runs the site. azure.com. Sign-up requires a card; the site costs ≈ $0.25/month, with an alert at $0.50.
4. **A computer with three tools installed:**
   - Git, to copy the code. git-scm.com.
   - Azure CLI, to control Azure from the terminal. Search "install Azure CLI".
   - GitHub CLI, to control GitHub from the terminal. cli.github.com.

On Windows, run every command in **Git Bash** (installed with Git). Not PowerShell, not Command Prompt: these commands silently misbehave in other shells. This has already cost the project a day.

---

## Step 1. Get your own copy of the code

Press **Fork** at the top of the GitHub repository page. Then clone it:

```bash
git clone https://github.com/<your-username>/FantasyFootballWebsite.git
cd FantasyFootballWebsite
```

---

## Step 2. Find your league number

Open the league on sleeper.com. The address bar reads `sleeper.com/leagues/<league-id>/team`. The long number is the league ID. Save it for Step 5.

**The number is private.** Anyone holding it can read the league from Sleeper's servers. Never put it in a file, a message, or a commit.

---

## Step 3. Sign in to Azure and make a home for the site

```bash
az login
```

Sign in in the browser window that opens.

Create a resource group: an Azure container for every resource the site uses. Deleting the group deletes the whole site.

```bash
az group create --name rg-myleague --location eastus2
```

Replace `rg-myleague` with your own name and record it.

---

## Step 4. Let GitHub deploy on your behalf

GitHub needs permission to deploy into the Azure account. An app registration with federated credentials gives GitHub an identity that Azure accepts. No password is stored.

Create the identity:

```bash
az ad app create --display-name "fantasy-deploy"
```

Copy `appId` from the output. Grant it Contributor on the resource group:

```bash
APP_ID=<the appId you copied>
SUB=$(az account show --query id -o tsv | tr -d '\r')

az ad sp create --id "$APP_ID"
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUB/resourceGroups/rg-myleague"
```

The site reaches storage with its own identity, not a key, so the deploy must grant that identity three storage roles. Contributor cannot assign roles. The command below adds role-assignment rights, restricted to those three roles only:

```bash
ROLES="b7e6dc6d-f1e8-4753-8033-0f276bb0955b, 974c5e8b-45b9-4653-ba55-5f855dd0fb88, 0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3"
CONDITION="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ROLES})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ROLES}))"

az role assignment create --assignee "$APP_ID" \
  --role "Role Based Access Control Administrator" \
  --scope "/subscriptions/$SUB/resourceGroups/rg-myleague" \
  --condition "$CONDITION" --condition-version "2.0"
```

The three GUIDs are Azure's built-in IDs for Storage Blob Data Owner, Storage Queue Data Contributor and Storage Table Data Contributor. They are identical in every Azure account.

Next, tell Azure which GitHub workflows to trust. This needs the GitHub username and GitHub's numeric user and repository IDs:

```bash
gh auth login
OWNER=$(gh api user --jq .login)
OWNER_ID=$(gh api user --jq .id)
REPO_ID=$(gh api "repos/$OWNER/FantasyFootballWebsite" --jq .id)
```

Create three trust entries: main branch, pull requests, production environment.

```bash
for entry in \
  "gh-main:repo:$OWNER@$OWNER_ID/FantasyFootballWebsite@$REPO_ID:ref:refs/heads/main" \
  "gh-pr:repo:$OWNER@$OWNER_ID/FantasyFootballWebsite@$REPO_ID:pull_request" \
  "gh-env:repo:$OWNER@$OWNER_ID/FantasyFootballWebsite@$REPO_ID:environment:production"
do
  name="${entry%%:*}"
  subject="${entry#*:}"
  cat > fed.json <<JSON
{ "name": "$name",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "$subject",
  "audiences": ["api://AzureADTokenExchange"] }
JSON
  az ad app federated-credential create --id "$APP_ID" --parameters @fed.json
done
rm fed.json
```

Each subject must match what GitHub sends exactly, including the numeric IDs. A deploy failing with `AADSTS700213` means a subject mismatch; the deploy log prints the subject GitHub sent.

---

## Step 5. Store your settings in GitHub

GitHub secrets are hidden: they appear in neither the code nor the logs.

`AZ_SQL_ADMIN_OBJECT_ID` and `AZ_SQL_DB_ADMIN_NAME` are your own Azure user identity, which makes you the database owner.

```bash
gh secret set AZURE_CLIENT_ID          --body "$APP_ID"
gh secret set AZURE_TENANT_ID          --body "$(az account show --query tenantId -o tsv | tr -d '\r')"
gh secret set AZURE_SUBSCRIPTION_ID    --body "$SUB"
gh secret set AZ_RESOURCE_GROUP_NAME   --body "rg-myleague"
gh secret set AZ_SQL_ADMIN_OBJECT_ID   --body "$(az ad signed-in-user show --query id -o tsv | tr -d '\r')"
gh secret set AZ_SQL_DB_ADMIN_NAME     --body "$(az ad signed-in-user show --query userPrincipalName -o tsv | tr -d '\r')"
gh secret set BUDGET_ALERT_EMAIL       --body "you@example.com"   # where error alerts go; the deploy stops without it
gh secret set LEAGUE_ID                --body "<your league number from Step 2>"
```

Keep `tr -d '\r'`. On Windows the CLI appends an invisible carriage return to each line; without the strip, the stored value carries it and everything fails with no useful error.

### If your league has a placeholder team

A dummy team that nobody plays must not appear in the standings or affect league averages.

Find its owner:

```bash
curl -s "https://api.sleeper.app/v1/league/<your league number>/users" \
  | grep -o '"user_id":"[0-9]*"'
```

Match the ID to the team and store it. Separate several IDs with commas:

```bash
gh secret set EXCLUDED_OWNER_IDS --body "123456789,987654321"
```

With no dummy team, set it to an empty value.

---

## Step 6. Deploy

A push to main builds and deploys everything. The first run takes ≈ 10 min.

```bash
git commit --allow-empty -m "first deploy"
git push
```

Watch it:

```bash
gh run watch
```

Get the web address:

```bash
az staticwebapp list -g rg-myleague --query "[0].defaultHostname" -o tsv
```

Open it in a browser. The standings appear.

### Set the budget alert, once

Emails you if the monthly bill exceeds $0.50. Set once by hand, not per deploy, because Azure forbids changing a budget's start date after creation:

```bash
az deployment group create -g rg-myleague --template-file infra/budget.bicep \
  --parameters alertEmail="you@example.com" startDate="$(date -u +%Y-%m-01)"
```

To change the budget later, rerun it with the original creation date in place of `$(date …)`. `az consumption budget list -g rg-myleague` shows it.

---

## Step 7. Use your own web address (optional)

The site works on its `*.azurestaticapps.net` address. A custom address is shorter to share.

Requires a domain you already own. Use a **subdomain** such as `ff.example.com`; it is far simpler than the bare domain.

### The DNS record

At the registrar, add one record. Not `A` or `AAAA`: those point at a fixed IP, and this host has none.

| Field | Value |
|---|---|
| Type | `CNAME` |
| Name | `ff` |
| Value | the address from Step 6 |
| TTL | 600 while you set it up |

Enter only the subdomain part in Name. GoDaddy appends the domain itself; typing the full name yields `ff.example.com.example.com`.

### Tell Azure

```bash
SWA=$(az staticwebapp list -g rg-myleague --query "[0].name" -o tsv | tr -d '\r')
az staticwebapp hostname set -n "$SWA" -g rg-myleague --hostname ff.example.com
az staticwebapp hostname list -n "$SWA" -g rg-myleague -o table
```

Wait for `Ready` (not `Validating`). Azure issues and renews the certificate.

### Tell the data side, or nothing will load

The most commonly missed step. The page and the data are served from two addresses, and the data side keeps an allow-list of callers. Until the new address is on it, the page loads empty.

Store the address and redeploy:

```bash
gh variable set CUSTOM_DOMAIN_NAME --body "ff.example.com"
git commit --allow-empty -m "use the custom domain"
git push
```

The deploy adds the address to the allow-list and verifies it. Skipped, the browser console reports `blocked by CORS policy`.

---

## What it costs

**≈ $0.25/month.** Nearly every component runs on a free allowance. Storage costs ≈ $0.15/month; the error email alert ≈ $0.10/month.

| Piece | What it does | Free allowance |
|---|---|---|
| Static Web Apps | Serves the page | Always free at this size |
| Functions | Fetches from Sleeper | 1 million calls a month |
| Azure SQL | Remembers finished weeks | 100,000 seconds a month, free offer |
| Application Insights | Records errors | 5 GB a month |
| Storage | Holds the site's working files | None: about $0.15 a month |
| Error alert | Emails you when the site fails | None: about $0.10 a month |

A six-team league with ≈ 12 viewers uses a small fraction of each.

Three built-in safeguards make a mistake cost an outage, not a bill:

1. **The database pauses** when its free allowance runs out, instead of billing. It resumes on the next visit, in ≤ 1 min.
2. **Error recording stops** at a fixed daily cap. That day's error log is lost; no invoice is issued.
3. **An email warns** if the bill exceeds $0.50.

A fourth email, separate from billing, reports errors thrown by the site.

The code sets all of these, so a change made in the Azure portal is reverted on the next deploy. Exception: the budget, set once in Step 6.

The only paid item is a domain name, ≈ $15/year. Step 7 is optional. A subdomain of a domain you already own costs nothing extra.

---

## Ongoing maintenance

All of it is listed below.

### Every August

When the league rolls over to a new season, point the site at it. This is the only scheduled task of the year.

1. In Sleeper, create the new season with **roll over**, not as a fresh league. Rollover keeps the link to the previous season, which is how past seasons stay visible.
2. Get the new league number from the address bar, as in Step 2.
3. Point the site at it. Change the GitHub secret, not the Azure setting: every deploy rewrites the
   Azure setting from the secret, so an edit made only in Azure is undone by the next deploy.

```bash
gh secret set LEAGUE_ID --body "<new league number>"
gh workflow run deploy.yml
```

4. Check the new year appears:

```bash
curl -s "https://$(az functionapp list -g rg-myleague \
  --query "[0].defaultHostName" -o tsv | tr -d '\r')/api/seasons"
```

The site walks backwards from the configured league (A34). It never discovers a new season itself; item 3 is the whole job. Last season stays in the season picker, served from the database, once its weeks are stored.

### Whenever GitHub offers

Dependabot opens pull requests that update dependencies. Merge them when checks pass. Ignoring them breaks nothing for a long time.

### Never

No restarts, backups, or renewals of expiring items. Finished weeks are stored in the database, so a Sleeper outage leaves past weeks visible and marks the current week stale.

---

## When something looks wrong

### The page loads but the table is empty

A CORS problem: the data side's allow-list lacks the page's address. The browser console says so.

```bash
FUNC=$(az functionapp list -g rg-myleague --query "[0].name" -o tsv | tr -d '\r')
az functionapp cors show -n "$FUNC" -g rg-myleague
```

The usual cause is Step 7: `CUSTOM_DOMAIN_NAME` is unset, or set with no deploy since. Query the data side directly:

```bash
curl -fsS -D - -o /dev/null -H "Origin: https://ff.example.com" \
  "https://$(az functionapp list -g rg-myleague \
  --query "[0].defaultHostName" -o tsv | tr -d '\r')/api/season" \
  | grep -i access-control-allow-origin
```

No output means the address is not allowed. Push a deploy.

### It is very slow the first time each day

Expected. The database pauses after 1 h idle and takes ≤ 1 min to resume. The page shows a notice while it waits. Later visits are fast.

### A deploy fails with a long error starting AADSTS

A federated credential from Step 4 did not match. The failing deploy log prints the exact subject GitHub sent, under **Federated token details**. Compare it with Azure's entries:

```bash
az ad app federated-credential list --id "$APP_ID" --query "[].{name:name, subject:subject}"
```

A new deployment environment or a renamed branch changes the subject GitHub sends. Each needs its own entry.

### The standings look wrong

Check that the dummy team from Step 5 is excluded. Included, it skews every league average.

---

## Things to keep private

The league number and the league members' Sleeper user IDs. Both belong in the Step 5 secrets and never in a committed file.

A check runs on every change and scans the code for them. It reports file and line, never the value. It has already caught one real leak in this project.

No other keys are stored. The database and storage authenticate the site by its identity, and each deploy fetches the page's upload key fresh. If that key may have leaked, reset it; the next deploy picks up the new one automatically:

```bash
SWA=$(az staticwebapp list -g rg-myleague --query "[0].name" -o tsv | tr -d '\r')
az staticwebapp secrets reset-api-key -n "$SWA" -g rg-myleague
```

---

## Where to read more

- `README.md` explains what the three numbers on the page mean.
- `docs/design.md` explains why the site is built this way.
- `docs/field-manual.md` is the build itself, task by task.
