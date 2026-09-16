#!/usr/bin/env bash
# Checks the three settings that keep this project free.
# Run after any bicep deployment. A portal set value does not survive one.
#
#   RG=<resource-group> ./scripts/verify-cost.sh
set -uo pipefail

: "${RG:?set RG to the resource group}"

fail=0
ok()  { printf '  ok    %s\n' "$1"; }
bad() { printf '  FAIL  %s\n' "$1"; fail=1; }

echo "Telemetry ingestion cap"
# workspace based app insights caps on the workspace.
# the component has its own DailyCapGb which is 100 and does nothing here
ws=$(az monitor log-analytics workspace list -g "$RG" --query "[0].name" -o tsv 2>/dev/null | tr -d '\r')
if [ -z "$ws" ]; then
  bad "no log analytics workspace in $RG"
else
  quota=$(az monitor log-analytics workspace show -g "$RG" -n "$ws" \
          --query "workspaceCapping.dailyQuotaGb" -o tsv 2>/dev/null | tr -d '\r')
  if [ -z "$quota" ] || [ "$quota" = "-1.0" ]; then
    bad "$ws has no daily quota"
  elif awk "BEGIN{exit !($quota > 1)}"; then
    bad "$ws quota is ${quota} GB per day which will leave the free allocation"
  else
    ok "$ws capped at ${quota} GB per day"
  fi
fi

echo "Azure SQL free limit"
server=$(az sql server list -g "$RG" --query "[0].name" -o tsv 2>/dev/null | tr -d '\r')
if [ -z "$server" ]; then
  ok "no sql server deployed"
else
  # master is always present and is never the one we care about
  db=$(az sql db list -g "$RG" -s "$server" --query "[?name!='master'] | [0].name" -o tsv 2>/dev/null | tr -d '\r')
  if [ -z "$db" ]; then
    bad "$server has no user database"
  else
    read -r free behaviour pause <<<"$(az sql db show -n "$db" -s "$server" -g "$RG" \
      --query "[useFreeLimit, freeLimitExhaustionBehavior, autoPauseDelay]" -o tsv 2>/dev/null | tr -d '\r' | tr '\n' ' ')"
    # a runaway job should cost a paused database rather than a bill
    # az lowercases booleans in tsv
    if [ "${free,,}" != "true" ]; then
      bad "$db is not on the free limit"
    elif [ "$behaviour" != "AutoPause" ]; then
      bad "$db exhaustion behaviour is $behaviour"
    else
      ok "$db auto pauses after ${pause} minutes idle"
    fi
  fi
fi

echo "Budget alert"
# an empty budgetAlertEmail skips the resource entirely so the alarm is simply absent
emails=$(az consumption budget list -g "$RG" \
  --query "length([].notifications.*.contactEmails[][])" -o tsv 2>/dev/null | tr -d '\r')
if [ -z "$emails" ] || [ "$emails" = "0" ]; then
  bad "no budget contact in $RG. BUDGET_ALERT_EMAIL is probably unset in deploy.yml"
else
  ok "budget present with ${emails} contact address(es)"
fi

echo
if [ "$fail" -eq 0 ]; then
  echo "All cost guardrails in place."
else
  echo "One or more guardrails are missing. See above."
fi
exit "$fail"
