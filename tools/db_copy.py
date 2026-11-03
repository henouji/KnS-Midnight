#!/usr/bin/env python3
"""Copy a SQLite save (work/kns.sqlite, or one fetched with deploy/azure/kudu_file.py) into the
MySQL database named by KNS_DATABASE_URL (read from the environment, else work/db.env).

    python tools/db_copy.py <save.sqlite> [--replace]

The target schema and master tables come from the server's own code (gamegw._db, app.db). Every
player table is copied with its original row ids (rowid_alias), so item_no / fellow_no stay the
same. Refuses to touch a target that already holds players unless --replace (then the player
tables are emptied first). Prints row counts on both sides.
"""
from __future__ import annotations

import os
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_env() -> None:
    if os.environ.get("KNS_DATABASE_URL"):
        return
    env = ROOT / "work" / "db.env"
    if env.exists():
        for line in env.read_text().splitlines():
            if line.startswith("KNS_DATABASE_URL="):
                os.environ["KNS_DATABASE_URL"] = line.split("=", 1)[1].strip()


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 1:
        print(__doc__)
        return 2
    load_env()
    sys.path.insert(0, str(ROOT / "server"))
    import kdb      # noqa: E402  (reads KNS_DATABASE_URL at import)
    if not kdb.IS_MYSQL:
        sys.exit("KNS_DATABASE_URL is not a mysql:// URL")
    import app      # noqa: E402
    import gamegw   # noqa: E402

    src = sqlite3.connect(args[0])
    if not src.execute("SELECT 1 FROM sqlite_master WHERE name='sqlite_sequence'").fetchone():
        src.execute("CREATE TEMP TABLE sqlite_sequence(name TEXT, seq INTEGER)")
    master = set(gamegw.MASTER_TABLES)
    tables = [r[0] for r in src.execute("SELECT name FROM sqlite_master WHERE type='table'"
                                        " AND name NOT LIKE 'sqlite_%' ORDER BY name")
              if r[0] not in master and r[0] != "save"]
    dst = gamegw._db()               # creates the schema + master tables on MySQL
    app.db().close()                 # account table
    have = dst.execute("SELECT count(*) FROM gameuser").fetchone()[0]
    if have and "--replace" not in sys.argv:
        sys.exit("target already has %d players; pass --replace to overwrite" % have)

    for t in tables:
        cols = [r[1] for r in src.execute("PRAGMA table_info(%s)" % t)]
        tcols = [r[1] for r in dst.execute("PRAGMA table_info(%s)" % t)]
        if not tcols:
            print("skip %-20s (no such table on the target)" % t)
            continue
        use = [c for c in cols if c in tcols]
        rows = src.execute("SELECT %s FROM %s" % (", ".join(use), t)).fetchall()
        dst.execute("DELETE FROM %s" % t)
        if rows:
            dst.executemany("INSERT INTO %s (%s) VALUES (%s)" % (t, ", ".join(use), ", ".join("?" * len(use))),
                            rows)
        dst.commit()
        n = dst.execute("SELECT count(*) FROM %s" % t).fetchone()[0]
        print("%-20s %6d -> %6d%s" % (t, len(rows), n, "" if n == len(rows) else "   MISMATCH"))
    # SQLite AUTOINCREMENT never reuses an id, even of a deleted row (a sold item or ally): carry
    # the counters over so new item_no / fellow_no values continue after the highest ever issued.
    for name, seq in src.execute("SELECT name, seq FROM sqlite_sequence"):
        if name in tables:
            dst.execute("ALTER TABLE %s AUTO_INCREMENT = %d" % (name, int(seq) + 1))
    dst.commit()
    dst.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
