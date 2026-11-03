# KnS server (.NET 8)

The C# port of the Python mock backend in `server/`: the native game gateway
(`/1mhero/gateway.php`), the Com2uS platform surfaces (ActiveUser, Hub, Hive, Mercury, offerwall,
billing) and the addition-file CDN. It answers every request exactly as the Python server does
(checked by `tools/parity_check.py`) and runs on the same database, so either server can serve
the same saves.

## Run

From the repository root (`work/` and `original/` are relative to it, as for the Python server):

```sh
dotnet run --project server-dotnet/src/KnS.Server -- 8080
dotnet test server-dotnet
```

The settings are the Python server's environment variables:

| Variable | Effect |
|---|---|
| `PORT` / `WEBSITES_PORT` | Port when no argument is given (default 8080). |
| `KNS_DB` | SQLite file (default `work/kns.sqlite`). |
| `KNS_DATABASE_URL` | `mysql://user:password@host:3306/db`: use MySQL instead (TLS, CA verified). |
| `KNS_DB_POOL` | Most pooled MySQL connections (default 16). |
| `KNS_SEED_DIR` | Master-data CSVs (default: `seed/` next to the binaries, copied from `server/seed`). |
| `KNS_QUIET` | No `work/server.log` / `work/requests.jsonl` / traffic log (hosted servers). |
| `KNS_LOG_DIR` | Folder of those logs (default `work`). |
| `KNS_TRAFFIC_LOG` | Write the traffic log even when quiet (bounded by the retention). |
| `KNS_TRAFFIC_DAYS` | Days of traffic files kept, today included (default 7). |
| `KNS_BREAK_AU` | Plain ActiveUser reply for the encrypted `gateway.php` (set on Azure). |
| `KNS_GW_DELAY`, `KNS_BILLING_DELAY` | Seconds of extra latency on gateway / check2 replies. |
| `KNS_SERVE_ADDITION` | List the archived addition files in protocol 48. |
| `KNS_EMPTY_HERO`, `KNS_MUTE`, `KNS_DROP_ALL` | Test switches (no hero, no audio, every drop hits). |
| `KNS_TESTERS_ONLY` | Only testers in the `tester` table (email, attached device, revoke flag) may use the game gateway; others get an error popup and appear in the refused list. Unset = open. |
| `KNS_ADMIN_KEY` | Secret for `/admin/testers` (header `X-Admin-Key`). Unset = no such endpoint. |
| `KNS_RNG_SEED` | Deterministic random draws, identical to the Python server's (parity runs only). |

## Traffic log

Every request and its reply go to `work/traffic/<yyyy-MM-dd>.jsonl`, one JSON line each:
`seq`, `time`, `ms`, `surface` (gateway, hive, hub, activeuser, mercury, billing, ...), `path`,
`status`, the request and the reply. Game gateway lines are decrypted and add `user_no` (the
USERNO header), `protocol`, `name`, `errno` and:

- `unread`: parameters the client sent that no code read: fields the server ignores.
- `notes`: handler findings. The stage start (71) notes `rolled`, every reward slot the roll hit,
  fellows included (they are granted by the result, so the 71 reply does not show them). The
  stage result (72) notes `drops`: what was `offered`, what the client `reported`, what was
  `granted`, and `not_offered` / `not_picked_up` / `fellows_not_granted`.
- `warnings`: the findings that look like a lost drop: a reported number nothing was offered
  under, more etc-item pickups (the 0s in `reward_items`) than credited offers, a rolled fellow
  not granted on a clear, a stage result field the server ignores. These also go to the console.

Read it with `tools/traffic.py`:

```sh
python tools/traffic.py                 # today, one line per exchange
python tools/traffic.py --drops         # stage runs: rolled / offered / reported / granted, with names
python tools/traffic.py --warnings      # only exchanges with findings or failures
python tools/traffic.py -p 72 -v        # stage results with the full request, notes and reply
python tools/traffic.py --seq 57        # one exchange in full
python tools/traffic.py -f              # follow while playing
```

On a hosted server (`KNS_QUIET`) set `KNS_TRAFFIC_LOG=1` and point `KNS_LOG_DIR` at persistent
storage (App Service: `/home/LogFiles/kns`), then copy the day's file down and pass its path to
`tools/traffic.py`.

## Layout

| Project | Holds |
|---|---|
| `KnS.Protocol` | Wire level: the two AES ciphers, lenient base64, the client-JSON reading rules (`RequestParameters`, `LenientJson`), form bodies. No database, no game rules. |
| `KnS.Data` | `GameDatabase` / `DataSession` (one connection + transaction per request), the SQLite and MySQL dialects, the schema and master-data seeding, and one repository per table group. |
| `KnS.Game` | The game: `GameGateway` (decrypt → handler → flat envelope → encrypt), one `IProtocolHandler` per protocol under `Protocols/` (each with its reply model), the shared services (`Players/`, `Inventory/`, `Fellows/`, `Stages/`), the rules and constants (`Rules/`), and the random source. |
| `KnS.Platform` | The Com2uS platform surfaces. |
| `KnS.Server` | The ASP.NET Core host: environment settings, `RequestRouter` (path-substring routing, in the Python server's order), `/health`, `/addition/`, the request journal. |
| `KnS.Tests` | xUnit: cipher and RNG vectors taken from the Python server, request-reading rules, the CDN trailer, and in-memory integration tests of the gateway. |

A protocol handler is registered as a keyed service under its protocol number
(`GameServiceCollectionExtensions`). Adding a protocol = a value in `GameProtocol`, a handler class
with its reply record, and one `AddHandler` line.

Table and column names are unchanged from the Python server (they are the save format); the
repositories give the unnamed master columns names (`c4` → `MaxUpgrade`, `c87` → stage exp).

## Checking against the Python server

```sh
python tools/parity_check.py              # seeded drops
python tools/parity_check.py --drop-all   # every reward slot hits, maximum options
python tools/parity_check.py --from-db work/kns.sqlite   # start both from copies of a real save
```

It starts both servers on scratch databases with the same `KNS_RNG_SEED`, plays one session
(platform surfaces, 48 → 306, two players) against both and diffs status, content type and the
decrypted JSON including key order. Clock-derived values are masked.

Known, intended differences:

- **Seeded draws on old databases.** Random pools are read with `ORDER BY` (fellow, option and
  item ids). On a database whose master tables predate typed seeding, the Python server scans
  `master_fellow` in CSV order, so a seeded draw picks a neighbouring fellow. Unseeded play is
  unaffected (the pick is random either way).
- **Transactions.** Each gateway request is one transaction, retried on a MySQL deadlock / lock
  timeout or a busy SQLite file, where the Python server committed block by block.
- **Bad input.** Where the Python server crashed on malformed input (the client never got a
  reply), the port answers 500 or ignores the malformed element.

## Tester allowlist

Set `KNS_TESTERS_ONLY=1` and `KNS_ADMIN_KEY=<secret>`. The game never sends an email, so a tester is an
email row with a device id attached (`hub_did`, stable per install). Manage it with the admin endpoint
(plain http: the key travels in the clear, so rotate it after use on an open network):

```sh
H="X-Admin-Key: <secret>"; U=http://<host>/admin/testers
curl -H "$H" $U                                                    # testers + refused devices (newest first)
curl -H "$H" -d '{"email":"a@b.com","note":"alpha"}' $U            # add
curl -H "$H" -d '{"email":"a@b.com","device_id":"<hub_did>"}' $U/attach   # after their first (refused) try
curl -H "$H" -d '{"email":"a@b.com"}' $U/revoke                    # cut off (and /restore)
```

A refused device is listed with its attempt count (200 kept); attaching it removes it from that list.
Revoking takes effect on the next request: the player keeps their save and gets it back on `/restore`.

## Feature flags

The `feature_flag` table (`feature_key`, `enabled`, `created_date`, `update_date`; unix seconds) holds
server-side switches, read on every request so a change applies at once. A missing row falls back to the
flag default. Flags: `fourth_fellow` (default on): the rebuild fourth dungeon ally slot; off, a request
`party_slots: 4` is ignored and replies keep the original three positions.

```sh
curl -H "X-Admin-Key: <secret>" http://<host>/admin/flags                                     # list
curl -H "X-Admin-Key: <secret>" -d '{"feature_key":"fourth_fellow","enabled":false}' http://<host>/admin/flags
```
