"""Audit MP3 startup metadata, or repair it after saving original uploads locally.

Read-only by default. --apply requires --backup-dir and administrator credentials
in SPOTIBUDS_ACCESS_TOKEN, or SPOTIBUDS_USERNAME / SPOTIBUDS_PASSWORD. Credentials
and signed media URLs are never logged. Pause concurrent imports during a repair.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit
from urllib.request import Request, urlopen

MAX_AUDIO = 50 * 1024 * 1024


def audio_only(source):
    offset = 0
    while source[offset:offset + 3] == b"ID3":
        header = source[offset:offset + 10]
        if len(header) < 10 or header[3] not in (2, 3, 4) or header[4] == 255:
            raise ValueError("Invalid MP3 metadata header")
        if any(value >= 128 for value in header[6:10]):
            raise ValueError("Invalid MP3 metadata size")
        size = 0
        for value in header[6:10]:
            size = (size << 7) | value
        footer = header[3] == 4 and header[5] & 16
        length = 10 + size + (10 if footer else 0)
        if offset + length > len(source):
            raise ValueError("Truncated MP3 metadata")
        if footer and source[offset + 10 + size:offset + length] != b"3DI" + header[3:]:
            raise ValueError("Invalid MP3 metadata footer")
        offset += length
    if offset == len(source):
        raise ValueError("MP3 contains no audio")
    return source[offset:]


def request(url, method="GET", headers=None, data=None, maximum=MAX_AUDIO):
    with urlopen(Request(url, data=data, headers=headers or {}, method=method), timeout=120) as response:
        body = response.read(maximum + 1)
        if len(body) > maximum:
            raise ValueError("Response exceeds the audio size limit")
        return body, response.headers


def json_request(url, headers=None, data=None):
    body, _ = request(url, "POST" if data is not None else "GET",
                      {**(headers or {}), "Content-Type": "application/json"},
                      json.dumps(data).encode() if data is not None else None)
    return json.loads(body)


def backup(original, song_id, directory):
    digest = hashlib.sha256(original).hexdigest()
    destination = directory / f"{song_id}-{digest}.mp3"
    if destination.exists():
        if hashlib.sha256(destination.read_bytes()).hexdigest() != digest:
            raise ValueError("Existing backup failed its checksum")
    else:
        with destination.open("xb") as handle:
            handle.write(original)
            handle.flush()
            os.fsync(handle.fileno())
    return digest, destination.name


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", required=True, help="Music API base URL, including /services/music if deployed")
    parser.add_argument("--song", action="append", default=[], help="Only audit/repair this song ID; repeat as needed")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--backup-dir", type=Path)
    args = parser.parse_args()
    api = args.api.rstrip("/")
    parsed = urlsplit(api)
    if parsed.scheme not in ("http", "https") or not parsed.netloc or parsed.username or parsed.password or parsed.query or parsed.fragment:
        parser.error("--api must be an HTTP(S) base URL without credentials")
    if args.apply and not args.backup_dir:
        parser.error("--apply requires --backup-dir to preserve original uploads")
    headers = {}
    if args.apply:
        token = os.environ.get("SPOTIBUDS_ACCESS_TOKEN")
        if not token:
            username, password = os.environ.get("SPOTIBUDS_USERNAME"), os.environ.get("SPOTIBUDS_PASSWORD")
            if not username or not password:
                parser.error("Set SPOTIBUDS_ACCESS_TOKEN, or SPOTIBUDS_USERNAME and SPOTIBUDS_PASSWORD")
            # Deployed APIs share the /services prefix; local Identity uses port 5101.
            identity = os.environ.get("SPOTIBUDS_IDENTITY_API")
            if not identity:
                parser.error("Set SPOTIBUDS_IDENTITY_API when signing in with username/password")
            token = json_request(identity.rstrip("/") + "/api/auth/login",
                                 data={"username": username, "password": password})["token"]
        headers = {"Authorization": "Bearer " + token}
        args.backup_dir.mkdir(parents=True, exist_ok=True)
    songs = []
    if args.song:
        if any(not re.fullmatch(r"[0-9a-fA-F]{24}", value) for value in args.song):
            parser.error("Song IDs must be 24 hexadecimal characters")
        songs = [json_request(api + "/api/songs/" + value) for value in args.song]
    else:
        while True:
            page = json_request(api + f"/api/songs?limit=100&skip={len(songs)}")
            songs.extend(page)
            if len(page) < 100:
                break
    found = repaired = 0
    for song in songs:
        song_id, file_url = song["id"], song.get("fileUrl")
        if not file_url:
            continue
        if not re.fullmatch(r"[0-9a-fA-F]{24}", song_id):
            raise ValueError("Catalogue returned an invalid song ID")
        media = api + "/api/media/audio?url=" + quote(file_url, safe="")
        prefix, metadata = request(media, headers={"Range": "bytes=0-9"}, maximum=10)
        if metadata.get("Content-Type", "").split(";")[0] != "audio/mpeg" or prefix[:3] != b"ID3":
            continue
        found += 1
        print(f"MP3 metadata found: {song_id}", flush=True)
        if not args.apply:
            continue
        original, original_headers = request(media)
        optimized = audio_only(original)
        digest, filename = backup(original, song_id, args.backup_dir)
        with (args.backup_dir / "repairs.jsonl").open("a", encoding="utf-8") as manifest:
            manifest.write(json.dumps({"songId": song_id, "original": filename, "sha256": digest,
                                       "originalBytes": len(original), "playbackBytes": len(optimized)}) + "\n")
            manifest.flush()
            os.fsync(manifest.fileno())
        current = json_request(api + "/api/songs/" + song_id)
        if urlsplit(current["fileUrl"])[:3] != urlsplit(file_url)[:3]:
            raise ValueError("Song audio changed during repair; original saved, upload skipped")
        _, current_headers = request(media, "HEAD", maximum=0)
        if original_headers.get("ETag") != current_headers.get("ETag"):
            raise ValueError("Audio bytes changed during repair; original saved, upload skipped")
        boundary = "spotibuds-" + secrets.token_hex(16)
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="audioFile"; filename="playback.mp3"\r\n'
                'Content-Type: audio/mpeg\r\n\r\n').encode() + optimized + f"\r\n--{boundary}--\r\n".encode()
        request(api + f"/api/songs/{song_id}/upload-file", "POST",
                {**headers, "Content-Type": "multipart/form-data; boundary=" + boundary}, body)
        repaired += 1
        print(f"Playback optimized: {song_id}; original saved", flush=True)
    print(f"{'Repair' if args.apply else 'Read-only audit'} complete: {found} tagged MP3s, {repaired} repaired")


if __name__ == "__main__":
    try:
        main()
    except HTTPError as error:
        raise SystemExit(f"Request failed (HTTP {error.code}); credentials and media URLs omitted") from None
    except Exception as error:
        # Network errors may contain signed URLs; never dump their messages.
        raise SystemExit(f"Repair stopped ({type(error).__name__}); inspect backups before retrying") from None
