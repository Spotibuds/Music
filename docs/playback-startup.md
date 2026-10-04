# Faster playback startup

Some imported MP3s carry embedded APIC artwork. In the tested Windows Edge/Chromium
decoder this causes much more audio to be read before playback becomes ready.
An isolated 1 Mbps benchmark of a three-megabyte sample of “Spiral Static” started
in 20.5 seconds with the original tag, 1.6 seconds with only its APIC frame removed,
and 0.5 seconds with the leading ID3 tag removed. These are controlled diagnostic
measurements, not guaranteed timings on every browser or connection.

`AzureBlobService.Validate` now prepares MP3 playback uploads with
`Mp3Playback.AudioOnly` before the existing full audio decoder validation. It
removes bounded, validated leading ID3v2 tags (including v2.4 footers) and leaves
all following bytes, including MPEG frames and their Xing/Info seek headers,
unchanged. It does not re-encode music. Catalogue titles, artists and image URLs
remain separate. Source downloads and the Telegram importer are unchanged.

New uploads use this path after deploying the Music service. Previously uploaded
assets do not automatically change. The frontend still requests immediate
playback and lets the browser stream and buffer the current track. It exposes
the buffer extent and warms the next known song with a separate, silent media
element using `preload="metadata"` after the current song has at least ten seconds
buffered. The warmed element is reused at transition, not restarted. Preloading
stops during current-track buffering, pause, teardown, or when the browser reports
data saving / a 2G connection; unpredictable shuffle selections are not prefetched.
Metadata preload is a browser hint, not an exact byte limit.

## Repairing existing MP3s

`scripts/optimize-playback.py` audits existing tracks by default. It has no third
party Python dependencies. Use a Music API base URL, including its deployment
prefix when needed:

```powershell
python Music/scripts/optimize-playback.py --api http://127.0.0.1:5102
```

For a bounded audit, append `--song <24-character-song-id>`; the argument can be
repeated. Do not use a live deployment for tests or generated fixtures.

To repair, first deploy the reviewed Music changes and pause catalogue imports
and concurrent song uploads. Set an administrator access token in
`SPOTIBUDS_ACCESS_TOKEN`. Alternatively set `SPOTIBUDS_USERNAME`,
`SPOTIBUDS_PASSWORD` and `SPOTIBUDS_IDENTITY_API` for administrator sign-in. Use
HTTPS for deployed APIs. Do not paste credentials into command arguments.

```powershell
python Music/scripts/optimize-playback.py --api http://127.0.0.1:5102 --song <song-id> --apply --backup-dir Music/tmp/audio-backups
```

Apply mode stores the original bytes under a checksum-based filename and flushes
the backup and `repairs.jsonl` before uploading an optimized copy through the
existing administrator audio-upload endpoint. It checks that the song still
references the same storage object and its ETag has not changed. These checks
do not replace a server-side conditional mutation: imports must remain paused.
Original local downloads are never edited. Keep the backup directory outside Git
and preserve it after the repair. The normal catalogue cleanup may remove the
superseded remote blob; the local backup is the recovery copy. Already tag-free
files are skipped on repeated runs.

Validation: frontend playback/buffer regressions; exact byte-preservation and
malformed tag/footer tests in `Mp3PlaybackTests`; a local HTTP repair-tool test
verifying read-only audit, durable original backup before upload, and repeat-safe
repair. The UI review uses generated WAV audio at desktop and mobile widths;
it does not verify live service persistence. Persisted backend tests require the
isolated MongoDB/Blob dependencies documented by the test project.
