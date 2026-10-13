# Running this for your own league

This site reads one Sleeper league and puts it on a web page. You can run your
own copy. This guide assumes you have never deployed anything before.

Set aside two hours for the first setup. Most of it is filling in forms. After
that it needs about ten minutes of attention a year.

---

## What you need first

Four accounts. All free.

1. **A Sleeper league.** The one you want to display.
2. **A GitHub account.** This is where the code lives. github.com.
3. **A Microsoft Azure account.** This is where the site runs. azure.com. Signing
   up asks for a card. You will not be charged if you follow this guide, but the
   card is required to open the account.
4. **A computer with the setup tools installed.** Three of them:
   - Git, which copies the code down. git-scm.com.
   - The Azure command line, which talks to Azure from your keyboard. Search
     "install Azure CLI".
   - The GitHub command line, which talks to GitHub the same way. cli.github.com.

On Windows, run every command in this guide in **Git Bash**, which Git installs
for you. Not PowerShell, and not the black Command Prompt window. Commands that
work in one will silently do the wrong thing in another. That has already cost
this project a day.

---

## Step 1. Get your own copy of the code

Make your own copy of the project on GitHub. Press **Fork** at the top of the
repository page.

Then pull it down to your computer:

```bash
git clone https://github.com/<your-username>/FantasyFootballWebsite.git
cd FantasyFootballWebsite
```

---

## Step 2. Find your league number

Open your league on sleeper.com. Look at the address bar. It reads something
like `sleeper.com/leagues/1234567890123456789/team`. The long number is your
league ID.

Copy it somewhere. You will paste it in Step 5.

**Treat this number as private.** Anyone with it can read your league from
Sleeper's servers without asking you. Never put it in a file, a message, or a
commit.

---

## Step 3. Sign in to Azure and make a home for the site

Sign in:

```bash
az login
```

A browser window opens. Sign in there, then come back.

Now make a folder in Azure to hold everything. Azure calls it a resource group.
Everything the site uses will live inside it, so deleting the group later deletes
the whole site cleanly.

```bash
az group create --name rg-myleague --location eastus2
```

Pick your own name in place of `rg-myleague`. Write it down.

---

## Step 4. Let GitHub deploy on your behalf

GitHub needs permission to build the site into your Azure account. You are
creating an ID card for GitHub and telling Azure to accept it. Azure calls this
an app registration with federated credentials. No password is stored anywhere.

Create the ID:

```bash
az ad app create --display-name "fantasy-deploy"
```

Copy the `appId` from the output. Then give it permission over your folder:

```bash
APP_ID=<the appId you copied>
SUB=$(az account show --query id -o tsv | tr -d '\r')

az ad sp create --id "$APP_ID"
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUB/resourceGroups/rg-myleague"
```

The site reaches its storage with its own identity rather than a key, so the
deploy has to hand that identity three storage roles. Contributor cannot hand
out roles. This gives the deploy that one extra power, limited to exactly those
three roles and nothing else:

```bash
ROLES="b7e6dc6d-f1e8-4753-8033-0f276bb0955b, 974c5e8b-45b9-4653-ba55-5f855dd0fb88, 0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3"
CONDITION="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ROLES})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ROLES}))"

az role assignment create --assignee "$APP_ID" \
  --role "Role Based Access Control Administrator" \
  --scope "/subscriptions/$SUB/resourceGroups/rg-myleague" \
  --condition "$CONDITION" --condition-version "2.0"
```

The three numbers are Azure's own IDs for Storage Blob Data Owner, Storage Queue
Data Contributor and Storage Table Data Contributor. They are the same in every
Azure account.

Now tell Azure which GitHub actions to trust. You need your GitHub username and
the numeric IDs GitHub uses internally.

```bash
gh auth login
OWNER=$(gh api user --jq .login)
OWNER_ID=$(gh api user --jq .id)
REPO_ID=$(gh api "repos/$OWNER/FantasyFootballWebsite" --jq .id)
```

Create three trust entries. One for the main branch, one for pull requests, one
for deployments.

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

Those subjects look strange. They have to match exactly what GitHub sends, down
to the numeric IDs. If a deploy later fails with `AADSTS700213`, the subject did
not match, and the deploy log prints the one GitHub actually sent.

---

## Step 5. Store your settings in GitHub

GitHub keeps these hidden. They never appear in the code or in the logs. GitHub
calls them secrets.

You need your own ID as a person in Azure, so the site's database knows who its
owner is:

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

The `tr -d` part strips an invisible character that Windows adds to the end of
every line. Leave it in. Without it the value is stored with a stray character on
the end and nothing works, with no useful error.

### If your league has a placeholder team

Some leagues carry a dummy team that nobody plays. It should not appear in the
standings or drag the league averages around.

Find its owner:

```bash
curl -s "https://api.sleeper.app/v1/league/<your league number>/users" \
  | grep -o '"user_id":"[0-9]*"'
```

Match the ID to the team and store it. Several IDs are separated by commas:

```bash
gh secret set EXCLUDED_OWNER_IDS --body "123456789,987654321"
```

If you have no dummy team, set it to an empty value and move on.

---

## Step 6. Deploy

Push a change to the main branch and GitHub builds everything. The first run
takes about ten minutes.

```bash
git commit --allow-empty -m "first deploy"
git push
```

Watch it:

```bash
gh run watch
```

When it finishes, get your web address:

```bash
az staticwebapp list -g rg-myleague --query "[0].defaultHostname" -o tsv
```

Open it in a browser. You should see your standings.

### Set the budget alert, once

This emails you if the month's bill passes fifty cents. It is set up once by
hand rather than on every deploy, because Azure will not let a budget's start
date change once it exists:

```bash
az deployment group create -g rg-myleague --template-file infra/budget.bicep \
  --parameters alertEmail="you@example.com" startDate="$(date -u +%Y-%m-01)"
```

To change the budget later, run it again with the date it was created with in
place of `$(date …)`. `az consumption budget list -g rg-myleague` shows it.

---

## Step 7. Use your own web address (optional)

The site works fine on its `*.azurestaticapps.net` address. A shorter one is
nicer to paste into a group chat.

You need a domain you already own. A **subdomain** of it is far easier than the
bare name, so use something like `ff.example.com`.

### The DNS record

At your registrar, add one record. Not an `A` or `AAAA` record: those point at a
fixed address, and this host does not have one.

| Field | Value |
|---|---|
| Type | `CNAME` |
| Name | `ff` |
| Value | the address from Step 6 |
| TTL | 600 while you set it up |

Enter only the first part in the Name field. GoDaddy adds the rest of your domain
itself, so typing the full name gives you `ff.example.com.example.com`.

### Tell Azure

```bash
SWA=$(az staticwebapp list -g rg-myleague --query "[0].name" -o tsv | tr -d '\r')
az staticwebapp hostname set -n "$SWA" -g rg-myleague --hostname ff.example.com
az staticwebapp hostname list -n "$SWA" -g rg-myleague -o table
```

Wait for `Ready` rather than `Validating`. The certificate is issued and renewed
for you.

### Tell the data side, or nothing will load

This is the step people miss. The page and the data live at two addresses, and
the data side keeps a list of who may ask it. Your new address is not on that
list yet, so the page will load and stay empty.

Store the address and deploy again:

```bash
gh variable set CUSTOM_DOMAIN_NAME --body "ff.example.com"
git commit --allow-empty -m "use the custom domain"
git push
```

The deploy adds the address to the allowed list and checks it worked. If you skip
this, the browser console says `blocked by CORS policy`.

---

## What it costs

**About twenty-five cents a month.** Almost every piece runs on a free
allowance. The storage the site needs costs about fifteen cents, and the email
alert on errors about ten.

| Piece | What it does | Free allowance |
|---|---|---|
| Static Web Apps | Serves the page | Always free at this size |
| Functions | Fetches from Sleeper | 1 million calls a month |
| Azure SQL | Remembers finished weeks | 100,000 seconds a month, free offer |
| Application Insights | Records errors | 5 GB a month |
| Storage | Holds the site's working files | None: about $0.15 a month |
| Error alert | Emails you when the site fails | None: about $0.10 a month |

A six-team league watched by a dozen people uses a small fraction of each.

Three safety nets are built in, so a mistake costs you an outage rather than a
bill.

1. **The database pauses itself** when it runs out of its free hours instead of
   billing you. It comes back on the next visit, taking up to a minute.
2. **Error recording stops** at a fixed daily amount. You lose that day's error
   log. You do not get an invoice.
3. **An email warns you** if the bill ever passes fifty cents.

A fourth email, separate from the bill, tells you when the site itself throws
an error.

All of them are set by the code, so a setting changed by clicking in the Azure
website is put back the next time the site deploys. The budget is the exception:
it is set once in Step 6.

The one thing that does cost money is a domain name, about fifteen dollars a
year. Step 7 is optional and the site works fine without it. A subdomain of a
domain you already own costs nothing extra.

---

## Ongoing maintenance

Almost none. Here is all of it.

### Every August

When your league rolls over to a new season, tell the site about it. This is the
only scheduled job of the year.

1. In Sleeper, create the new season using the **roll over** option rather than
   starting a fresh league. Rolling over keeps the link back to last year, which
   is how old seasons stay visible.
2. Get the new league number from the address bar, as in Step 2.
3. Point the site at it:

```bash
FUNC=$(az functionapp list -g rg-myleague --query "[0].name" -o tsv | tr -d '\r')
az functionapp config appsettings set -n "$FUNC" -g rg-myleague \
  --settings "League__CurrentLeagueId=<new league number>"
```

4. Check the new year shows up:

```bash
curl -s "https://$(az functionapp list -g rg-myleague \
  --query "[0].defaultHostName" -o tsv | tr -d '\r')/api/seasons"
```

The site looks backwards from whatever league you point it at. It will never find
a new season on its own. Step 3 is the whole job.

### Whenever GitHub offers

A robot opens requests to update the building blocks the site uses. Dependabot.
Merge them when the checks pass. Ignore them and nothing breaks for a long while.

### Never

You do not need to restart anything, back anything up, or renew anything that
expires.
Finished weeks are saved in the database, so a Sleeper outage leaves old weeks on
screen and marks the current week as stale.

---

## When something looks wrong

### The page loads but the table is empty

The page and the data come from two different addresses, and the data side has a
list of who may ask it. Yours may not be on it. This is a CORS problem, and the
browser console will say so.

```bash
FUNC=$(az functionapp list -g rg-myleague --query "[0].name" -o tsv | tr -d '\r')
az functionapp cors show -n "$FUNC" -g rg-myleague
```

Almost always this is Step 7. Either `CUSTOM_DOMAIN_NAME` is not set, or it is
set but nothing has deployed since. Ask the data side directly:

```bash
curl -fsS -D - -o /dev/null -H "Origin: https://ff.example.com" \
  "https://$(az functionapp list -g rg-myleague \
  --query "[0].defaultHostName" -o tsv | tr -d '\r')/api/season" \
  | grep -i access-control-allow-origin
```

No line back means the address is not allowed. Push a deploy.

### It is very slow the first time each day

Expected. The database goes to sleep after an hour of quiet and takes up to a
minute to wake. The page says so while it waits. Every visit after that is fast.

### A deploy fails with a long error starting AADSTS

The ID card from Step 4 did not match. The failing deploy log prints the exact
text GitHub sent, under **Federated token details**. Compare it with what Azure
has:

```bash
az ad app federated-credential list --id "$APP_ID" --query "[].{name:name, subject:subject}"
```

Adding a deployment environment or changing a branch name changes what GitHub
sends. Each one needs its own entry.

### The standings look wrong

Check the dummy team from Step 5 is excluded. If it is in the list, it drags
every league average.

---

## Things to keep private

Your league number and the Sleeper IDs of your league members. Both are in the
secrets you set in Step 5, which is the right place for them. Neither should ever
appear in a file you commit.

A check runs on every change and looks for them in the code. It reports which
file and line, never the value itself. It has already caught one real mistake in
this project.

No other keys are stored anywhere. The database and the storage both recognise
the site by its identity, and each deploy fetches the page's upload key fresh.
If you think that key has leaked, replace it; the next deploy picks up the new
one by itself:

```bash
SWA=$(az staticwebapp list -g rg-myleague --query "[0].name" -o tsv | tr -d '\r')
az staticwebapp secrets reset-api-key -n "$SWA" -g rg-myleague
```

---

## Where to read more

- `README.md` explains what the three numbers on the page mean.
- `docs/design.md` explains why the site is built this way.
- `docs/field-manual.md` is the build itself, task by task.
