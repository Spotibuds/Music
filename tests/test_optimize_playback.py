import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import unittest
from email import policy
from email.parser import BytesParser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "optimize-playback.py"
spec = importlib.util.spec_from_file_location("playback_repair", SCRIPT)
repair = importlib.util.module_from_spec(spec)
spec.loader.exec_module(repair)
SONG = "123456789012345678901234"
AUDIO = b"\xff\xfb\xe0\x64" + bytes(range(100))
ORIGINAL = b"ID3\x03\x00\x00\x00\x00\x00\x04" + b"art!" + AUDIO


class RepairTests(unittest.TestCase):
    def test_audio_bytes_and_malformed_metadata(self):
        self.assertEqual(repair.audio_only(ORIGINAL), AUDIO)
        self.assertEqual(repair.audio_only(AUDIO), AUDIO)
        self.assertEqual(repair.audio_only(ORIGINAL[:14] + ORIGINAL), AUDIO)
        for broken in (b"", b"ID3", ORIGINAL[:12], b"ID3\x03\x00\x00\x80\x00\x00\x00" + AUDIO):
            with self.assertRaises(ValueError):
                repair.audio_only(broken)

    def test_dry_run_preserves_catalogue_and_apply_backs_up_before_upload(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            state = {"audio": ORIGINAL, "uploads": 0, "backedUpBeforeUpload": False}

            class Handler(BaseHTTPRequestHandler):
                def log_message(self, *_args):
                    pass

                def respond(self, status, body, headers):
                    self.send_response(status)
                    for key, value in headers.items():
                        self.send_header(key, value)
                    self.send_header("Content-Length", str(len(body)))
                    self.end_headers()
                    if self.command != "HEAD":
                        self.wfile.write(body)

                def do_GET(self):
                    if self.path.startswith("/api/songs"):
                        song = {"id": SONG, "fileUrl": f"http://127.0.0.1:{self.server.server_port}/api/media/blob/songs/original.mp3"}
                        body = json.dumps([song] if "?" in self.path else song).encode()
                        self.respond(200, body, {"Content-Type": "application/json"})
                    else:
                        audio = state["audio"]
                        headers = {"Content-Type": "audio/mpeg", "ETag": '"original"' if state["uploads"] == 0 else '"optimized"'}
                        if self.headers.get("Range"):
                            body = audio[:10]
                            headers["Content-Range"] = f"bytes 0-9/{len(audio)}"
                            self.respond(206, body, headers)
                        else:
                            self.respond(200, audio, headers)

                def do_HEAD(self):
                    self.do_GET()

                def do_POST(self):
                    content = self.rfile.read(int(self.headers["Content-Length"]))
                    message = BytesParser(policy=policy.default).parsebytes(
                        b"Content-Type: " + self.headers["Content-Type"].encode() + b"\r\n\r\n" + content)
                    state["backedUpBeforeUpload"] = any(path.read_bytes() == ORIGINAL for path in directory.glob("*.mp3"))
                    state["audio"] = message.get_payload()[0].get_payload(decode=True)
                    state["uploads"] += 1
                    self.respond(200, b"{}", {"Content-Type": "application/json"})

            server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            try:
                command = [sys.executable, str(SCRIPT), "--api", f"http://127.0.0.1:{server.server_port}"]
                environment = {**os.environ, "SPOTIBUDS_ACCESS_TOKEN": "test-only-token"}
                dry = subprocess.run(command, capture_output=True, text=True, env=environment)
                self.assertEqual(dry.returncode, 0, dry.stderr)
                self.assertIn("1 tagged MP3s, 0 repaired", dry.stdout)
                self.assertEqual(state["uploads"], 0)
                applied = subprocess.run(command + ["--apply", "--backup-dir", temporary], capture_output=True, text=True, env=environment)
                self.assertEqual(applied.returncode, 0, applied.stderr)
                self.assertEqual(state["audio"], AUDIO)
                self.assertTrue(state["backedUpBeforeUpload"])
                self.assertEqual(state["uploads"], 1)
                record = json.loads((directory / "repairs.jsonl").read_text())
                self.assertEqual(record["sha256"], hashlib.sha256(ORIGINAL).hexdigest())
                repeated = subprocess.run(command + ["--apply", "--backup-dir", temporary], capture_output=True, text=True, env=environment)
                self.assertEqual(repeated.returncode, 0, repeated.stderr)
                self.assertEqual(state["uploads"], 1)
                self.assertNotIn("test-only-token", applied.stdout + applied.stderr)
            finally:
                server.shutdown()
                server.server_close()
                thread.join()


if __name__ == "__main__":
    unittest.main()
