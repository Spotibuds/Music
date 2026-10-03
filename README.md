# Music local demo

Music serves public artist, album and song metadata and validated catalogue media. Playlist visibility is explicit (`isPublic`, default true); owners and administrators may edit, and private playlists/covers are visible only to their owner and administrators. Owner IDs are Identity GUIDs; catalogue IDs are MongoDB ObjectIds. Actors always come from the validated JWT, whose session `sid` is checked against Identity before authenticated requests.

The supported demo has one Music instance, one authenticated MongoDB replica-set node, private Azurite containers, and .NET 10.0.401 SDK / 10.0.12 runtime. MongoDB transactions make song publication/album ordering and song deletion/playlist-reference removal atomic. Blob Storage remains outside Mongo transactions: durable cleanup intents are saved before upload, all replacements finish before metadata publication, and a cancellable worker deletes only unreferenced exact objects. Failed or interrupted replacements retain working metadata. Cleanup retries after dependency recovery and survives service restarts.

Album.Artist and Song.Album / Song.Artists immutable IDs are canonical relationships. Song.AlbumPosition is canonical album order. Album.Songs and Artist.Albums returned to clients are derived from canonical documents; embedded names are hydrated in bounded batches. Artist/album renames do not break discovery. Artist/album deletion returns409 while referenced; song deletion removes all playlist references and reindexes within one transaction. Playlists use versioned replacements under the single-instance command gate; addition/removal/reorder keeps unique contiguous positions. Reorder accepts an exact permutation at `PUT /api/playlists/{id}/songs/reorder` with `{ "songIds": [...] }`.

Run the complete stack from the Frontend repository using the versioned [demo guide](../../Frontend/demo/README.md). From the workspace root:

```powershell
pwsh -File Frontend/demo/New-LocalEnvironment.ps1
pwsh -File Frontend/demo/Demo.ps1 -Action Start
pwsh -File Frontend/demo/Test.ps1
pwsh -File Frontend/demo/Demo.ps1 -Action Stop
```

The root orchestration guide is authoritative for clean startup, fresh credentials, seed, reset and browser walkthrough. Stop preserves named volumes. Reset is an explicit destructive action scoped to this task's named demo resources.

Required runtime configuration (no production defaults or historical credentials):

| Key | Meaning |
|---|---|
| `ConnectionStrings__MongoDb` | authenticated single-node replica URI, `replicaSet=demo-rs` |
| `MongoDb__DatabaseName` | isolated Music database; demo uses `spotibuds_music_demo` |
| `AzureStorage__ConnectionString` | fresh Azurite account/key and internal endpoint `http://azurite:10000/spotibudsdemo` |
| `AzureStorage__PublicBaseUrl` | browser API origin, `http://127.0.0.1:5102` |
| `Jwt__Secret`, `Jwt__Issuer`, `Jwt__Audience` | shared generated signing secret, `spotibuds-local`, `spotibuds-demo` |
| `ServiceAuth__Secret` | fresh shared internal service credential |
| `IdentityService__BaseUrl` | internal `http://identity:8080` |
| `Cors__AllowedOrigins` | exact origin `http://127.0.0.1:3100` |
| `ASPNETCORE_URLS` or `ASPNETCORE_HTTP_PORTS` | honored by Kestrel; container uses8080 |

Unset/blank container options resolve `songs`, `artists`, `albums`, `playlists`; invalid names fail clearly. Storage configuration is mandatory. Redis is optional and Music uses no Redis/memory media cache. Blob-native ETags and `Cache-Control: no-cache` enforce revalidation and preserve byte accuracy without collisions, expiry or stale cache tiers. Media fields store stable API URLs, never SAS credentials. The proxy resolves only the configured account and allowed catalogue paths, then verifies a current permitted metadata reference. Users/other private containers and arbitrary hosts are rejected. No container-public-access helper or diagnostic test route exists.

`GET /health/live` is lightweight liveness. `GET /health/ready` requires successful index/migration initialization and bounded Mongo/Blob checks. Requests map unavailable dependencies to sanitized503 and recover without recreating clients. Authenticated writes additionally fail closed when Identity session validation is unavailable. Index initialization and placeholder-field removal are idempotent; known old Azure/Azurite catalogue URLs are normalized to stable URLs. `POST /api/admin/repair` is administrator-only and removes only the historical `placeholder` field; it does not reset any database.

PNG, JPEG and static WebP image bytes are decoded with Skia before upload. Maximum10MiB,4096px per dimension and16million pixels. PCM WAV is validated over the complete chunk structure and sample data; MP3/FLAC/Ogg must pass bounded full ffmpeg decode in the container. Audio is at most50MiB and domain duration1..7200sec. Supported media keeps its actual extension/MIME, regardless of filename or declared MIME. Global multipart limit150MiB. Public query pages use1..100 and nonnegative skip up to10000; playlist detail is capped at500 songs, list summaries contain at most20 expanded songs and a complete `songCount`. Albums contain at most500 songs, artists at most100 albums, accounts at most100 playlists. Search treats input as a literal string up to100characters, executes limited Mongo queries with max execution time, and is limited to60requests/minute/IP.

Admin upload contracts:

- `POST /api/admin/artists`: multipart `Name`, `Bio`, `ImageFile` optional.
- `POST /api/admin/albums`: multipart `Title`, `ArtistId`, `ReleaseDate`, `CoverFile` optional.
- `POST /api/admin/songs`: multipart `Title`, `ArtistId`, `AlbumId` optional, `Genre`, `Duration`, required `AudioFile`, optional `CoverFile`/`SnippetFile`.
- The original JSON CRUD routes remain, sharing the same validation/relationships/deletion commands.
- Multipart updates preserve omitted fields. An explicit empty `Bio` or `Genre` clears the saved text, and an empty `AlbumId` detaches the song and reindexes its former album. Supplied blank required names/titles/artist IDs return400 without saving changes.
- Bulk requires1..20 indexed `SongsData` metadata rows, matching `AudioFiles` and optional matching `CoverFiles`. Each row has a complete success/error result and stable item key. Supply `Idempotency-Key` header or multipart `IdempotencyKey` to resume the same payload after interruptions without duplicate songs. Different payloads under the same key409. Retry corrected failed rows with a new key. Fully completed operations return their original receipt.

Tests are in Music.sln and run the actual JWT/auth/CORS middleware. Persisted tests allocate only uniquely named `music_regression_*` databases and generated storage containers, assert resulting documents/media bytes, and remove only those resources afterwards. Configure `MUSIC_TEST_MONGO` and `MUSIC_TEST_STORAGE` to task-created disposable dependencies. Without them, persisted tests explicitly show skipped; helper-only green tests do not prove full demo readiness. `Frontend/demo/Test.ps1` supplies them safely from ignored fresh local settings.

```powershell
dotnet build Music.sln -c Release --no-restore
dotnet test Music.sln -c Release --no-restore
dotnet list Music.sln package --vulnerable --include-transitive --no-restore
```

Current dependencies were checked against NuGet's live vulnerability feed; the verification evidence and each original audit ID are recorded in [remediation.json](remediation.json). .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy . Skia package/license: https://www.nuget.org/packages/SkiaSharp/4.153.1 . ImageSharp4.x was not adopted because its license gate requires a purchased license; no license check was weakened.

Production-only follow-ups: more than one Music instance requires distributed catalogue locking or a transaction-only command design, scoped cloud credentials/TLS/proxy infrastructure, production load/resource testing and backups. No historical external credential revocation is claimed.
