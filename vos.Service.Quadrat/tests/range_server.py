"""A small web server for tests, run as its own process so a read that holds the GIL cannot starve it.

Serves the files under a folder with byte ranges, as a COG host does. `/__log` answers the requests made so far,
`/__redirect/<path>` redirects to `/<path>`, and `/__slow/<path>` answers `/<path>` after a pause.
"""
import http.server
import json
import os
import re
import sys
import threading
import time

root = os.path.abspath(sys.argv[1])
requests = []
lock = threading.Lock()


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_HEAD(self):
        self._answer(False)

    def do_GET(self):
        self._answer(True)

    def _answer(self, with_body):
        path = self.path.split("?")[0]
        if path == "/__log":
            return self._send(200, json.dumps(requests).encode(), "application/json", with_body)
        if path.startswith("/__redirect/"):
            self.send_response(302)
            self.send_header("Location", path[len("/__redirect"):])
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        if path.startswith("/__slow/"):
            time.sleep(3)
            path = path[len("/__slow"):]
        target = os.path.join(root, path.lstrip("/"))
        if not os.path.isfile(target):
            return self._send(404, b"", "text/plain", with_body)
        with open(target, "rb") as file:
            data = file.read()
        wanted = self.headers.get("Range")
        start, end = 0, len(data) - 1
        if wanted:
            parsed = re.match(r"bytes=(\d+)-(\d*)", wanted)
            start = int(parsed[1])
            end = min(int(parsed[2]) if parsed[2] else end, len(data) - 1)
        with lock:
            requests.append({"method": self.command, "path": path, "range": wanted, "bytes": end - start + 1 if with_body else 0})
        self.send_response(206 if wanted else 200)
        if wanted:
            self.send_header("Content-Range", f"bytes {start}-{end}/{len(data)}")
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Type", "image/tiff")
        self.send_header("Content-Length", str(end - start + 1))
        self.end_headers()
        if with_body:
            self.wfile.write(data[start:end + 1])

    def _send(self, status, body, kind, with_body):
        self.send_response(status)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if with_body:
            self.wfile.write(body)


server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
print(server.server_port, flush=True)
server.serve_forever()
