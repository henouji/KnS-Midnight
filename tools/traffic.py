#!/usr/bin/env python3
"""Read the server's traffic log (work/traffic/<yyyy-MM-dd>.jsonl), written by server-dotnet.

Every request is one JSON line with its reply; game gateway lines hold the decrypted request
and reply, the protocol name, the parameters the server never read ("unread"), handler notes
(the stage drop audit) and warnings (drops the client reported that the server did not keep).
Item and fellow ids are shown with their names from server/seed.

Usage (from the repository root):
    python tools/traffic.py                      # today's exchanges, one line each
    python tools/traffic.py -p 71,72 -v          # stage starts / results with request and reply
    python tools/traffic.py --drops              # stage runs: rolled, offered, reported, granted
    python tools/traffic.py --warnings           # only the exchanges with findings
    python tools/traffic.py --seq 57             # one exchange in full
    python tools/traffic.py -f                   # follow the file as the server writes it
    python tools/traffic.py --day 2026-10-24 -u 10001 --surface gateway
    python tools/traffic.py path/to/2026-10-24.jsonl   # a file copied from a hosted server
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import json
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SEED = ROOT / "server" / "seed"
FELLOW_MAX_ID = 199999


def load_names() -> dict[int, str]:
    names: dict[int, str] = {}
    for file, key in [("master_items.csv", "item_id"), ("master_etc_items.csv", "item_id"),
                      ("master_materials.csv", "item_id"), ("master_fellows.csv", "fellow_id")]:
        try:
            with open(SEED / file, encoding="utf-8", newline="") as handle:
                for row in csv.DictReader(handle):
                    try:
                        names[int(row[key])] = row.get("name") or ""
                    except (KeyError, ValueError):
                        pass
        except OSError:
            pass
    return names


NAMES = load_names()


def named(item_id) -> str:
    try:
        name = NAMES.get(int(item_id))
    except (TypeError, ValueError):
        return str(item_id)
    return f"{item_id} {name}" if name else str(item_id)


def entries(path: Path, follow: bool):
    with open(path, encoding="utf-8-sig") as handle:
        partial = ""
        while True:
            line = partial + handle.readline()
            partial = ""
            if not line.endswith("\n"):
                if not follow:
                    if line.strip():
                        print("(last line incomplete)", file=sys.stderr)
                    return
                # Nothing new yet, or the server is mid-write: keep what came and wait.
                partial = line
                time.sleep(0.3)
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError as error:
                print(f"(unreadable line: {error})", file=sys.stderr)


def summary(entry: dict) -> str:
    clock = entry.get("time", "")[11:23]
    status = entry.get("status")
    head = f"#{entry.get('seq')} {clock} {entry.get('ms', 0):>6}ms"
    if entry.get("surface") == "gateway":
        user = entry.get("user_no")
        name = entry.get("name") or "?"
        errno = entry.get("errno")
        text = f"{head} u{user if user is not None else '-'} {entry.get('protocol')} {name}"
        if errno not in (0, None):
            text += f" errno={errno} {entry.get('error', '')}".rstrip()
    else:
        text = f"{head} {entry.get('surface')} {entry.get('method')} {entry.get('path')}"
    if status != 200:
        text += f" [{status}]"
    if entry.get("failure"):
        text += f" FAILED {entry['failure']}"
    if entry.get("unread"):
        text += f"  unread: {','.join(entry['unread'])}"
    for warning in entry.get("warnings", []):
        text += f"\n    !! {warning}"
    return text


def dump(label: str, value) -> str:
    body = json.dumps(value, ensure_ascii=False, indent=2)
    return f"  {label}:\n" + "\n".join("    " + line for line in body.splitlines())


def verbose(entry: dict) -> str:
    parts = [summary(entry)]
    request = entry.get("request")
    if entry.get("surface") == "gateway" and isinstance(request, dict):
        request = request.get("params", request)
    parts.append(dump("request", request))
    if entry.get("notes"):
        parts.append(dump("notes", entry["notes"]))
    parts.append(dump("reply", entry.get("reply")))
    return "\n".join(parts)


def drops_view(entry: dict) -> str | None:
    """A stage start (71) or result (72) as a drop ledger."""
    protocol = entry.get("protocol")
    reply = entry.get("reply") or {}
    notes = entry.get("notes") or {}
    lines = [summary(entry)]
    if protocol == 71:
        params = (entry.get("request") or {}).get("params", {})
        lines.append(f"  stage {params.get('stage_id')} mode {params.get('stage_mode')}")
        rolled = notes.get("rolled", [])
        lines.append("  rolled:   " + (", ".join(f"{named(r['item_id'])} x{r['count']} ({r['kind']})" for r in rolled) or "nothing"))
        drop_list = reply.get("drop_item_list", {})
        for key, label in [("1", "chests"), ("4", "boss")]:
            offered = drop_list.get(key, [])
            if offered:
                lines.append(f"  {label}:   " + ", ".join(f"#{d['item_no']} {named(d['item_id'])}" for d in offered))
        events = reply.get("drop_event_item_list", [])
        if events:
            lines.append("  event:    " + ", ".join(f"#{e['item_no']} {named(e['item_id'])} x{e['count']}" for e in events))
        return "\n".join(lines)
    if protocol == 72:
        audit = notes.get("drops")
        params = (entry.get("request") or {}).get("params", {})
        lines.append(f"  stage {params.get('stage_no')} result={params.get('stage_result', 1)} reward_gold={params.get('reward_gold')}")
        if not audit:
            lines.append("  (no drop audit: written before the audit existed, or the handler failed)")
            return "\n".join(lines)
        offered, reported, granted = audit["offered"], audit["reported"], audit["granted"]
        lines.append("  offered:  " + (", ".join(
            [f"#{i['item_no']} {named(i['item_id'])}" for i in offered["items"]]
            + [f"#{c['item_no']} {named(c['item_id'])} x{c['count']}" for c in offered["counted"]]
            + [f"{named(r['item_id'])} x{r['count']} (fellow, on clear)" for r in offered["rolled"] if r["kind"] == "fellow"]) or "nothing"))
        lines.append(f"  reported: reward_items={json.dumps(reported['reward_items'])} event_items={json.dumps(reported['event_items'])}")
        lines.append("  granted:  " + (", ".join(
            [f"#{i['item_no']} {named(i['item_id'])}" for i in granted["items"]]
            + [f"#{c['item_no']} {named(c['item_id'])} x{c['count']}" for c in granted["counted"]]
            + [f"{named(f)} (fellow)" for f in granted["fellows"]]) or "nothing")
            + ("" if granted["cleared"] else "  (not a clear)"))
        missed = audit["not_picked_up"]
        if missed["items"] or missed["counted"]:
            lines.append("  left:     " + ", ".join(
                [f"#{i['item_no']} {named(i['item_id'])}" for i in missed["items"]]
                + [f"#{c['item_no']} {named(c['item_id'])} x{c['count']}" for c in missed["counted"]]))
        if audit["not_offered"]:
            lines.append("  UNKNOWN:  " + ", ".join(f"#{n}" for n in audit["not_offered"]) + "  (reported, never offered)")
        if audit["fellows_not_granted"]:
            lines.append("  NO GRANT: " + ", ".join(named(f) for f in audit["fellows_not_granted"]) + "  (rolled fellows)")
        return "\n".join(lines)
    return None


def matches(entry: dict, args) -> bool:
    if args.seq is not None and entry.get("seq") != args.seq:
        return False
    if args.protocols and entry.get("protocol") not in args.protocols:
        return False
    if args.drops and entry.get("protocol") not in (71, 72):
        return False
    if args.user is not None and str(entry.get("user_no")) != args.user:
        return False
    if args.surface and entry.get("surface") != args.surface:
        return False
    if args.warnings and not (entry.get("warnings") or entry.get("failure")):
        return False
    if args.grep and args.grep not in json.dumps(entry, ensure_ascii=False):
        return False
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("file", nargs="?", type=Path, help="a traffic .jsonl (default: today's in --dir)")
    parser.add_argument("--dir", type=Path, default=ROOT / "work" / "traffic", help="the traffic folder (KNS_LOG_DIR/traffic)")
    parser.add_argument("--day", help="yyyy-MM-dd (default: today)")
    parser.add_argument("-p", "--protocol", dest="protocols", type=lambda text: {int(p) for p in text.split(",")},
                        help="game gateway protocols, e.g. 71,72")
    parser.add_argument("-u", "--user", help="user_no")
    parser.add_argument("--surface", help="gateway, hive, hub, activeuser, mercury, billing, ...")
    parser.add_argument("--warnings", action="store_true", help="only exchanges with warnings or failures")
    parser.add_argument("--grep", help="only exchanges whose JSON contains this text")
    parser.add_argument("--seq", type=int, help="one exchange, in full")
    parser.add_argument("--drops", action="store_true", help="stage starts and results as drop ledgers")
    parser.add_argument("-v", "--verbose", action="store_true", help="print request, notes and reply")
    parser.add_argument("-f", "--follow", action="store_true", help="keep reading as the server writes")
    args = parser.parse_args()

    path = args.file or args.dir / f"{args.day or dt.date.today().isoformat()}.jsonl"
    if not path.exists():
        print(f"No traffic log at {path}", file=sys.stderr)
        return 1

    try:
        for entry in entries(path, args.follow):
            if not matches(entry, args):
                continue
            if args.drops:
                text = drops_view(entry)
            elif args.verbose or args.seq is not None:
                text = verbose(entry)
            else:
                text = summary(entry)
            if text:
                print(text, flush=True)
                if args.drops or args.verbose or args.seq is not None:
                    print()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
