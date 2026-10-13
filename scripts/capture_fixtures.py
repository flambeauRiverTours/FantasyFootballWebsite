#!/usr/bin/env python3
"""Capture real Sleeper payloads into tests/fixtures/.

Usage:
    export League__CurrentLeagueId=...        # the name the app and CLI read; never hard-code it (A2)
    python scripts/capture_fixtures.py              # every week up to the current one
    python scripts/capture_fixtures.py 1 2 3        # just those weeks

Writes:
    tests/fixtures/wk{N}-matchups.json    the payload verbatim
    tests/fixtures/roster-positions.json  roster_positions only, lifted out of the league
    tests/fixtures/player-positions.json  id -> name/team/fantasyPositions

Needs no jq. Standard library only.

Two things this script exists to get right:

1.  `player-positions.json` is rebuilt over the UNION of every captured week,
    not just the ones fetched this run. Rosters change on waivers, so a map
    built from week 1 alone goes stale and later weeks reference players it does
    not contain.

2.  Nothing carrying the league ID or a user ID is ever written. The league
    object holds `league_id`, so only its `roster_positions` array is kept, and
    /league/{id}/users is not fetched at all.
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
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")
    print(f"  {name:<26} {path.stat().st_size:>9,} bytes")


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

    # Union of every captured week on disk, not merely this run's.
    rostered: set[str] = set()
    captured = sorted(FIXTURES.glob("wk*-matchups.json"))
    for path in captured:
        for entry in json.loads(path.read_text(encoding="utf-8")):
            rostered.update(p for p in (entry.get("players") or []) if p != "0")
            rostered.update(p for p in (entry.get("starters") or []) if p != "0")

    print(f"\n  {len(rostered)} distinct players across {len(captured)} captured week(s)")
    print("  fetching the player dump (~5 MB, once)")
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

    # Belt and braces: refuse to leave a secret on disk.
    for path in FIXTURES.glob("*.json"):
        if lid in path.read_text(encoding="utf-8") or lid in path.name:
            fail(f"{path.name} contains the league ID - not committing that")

    slots = [c for c in league["roster_positions"] if c not in NOT_STARTABLE]
    print(f"\n  {len(slots)} startable slots: {', '.join(slots)}")
    print("  no league or user IDs written")


if __name__ == "__main__":
    main()
