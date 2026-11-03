#!/usr/bin/env python3
"""Export the price list of dt_etc_setting.json into the server's seed.

Offline: dt_etc_setting.json.jpg ships in the decoded APK as plain JSON (not encrypted).

  server/seed/master_store_prices.csv     its "Store" array: one row per PriceName
                                          (Continue, StaminaRecovery, PvpRecovery, ...).
                                          price_item_id is the currency item (1900009 = gems)
                                          or 0 when the row is a plain number (seconds, slots).
  server/seed/master_item_option_counts.csv  its "Get_Item_Option_Number" array: for each
                                          StageMode ("Nomal" [sic], "Hard", "ItemShop",
                                          "Event_Armor") the percent chance that a new item
                                          gets 0..6 options; one row per (mode, count).

Usage:
    python tools/dump_etc_setting.py [game_res_dir] [seed_dir]
"""
import csv
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def main() -> int:
    res = Path(sys.argv[1] if len(sys.argv) > 1 else
               ROOT / "decoded/kns-apktool-probe/assets/common/game_res")
    out = Path(sys.argv[2] if len(sys.argv) > 2 else ROOT / "server/seed")

    setting = json.loads((res / "dt_etc_setting.json.jpg").read_text(encoding="utf-8"))
    with (out / "master_store_prices.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["price_name", "price_type", "price_item_id", "price"])
        for row in setting["Store"]:
            w.writerow([row["PriceName"], row["PriceType"], int(row["PriceID"] or 0),
                        int(row["Price"])])
    print("master_store_prices.csv: %d rows" % len(setting["Store"]))

    rows = [[row["StageMode"], n, int(row["OptionNum%d" % n])]
            for row in setting["Get_Item_Option_Number"] for n in range(7)]
    with (out / "master_item_option_counts.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["stage_mode_name", "option_count", "percent"])
        w.writerows(rows)
    print("master_item_option_counts.csv: %d rows" % len(rows))
    return 0


if __name__ == "__main__":
    sys.exit(main())
