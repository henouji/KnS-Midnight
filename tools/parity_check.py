#!/usr/bin/env python3
"""Compare the Python server (server/) with the .NET port (server-dotnet/), request by request.

Starts both servers on fresh scratch databases with the same deterministic random seed
(KNS_RNG_SEED: both draw the same splitmix64 numbers), plays one scripted session against both
(platform surfaces, login, stages with drops, items, fellows, heroes, a second player) and diffs
every reply: HTTP status, content type, and the decrypted JSON including key order. Values that
depend on the clock (server_time, the time-derived Hub / Hive ids, ...) are masked.

Each request goes to both servers; the next request is built from the Python server's reply,
so a divergence shows up at the first request that differs.

Usage (from the repository root; needs pycryptodome and the .NET 8 SDK):
    python tools/parity_check.py                # seeded drops
    python tools/parity_check.py --drop-all     # KNS_DROP_ALL: every slot hits, max options
    python tools/parity_check.py --keep         # keep the scratch folder (databases, server logs)
    python tools/parity_check.py --from-db work/kns.sqlite   # start both from copies of a real save
Exit code 0 when every reply matches.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import http.client
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "server"))
import activeuser  # noqa: E402  (request encryption for the ActiveUser surface)
import gamegw  # noqa: E402  (request encryption for the game gateway)

DOTNET_DLL = ROOT / "server-dotnet" / "src" / "KnS.Server" / "bin" / "Release" / "net8.0" / "KnS.Server.dll"
HOST = "kns.parity.test"     # sent to both, so protocol 48's download URL matches
CLOCK_KEYS = {"server_time", "server_date_time", "user_next_shop_update_time", "uptime_s"}
# Ids the platform surfaces derive from the clock.
PLATFORM_ID_KEYS = {"uid", "hub_uid", "user_id", "login_key", "session_key", "secure_key",
                    "guest_uid", "did", "did_data"}
ADDITION_VERSION = "1.4.0"


# --------------------------------------------------------------------------- servers
class Server:
    def __init__(self, name: str, command: list, port: int, env: dict, log_path: Path):
        self.name, self.port = name, port
        self._log = log_path.open("w", encoding="utf-8")
        self._process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=self._log,
                                         stderr=subprocess.STDOUT)

    def request(self, method: str, path: str, body: bytes = b"", headers: dict | None = None):
        """(status, lower-cased headers, body), or (None, {}, error text) when the call failed."""
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=60)
        try:
            conn.request(method, path, body=body or None, headers={"Host": HOST, **(headers or {})})
            response = conn.getresponse()
            return response.status, {k.lower(): v for k, v in response.getheaders()}, response.read()
        except (OSError, http.client.HTTPException) as e:
            return None, {}, repr(e).encode()
        finally:
            conn.close()

    def wait_healthy(self, timeout: float = 180) -> None:
        deadline = time.time() + timeout
        while time.time() < deadline:
            if self._process.poll() is not None:
                raise SystemExit(f"{self.name} server exited early (see its log)")
            status, _, _ = self.request("GET", "/health")
            if status == 200:
                return
            time.sleep(0.5)
        raise SystemExit(f"{self.name} server did not become healthy in {timeout:.0f}s")

    def stop(self) -> None:
        self._process.terminate()
        try:
            self._process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self._process.kill()
        self._log.close()


# --------------------------------------------------------------------------- diffing
def diff(reference, candidate, path: str = "$", masked: frozenset = frozenset()) -> list:
    """Differences between two JSON values, including the order of object keys."""
    if isinstance(reference, dict) and isinstance(candidate, dict):
        out = []
        if list(reference) != list(candidate):
            out.append(f"{path}: keys {list(reference)} != {list(candidate)}")
        for key in reference:
            if key in candidate and key not in masked:
                out += diff(reference[key], candidate[key], f"{path}.{key}", masked)
        return out
    if isinstance(reference, list) and isinstance(candidate, list):
        if len(reference) != len(candidate):
            return [f"{path}: {len(reference)} elements != {len(candidate)}: {_short(reference)} vs {_short(candidate)}"]
        out = []
        for i, (a, b) in enumerate(zip(reference, candidate)):
            out += diff(a, b, f"{path}[{i}]", masked)
        return out
    if type(reference) is not type(candidate) or reference != candidate:
        return [f"{path}: {_short(reference)} != {_short(candidate)}"]
    return []


def _short(value) -> str:
    text = json.dumps(value)
    return text if len(text) <= 160 else text[:157] + "..."


class Parity:
    """Sends each request to both servers and records whether the replies match."""

    def __init__(self, python: Server, dotnet: Server):
        self.python, self.dotnet = python, dotnet
        self.results: list = []

    def _record(self, name: str, problems: list) -> None:
        self.results.append((name, problems))
        print(("OK   " if not problems else "DIFF ") + name)
        for p in problems[:12]:
            print("       " + p)
        if len(problems) > 12:
            print("       ... %d more" % (len(problems) - 12))

    def _both(self, method, path, body=b"", headers=None):
        return (self.python.request(method, path, body, headers),
                self.dotnet.request(method, path, body, headers))

    @staticmethod
    def _envelope_problems(ref, cand, check_type: bool = True) -> list:
        problems = []
        if ref[0] != cand[0]:
            problems.append(f"status {ref[0]} != {cand[0]}")
        if check_type and ref[0] == 200 and ref[1].get("content-type") != cand[1].get("content-type"):
            problems.append(f"content-type {ref[1].get('content-type')!r} != {cand[1].get('content-type')!r}")
        return problems

    def json(self, name: str, method: str, path: str, payload=None, raw: bytes | None = None,
             masked=frozenset(CLOCK_KEYS)) -> dict | None:
        body = raw if raw is not None else (json.dumps(payload).encode() if payload is not None else b"")
        ref, cand = self._both(method, path, body)
        problems = self._envelope_problems(ref, cand)
        if ref[0] is not None and cand[0] is not None and not problems:
            problems += diff(json.loads(ref[2]), json.loads(cand[2]), masked=frozenset(masked))
        self._record(name, problems)
        return json.loads(ref[2]) if ref[0] == 200 else None

    def gateway(self, name: str, protocol, params: dict, user_no: int | None = None,
                raw: bytes | None = None) -> dict:
        body = raw if raw is not None else gamegw.encrypt({"params": params, "protocol": protocol})
        headers = {"Content-Type": "application/x-www-form-urlencoded"}
        if user_no:
            headers["USERNO"] = str(user_no)
        ref, cand = self._both("POST", "/1mhero/gateway.php", body, headers)
        problems = self._envelope_problems(ref, cand)
        reference = {}
        if ref[0] == 200:
            reference = gamegw.decrypt(ref[2])
        if not problems and ref[0] == 200:
            problems += diff(reference, gamegw.decrypt(cand[2]), masked=frozenset(CLOCK_KEYS))
        self._record(f"gw {protocol:<4} {name}" if isinstance(protocol, int) else f"gw {name}", problems)
        return reference

    def active_user(self, name: str, api_request: dict) -> None:
        timestamp = str(int(time.time()))
        body = activeuser.encrypt_response(timestamp, api_request)
        ref, cand = self._both("POST", "/v1/gateway.php", body, {"REQ-TIMESTAMP": timestamp})
        problems = self._envelope_problems(ref, cand)
        if not problems and ref[0] == 200:
            replies = []
            for label, (status, headers, raw) in (("python", ref), ("dotnet", cand)):
                plaintext = activeuser._pkcs7_unpad(
                    activeuser.AES.new(activeuser._key(timestamp), activeuser.AES.MODE_CBC,
                                       activeuser.IV).decrypt(__import__("base64").b64decode(raw)))
                if headers.get("req-timestamp") != timestamp:
                    problems.append(f"{label}: REQ-TIMESTAMP {headers.get('req-timestamp')!r}")
                if headers.get("req-authkey") != hashlib.md5(plaintext).hexdigest():
                    problems.append(f"{label}: REQ-AUTHKEY is not md5 of the reply")
                replies.append(json.loads(plaintext))
            problems += diff(replies[0], replies[1])
        self._record("activeuser " + name, problems)

    def binary(self, name: str, path: str, headers: dict | None = None) -> None:
        ref, cand = self._both("GET", path, b"", headers)
        problems = self._envelope_problems(ref, cand, check_type=False)
        if not problems and ref[0] in (200, 206):
            if ref[2] != cand[2]:
                problems.append(f"body differs ({len(ref[2])} vs {len(cand[2])} bytes)")
            for header in ("content-range", "accept-ranges", "content-length"):
                if ref[1].get(header) != cand[1].get(header):
                    problems.append(f"{header} {ref[1].get(header)!r} != {cand[1].get(header)!r}")
        self._record(name, problems)

    def summary(self) -> int:
        failed = [name for name, problems in self.results if problems]
        print("\n%d requests, %d matched, %d differed" % (len(self.results), len(self.results) - len(failed), len(failed)))
        for name in failed:
            print("  differs: " + name)
        return 1 if failed else 0


# --------------------------------------------------------------------------- scenario
def master_rows(file_name: str) -> list:
    with (ROOT / "server" / "seed" / file_name).open(encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def play_stage(p: Parity, user_no: int, stage_id: int, mode: int = 1, result: int = 1, label: str = "") -> dict:
    """A stage start and its result, reporting every offered drop as picked up."""
    start = p.gateway(f"stage start {stage_id} mode {mode} {label}", 71,
                      {"friend_type": 0, "stage_id": stage_id, "stage_mode": mode, "friend_user_no": 0}, user_no)
    drops = start.get("drop_item_list", {})
    reward_items = [d["item_no"] for d in drops.get("1", []) + drops.get("4", [])]
    event_items = []
    for offer in start.get("drop_event_item_list", []):
        reward_items.append(0)               # the client reports a 0 per counted etc pickup
        event_items.append(offer["item_no"])
    return p.gateway(f"stage result {stage_id} mode {mode} {label}", 72,
                     {"stage_no": stage_id, "stage_mode": mode, "stage_result": result, "reward_gold": 321,
                      "reward_items": reward_items, "event_items": event_items, "max_damage": 77}, user_no)


def play_platform(p: Parity) -> None:
    ids = frozenset(CLOCK_KEYS | PLATFORM_ID_KEYS)
    p.json("health", "GET", "/health")
    p.json("root (fallback)", "GET", "/")
    p.json("unknown path (fallback)", "POST", "/nothing/here", {"a": 1})
    for api in ("get_did", "get_config", "download", "agreement", "get_agreement", "other"):
        p.active_user(api, {"api": api, "android_id": "parity-android"})
    p.active_user("no device id", {"api": "get_did"})
    p.json("activeuser plain with did", "POST", "/activeuser/check", {"did": "parity-did"})
    p.json("activeuser plain without did", "POST", "/activeuser/check", {}, masked=ids)
    p.json("mercury", "POST", "/mercury/init", {})
    p.json("messenger", "GET", "/messenger/list")
    p.json("offerwall", "GET", "/wall/list")
    p.json("billing check2", "POST", "/billing/check2.c2s", {"x": 1})
    # Hive ids come from the clock (tenths of a second) and are the account key: space the calls.
    for path in ("/guest/create", "/guest/auth", "/device/registered", "/guest/acquire_uid",
                 "/guest/bind", "/v2/auth", "/guest/other"):
        p.json("hive " + path, "POST", path, {"did": "parity-hive"}, masked=ids)
        time.sleep(0.15)
    p.json("hub new guest", "POST", "/hub/login", {"guest_uid": "parity-guest", "did": "d1"}, masked=ids)
    time.sleep(0.01)
    p.json("hub known guest", "POST", "/hub/login", {"guest_uid": "parity-guest"}, masked=ids)
    time.sleep(0.01)
    p.json("hub form body", "POST", "/member/x.c2s", raw=b"guest_uid=form-guest&did=form%20did&empty=", masked=ids)
    time.sleep(0.01)
    p.json("hub no guest", "POST", "/api/policy", {}, masked=ids)

    files = sorted((ROOT / "original" / gamegw.PKG / "files" / ADDITION_VERSION).iterdir())
    if files:
        name = files[0].name
        p.binary("addition full", f"/addition/{ADDITION_VERSION}/{name}")
        p.binary("addition resume", f"/addition/{ADDITION_VERSION}/{name}", {"Range": "bytes=100-"})
        p.binary("addition range", f"/addition/{ADDITION_VERSION}/{name}", {"Range": "bytes=0-99"})
        p.binary("addition trailer only", f"/addition/{ADDITION_VERSION}/{name}",
                 {"Range": "bytes=%d-" % files[0].stat().st_size})
    p.binary("addition missing", f"/addition/{ADDITION_VERSION}/no-such-file.ogg")
    p.binary("addition traversal", "/addition/../app.py")
    p.binary("addition too deep", f"/addition/{ADDITION_VERSION}/a/b")


def play_game(p: Parity) -> None:
    p.gateway("not encrypted", "decrypt failure", {}, raw=b"this is not base64 ciphertext!")
    p.gateway("version check", 48, {"strBundleIdentifier": gamegw.PKG, "strClientVersion": ADDITION_VERSION})
    login = p.gateway("login new device", 51, {"hub_did": "parity-device-1", "hub_uid": "-1"})
    user = login.get("user_no", 0)
    p.gateway("login same device", 51, {"hub_did": "parity-device-1", "hub_uid": "-1"})
    p.gateway("hub uid", 52, {"hub_did": "parity-device-1"})
    p.gateway("hub device", 53, {"hub_did": "parity-device-1"})
    p.gateway("event info", 59, {}, user)
    p.gateway("load user", 60, {"user_no": user}, user)
    p.gateway("inventory", 61, {"user_no": user}, user)
    p.gateway("stage info (none cleared)", 70, {"user_no": user}, user)
    p.gateway("unhandled protocol", 140, {"user_no": user}, user)

    # Area 3000000 needs 15 normal clears for its reward.
    area_stages = [int(r["stage_id"]) for r in master_rows("master_stage_areas.csv") if r["area_id"] == "3000000"]
    for round_number in range(3):
        for stage in sorted(area_stages):
            play_stage(p, user, stage, label=f"round {round_number + 1}")
    play_stage(p, user, 3000011, label="(boss)")
    play_stage(p, user, 3000001, mode=2, label="(hard)")
    play_stage(p, user, 3000002, result=0, label="(failed)")
    p.gateway("continue", 73, {"stage_no": 3000003, "rand_key": 1}, user)
    p.gateway("stage info", 70, {"user_no": user}, user)
    p.gateway("area reward", 74, {"area_id": 3000000, "stage_mode": 1}, user)
    p.gateway("area reward again", 74, {"area_id": 3000000, "stage_mode": 1}, user)
    p.gateway("area reward hard (not reached)", 74, {"area_id": 3000000, "stage_mode": 2}, user)

    play_items(p, user)
    play_fellows(p, user)
    play_heroes(p, user)

    second = p.gateway("login second player", 51, {"hub_did": "parity-device-2", "hub_uid": "-1"}).get("user_no", 0)
    p.gateway("second player load", 60, {"user_no": second}, second)
    play_stage(p, second, 3000001, label="(second player)")
    p.gateway("second player inventory", 61, {"user_no": second}, second)
    p.gateway("first player inventory", 61, {"user_no": user}, user)
    p.gateway("first player load", 60, {"user_no": user}, user)


def play_items(p: Parity, user: int) -> None:
    inventory = p.gateway("inventory after stages", 61, {"user_no": user}, user)
    owned = [i for i in inventory.get("item_infos", []) if i["item_no"] >= 2000]
    bag = [i for i in owned if not i.get("hero_no")]
    if bag:
        p.gateway("equip", 80, {"user_no": user, "hero_no": 1, "item_no": bag[0]["item_no"]}, user)
    p.gateway("equip unknown item", 80, {"hero_no": 1, "item_no": 999999}, user)

    inventory = p.gateway("inventory after equip", 61, {"user_no": user}, user)
    bag = [i for i in inventory.get("item_infos", []) if i["item_no"] >= 2000 and not i.get("hero_no")]
    by_id: dict = {}
    for item in bag:
        by_id.setdefault(item["item_id"], []).append(item["item_no"])
    pairs = [nos for nos in by_id.values() if len(nos) >= 2]
    if pairs:
        p.gateway("power up", 81, {"item_no": pairs[0][0], "used_item_no": pairs[0][1]}, user)
    p.gateway("power up with itself", 81, {"item_no": bag[0]["item_no"] if bag else 0,
                                          "used_item_no": bag[0]["item_no"] if bag else 0}, user)
    p.gateway("power up with starter material", 81, {"item_no": bag[0]["item_no"] if bag else 0,
                                                    "used_item_no": 1001}, user)
    if bag:
        p.gateway("enhance without materials", 82, {"item_no": bag[-1]["item_no"], "used_item_no_list": []}, user)
    p.gateway("enhance unknown", 82, {"item_no": 999999, "used_item_no_list": ["x", 5]}, user)

    play_enhance(p, user)

    inventory = p.gateway("inventory before sale", 61, {"user_no": user}, user)
    bag = [i["item_no"] for i in inventory.get("item_infos", []) if i["item_no"] >= 2000 and not i.get("hero_no")]
    p.gateway("sell", 83, {"sell_items": bag[:3] + [1001, 999999, "bad", 1003]}, user)
    p.gateway("sell starter again", 83, {"sell_items": [1003]}, user)

    p.gateway("expand item bag 1", 106, {"expansion_level": 1}, user)
    p.gateway("expand item bag 1 again", 106, {"expansion_level": 1}, user)
    p.gateway("expand item bag 2", 106, {"expansion_level": 2}, user)
    p.gateway("expand ally bag 1", 301, {"expansion_level": 1}, user)
    p.gateway("expand ally bag unknown", 301, {"expansion_level": 9999}, user)


def play_enhance(p: Parity, user: int) -> None:
    """Power an item up to its cap with copies of itself, then enhance it with its materials."""
    rules = {int(r["item_id"]): r for r in master_rows("master_items.csv")}
    inventory = p.gateway("inventory before enhance", 61, {"user_no": user}, user)
    bag = [i for i in inventory.get("item_infos", []) if i["item_no"] >= 2000 and not i.get("hero_no")]
    for item in bag:
        rule = rules.get(item["item_id"])
        if not rule or int(rule["next_item_id"]) <= 0 or item["item_upgrade"] != 0:
            continue
        cap = int(rule["c4"])
        copies = [i["item_no"] for i in bag if i["item_id"] == item["item_id"] and i["item_no"] != item["item_no"]][:cap]
        spare = [i for i in bag if i["item_no"] != item["item_no"] and i["item_no"] not in copies]
        materials = []
        for material_id in (int(rule["material%d" % n]) for n in range(1, 7)):
            match = next((i for i in spare if i["item_id"] == material_id and i["item_no"] not in materials), None)
            if material_id > 0:
                materials.append(match["item_no"] if match else None)
        if len(copies) < cap or None in materials:
            continue
        for copy in copies:
            p.gateway(f"power up {item['item_id']} toward its cap", 81,
                      {"item_no": item["item_no"], "used_item_no": copy}, user)
        p.gateway(f"enhance {item['item_id']} -> {rule['next_item_id']}", 82,
                  {"item_no": item["item_no"], "used_item_no_list": materials}, user)
        return
    print("     (no item could be enhanced with this seed)")


def play_fellows(p: Parity, user: int) -> None:
    p.gateway("draw 11", 92, {"package_id": 9710005, "use_ticket": 0, "group_num": 1}, user)
    p.gateway("draw 1", 92, {"package_id": 9710006}, user)
    p.gateway("draw friendship package (refused)", 92, {"package_id": 9710007}, user)
    loaded = p.gateway("load user with fellows", 60, {"user_no": user}, user)
    fellows = [f["fellow_no"] for f in loaded.get("fellow_info", [])]
    team = [{"fellow_no": no, "fellow_position": pos} for pos, no in enumerate(fellows[:3])]
    p.gateway("fellow set", 302, {"type": 3, "fellows": team + [{"fellow_no": 123, "fellow_position": 1}]}, user)
    p.gateway("fellow set other setup", 302, {"type": 1, "fellows": [{"fellow_no": fellows[0] if fellows else 0,
                                                                       "fellow_position": 2}]}, user)
    for stage in (3000001, 3000002):
        play_stage(p, user, stage, label="(with fellows)")
    p.gateway("fellow sell", 306, {"sell_fellows": fellows[:1] + fellows[-2:] + [1]}, user)
    p.gateway("load user after fellow sale", 60, {"user_no": user}, user)


def play_heroes(p: Parity, user: int) -> None:
    p.gateway("buy hero 2", 201, {"user_no": user, "hero_pcid": 2}, user)
    p.gateway("buy hero 2 again", 201, {"user_no": user, "hero_pcid": 2}, user)
    p.gateway("buy unknown hero", 201, {"hero_pcid": 99}, user)
    p.gateway("change to hero 1", 202, {"hero_no": 1}, user)
    p.gateway("change to unknown hero", 202, {"hero_no": 77}, user)
    p.gateway("buy hero 3 (demon hunter)", 201, {"hero_pcid": 3}, user)
    play_stage(p, user, 3000004, label="(demon hunter)")
    play_stage(p, user, 3000011, label="(demon hunter boss)")
    p.gateway("load user with heroes", 60, {"user_no": user}, user)


# --------------------------------------------------------------------------- main
def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--seed", type=int, default=20261025, help="KNS_RNG_SEED for both servers")
    parser.add_argument("--drop-all", action="store_true", help="KNS_DROP_ALL for both servers")
    parser.add_argument("--keep", action="store_true", help="keep the scratch folder")
    parser.add_argument("--no-build", action="store_true", help="use the existing Release build")
    parser.add_argument("--from-db", type=Path, help="start both servers from copies of this SQLite file")
    args = parser.parse_args()

    if not args.no_build:
        subprocess.run(["dotnet", "build", str(ROOT / "server-dotnet"), "-c", "Release", "-v", "q", "-nologo"],
                       check=True)
    scratch = Path(tempfile.mkdtemp(prefix="kns-parity-"))
    base_env = {**os.environ, "KNS_QUIET": "1", "KNS_RNG_SEED": str(args.seed)}
    for key in ("KNS_DATABASE_URL", "KNS_DROP_ALL", "KNS_SERVE_ADDITION", "KNS_EMPTY_HERO", "KNS_MUTE",
                "KNS_BREAK_AU", "KNS_GW_DELAY", "KNS_BILLING_DELAY"):
        base_env.pop(key, None)
    if args.drop_all:
        base_env["KNS_DROP_ALL"] = "1"
    if args.from_db:
        for name in ("python", "dotnet"):
            shutil.copyfile(args.from_db, scratch / f"{name}.sqlite")

    python = Server("python", [sys.executable, "-u", "server/app.py", "18091"], 18091,
                    {**base_env, "KNS_DB": str(scratch / "python.sqlite")}, scratch / "python.log")
    dotnet = Server("dotnet", ["dotnet", str(DOTNET_DLL), "18092"], 18092,
                    {**base_env, "KNS_DB": str(scratch / "dotnet.sqlite")}, scratch / "dotnet.log")
    try:
        python.wait_healthy()
        dotnet.wait_healthy()
        parity = Parity(python, dotnet)
        play_platform(parity)
        play_game(parity)
        return parity.summary()
    finally:
        python.stop()
        dotnet.stop()
        if args.keep:
            print("scratch folder kept: " + str(scratch))
        else:
            shutil.rmtree(scratch, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
