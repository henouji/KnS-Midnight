#!/usr/bin/env python3
"""Phase-0 capture server: log every request verbatim, return a benign 200.

Point the (patched or proxied) game at this host. It never tries to answer
correctly -- its only job is to record exactly what the client sends (method, path,
headers, raw body) so we can pair it with the Frida crypto captures. Bodies are
AES-encrypted at this stage, so they are logged as hex.

    python tools/capture/log_server.py [port]      # default 8080

Logs to stdout and appends JSONL to work/http-captures.jsonl next to CWD.
"""
import json
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

LOG = Path("work") / "http-captures.jsonl"


class Handler(BaseHTTPRequestHandler):
    def _record(self, method):
        length = int(self.headers.get("Content-Length", 0) or 0)
        body = self.rfile.read(length) if length else b""
        rec = {
            "ts": time.time(),
            "method": method,
            "path": self.path,
            "headers": dict(self.headers.items()),
            "body_len": len(body),
            "body_hex": body.hex(),
        }
        # Best-effort: show body as text if it happens to be printable.
        try:
            txt = body.decode("utf-8")
            if txt.isprintable() or "\n" in txt:
                rec["body_text"] = txt
        except UnicodeDecodeError:
            pass
        print(f"[{method}] {self.path}  ({len(body)} bytes)")
        if "body_text" in rec:
            print("  body:", rec["body_text"][:400])
        LOG.parent.mkdir(exist_ok=True)
        with LOG.open("a", encoding="utf-8") as f:
            f.write(json.dumps(rec) + "\n")

    def _reply(self):
        # Benign 200 with an empty-ish envelope. Not expected to satisfy the client
        # yet -- Phase 1 replaces this with real encrypted responses.
        payload = json.dumps({"errno": 0, "error": "", "Result": {}}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        self._record("GET")
        self._reply()

    def do_POST(self):
        self._record("POST")
        self._reply()

    def log_message(self, *a):  # silence default noisy logging
        pass


def main() -> int:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    srv = ThreadingHTTPServer(("0.0.0.0", port), Handler)
    print(f"[capture] listening on 0.0.0.0:{port}  -> {LOG}")
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\n[capture] stopping")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
