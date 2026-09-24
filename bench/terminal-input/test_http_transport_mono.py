"""Exercise production HTTP scheduling on Mono, without a game or real daemon."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import threading
import time


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def handle(self):
        try:
            super().handle()
        except ConnectionResetError:
            pass  # Client deliberately aborts oversized and timed-out responses.

    def log_message(self, *args):
        pass

    def do_POST(self):
        size = int(self.headers['Content-Length'])
        body = self.rfile.read(size)
        self.respond(str(len(body)).encode())

    def do_GET(self):
        if self.path == '/ok':
            time.sleep(.02)
        body = b'test error' if self.path == '/error' else b'response'
        self.respond(body)

    def respond(self, body):
        self.send_response(503 if self.path in ('/error', '/slow-error') else 200)
        self.send_header('Content-Type', 'text/plain' if self.path == '/wrong-type' else 'application/x-protobuf')
        big = self.path == '/oversize'
        self.send_header('Content-Length', str(33 * 1024 * 1024 if big else len(body)))
        self.end_headers()
        try:
            if self.path in ('/slow-body', '/slow-error'):
                time.sleep(2)
            if big:
                for _ in range(33):
                    self.wfile.write(b'x' * (1024 * 1024))
            else:
                self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass


def main():
    for executable in ('mono', 'mcs'):
        if not shutil.which(executable):
            raise SystemExit(f'Install {executable} to run the Mono HTTP regression test')
    root = Path(__file__).resolve().parents[2]
    source = os.environ.get('HTTP_PROBE_SOURCE', str(root / 'mod/Source/SlopWorld/Client/Daemon/DaemonClient.cs'))
    with tempfile.TemporaryDirectory() as directory, ThreadingHTTPServer(('127.0.0.1', 0), Handler) as server:
        server.daemon_threads = True
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            exe = str(Path(directory) / 'probe.exe')
            subprocess.run(['mcs', '-langversion:latest', '-out:' + exe, source,
                            str(root / 'bench/terminal-input/tests/http_transport/Probe.cs')], check=True)
            subprocess.run(['mono', exe, f'http://127.0.0.1:{server.server_port}'], timeout=15, check=True)
        finally:
            server.shutdown()
            thread.join()


if __name__ == '__main__':
    main()
