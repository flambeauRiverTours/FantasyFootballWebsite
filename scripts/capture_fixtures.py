#!/usr/bin/env python3
"""Capture real Sleeper payloads into tests/fixtures/.

Usage:
    export League__CurrentLeagueId=...        # the name the app and CLI read; never hard-code it (A2)
    python scripts/capture_fixtures.py              # every week up to the current one
    python scripts/capture_fixtures.py 1 2 3        # just those weeks

Writes:
    tests/fixtures/wk{N}-matchups.json    the payload verbatim
    tests/fixtures/roster-positions.json  roster_positions only, lifted out of the league
    tests/fixtures/league-settings.json   which weeks the season plays, named fields only (T64)
    tests/fixtures/rosters-record.json    each roster's W-L-T and points, by roster number (T68)
    tests/fixtures/previous-season.json   the same settings and records for last season, if any (T68)
    tests/fixtures/player-positions.json  id -> name/team/fantasyPositions

Needs no jq. Standard library only.

Two things this script exists to get right:

1.  `player-positions.json` is rebuilt over the UNION of every captured week,
    not just the ones fetched this run. Rosters change on waivers, so a map
    built from week 1 alone goes stale and later weeks reference players it does
    not contain.

2.  Nothing carrying the league ID or a user ID is ever written. The league
    object holds `league_id`, so only its `roster_positions` array and the named
    `SEASON_SETTINGS` are kept; a roster keeps its number and the named
    `ROSTER_SETTINGS`, never its owner; and /league/{id}/users is not fetched
    at all.
"""

from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

API = "https://api.sleeper.app/v1"
ROOT = Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "tests" / "fixtures"
NOT_STARTABLE = {"BN", "IR", "TAXI"}
# the settings that say which weeks a season plays; named, so nothing else can carry an id to disk.
# last_scored_leg and leg are not: they count the weeks played so far, and change every week
SEASON_SETTINGS = (
    "start_week",
    "playoff_week_start", "playoff_round_type", "playoff_teams", "playoff_type",
    "league_average_match", "divisions",
)
# a roster's record as Sleeper keeps it (T68); named, so owner_id and co_owners never reach disk
ROSTER_SETTINGS = (
    "wins", "losses", "ties",
    "fpts", "fpts_decimal", "fpts_against", "fpts_against_decimal",
    "division",
)


def fail(message: str) -> None:
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(1)


def league_id() -> str:
    value = os.environ.get("League__CurrentLeagueId", "").strip()
    if not value:
        fail(
            "League__CurrentLeagueId is not set.\n"
            "  Git Bash:   export League__CurrentLeagueId=...\n"
            "  PowerShell: $env:League__CurrentLeagueId = '...'\n"
            "It is a secret and must not be committed - see docs/amendments.md, A2."
        )
    return value


def get(path: str):
    url = f"{API}/{path}"
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as exc:
        # Never echo the URL; it contains the league ID.
        fail(f"HTTP {exc.code} fetching {path.split('/')[0]}")
    except urllib.error.URLError as exc:
        fail(f"network error: {exc.reason}")


def write(name: str, payload) -> None:
    FIXTURES.mkdir(parents=True, exist_ok=True)
    path = FIXTURES / name
    # CRLF, as .gitattributes checks every text file out
    with path.open("w", encoding="utf-8", newline="\r\n") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")
    print(f"  {name:<26} {path.stat().st_size:>9,} bytes")


def records(lid: str) -> tuple[list[dict], set[str]]:
    """Each roster's number and named settings only, plus every owner id seen, for the final check."""
    rosters = get(f"league/{lid}/rosters")
    kept = []
    for r in sorted(rosters, key=lambda r: r["roster_id"]):
        settings = r.get("settings") or {}
        kept.append({"roster_id": r["roster_id"], "settings": {k: settings[k] for k in ROSTER_SETTINGS if k in settings}})
    owners = {o for r in rosters for o in [r.get("owner_id"), *(r.get("co_owners") or [])] if o}
    return kept, owners


def show(label: str, kept: list[dict]) -> None:
    for r in kept:
        s = r["settings"]
        print(f"  {label} roster {r['roster_id']}: {s.get('wins', '?')}-{s.get('losses', '?')}-{s.get('ties', '?')}")


def main() -> None:
    lid = league_id()

    state = get("state/nfl")
    current_week = int(state.get("week") or 1)
    season = state.get("season")

    weeks = [int(a) for a in sys.argv[1:]] or list(range(1, current_week + 1))
    print(f"season {season}, current week {current_week}")
    print(f"capturing weeks: {', '.join(str(w) for w in weeks)}\n")

    for week in weeks:
        write(f"wk{week}-matchups.json", get(f"league/{lid}/matchups/{week}"))

    league = get(f"league/{lid}")
    write(
        "roster-positions.json",
        {"season": league["season"], "roster_positions": league["roster_positions"]},
    )

    # T64
    settings = league.get("settings") or {}
    kept = {k: settings[k] for k in SEASON_SETTINGS if k in settings}
    write("league-settings.json", {"season": league["season"], "settings": kept})
    # raw values only; RegularSeason decides what they mean. Every week-like setting is listed,
    # numbers only, so a field not in SEASON_SETTINGS still shows up here
    for key in sorted(k for k in settings if "week" in k or "leg" in k or k in SEASON_SETTINGS):
        value = settings[key]
        print(f"  {key}: {value if isinstance(value, (int, float)) else type(value).__name__}")

    # T68: Sleeper's own records, to check ours against
    kept, owners = records(lid)
    write("rosters-record.json", {"season": league["season"], "rosters": kept})
    show(league["season"], kept)

    # last season settles whether Sleeper's W-L counts playoff games; the link itself is never written
    secrets = {lid, *owners}
    previous = league.get("previous_league_id")
    if previous and previous != "0":
        secrets.add(previous)
        past = get(f"league/{previous}")
        past_settings = past.get("settings") or {}
        past_kept, past_owners = records(previous)
        secrets |= past_owners
        write("previous-season.json", {
            "season": past["season"],
            "settings": {k: past_settings[k] for k in SEASON_SETTINGS if k in past_settings},
            "rosters": past_kept,
        })
        show(past["season"], past_kept)
    else:
        print("  no previous season linked")

    # Union of every captured week on disk, not merely this run's.
    rostered: set[str] = set()
    captured = sorted(FIXTURES.glob("wk*-matchups.json"))
    for path in captured:
        for entry in json.loads(path.read_text(encoding="utf-8")):
            rostered.update(p for p in (entry.get("players") or []) if p != "0")
            rostered.update(p for p in (entry.get("starters") or []) if p != "0")

    print(f"\n  {len(rostered)} distinct players across {len(captured)} captured week(s)")
    print("  fetching the player dump (~15 MB, once)")
    dump = get("players/nfl")

    missing = sorted(pid for pid in rostered if pid not in dump)
    if missing:
        fail(f"{len(missing)} rostered player(s) absent from the dump: {missing[:5]}")

    write(
        "player-positions.json",
        {
            pid: {
                "name": dump[pid].get("full_name") or pid,
                "team": dump[pid].get("team"),
                "fantasyPositions": dump[pid].get("fantasy_positions") or [],
            }
            for pid in sorted(rostered)
        },
    )

    # Belt and braces: refuse to leave a secret on disk. Never name which one (A2).
    leaked = [
        path for path in FIXTURES.glob("*.json")
        if any(s in path.read_text(encoding="utf-8") or s in path.name for s in secrets)
    ]
    for path in leaked:
        path.unlink()
    if leaked:
        fail(f"{', '.join(p.name for p in leaked)} contained a league or user ID and were deleted")

    slots = [c for c in league["roster_positions"] if c not in NOT_STARTABLE]
    print(f"\n  {len(slots)} startable slots: {', '.join(slots)}")
    print("  no league or user IDs written")
    print("\n  delete the week still in progress: a fixture is a final week")


if __name__ == "__main__":
    main()
