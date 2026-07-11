# Spotibuds Music API

The Music API is the ASP.NET Core service for catalogue, playlist, search, and media features in Spotibuds. Spotibuds was a team project, and this repository covers one service within the wider application.

## Main functionality

- Song, album, artist, and playlist data stored in MongoDB.
- Audio, images, covers, and snippets stored in Azure Blob Storage.
- Image caching through `IMemoryCache` and Redis, with Blob Storage as the source of truth.
- JWT validation using the token format issued by the Spotibuds Identity service.
- Public catalogue, search, media, Swagger, and health routes.
- Admin-only catalogue writes and cache operations.
- Playlist changes limited to the playlist owner or an Admin.

The Identity service uses PostgreSQL and Entity Framework Core. The Music API does not use PostgreSQL for its catalogue data.

## Stack

- .NET 8 and ASP.NET Core
- MongoDB
- Redis
- Azure Blob Storage
- Docker
- xUnit

See [`docs/architecture.md`](docs/architecture.md) for the request flow and service boundaries.

## Run locally

You need the .NET 8 SDK, MongoDB, Redis, and an Azure Blob Storage account or compatible test endpoint.

Restore and build:

```bash
dotnet restore Music.csproj
dotnet build Music.csproj --configuration Release
```

Set the service configuration, then run the project:

```bash
ConnectionStrings__MongoDb="mongodb://localhost:27017/spotibuds"
ConnectionStrings__Redis="localhost:6379"
Cors__AllowedOrigins="http://localhost:3000"
AzureStorage__ConnectionString="<redacted-storage-connection-string>"
AzureStorage__SongsContainer="songs"
AzureStorage__ArtistsContainer="artists"
AzureStorage__AlbumsContainer="albums"
AzureStorage__PlaylistsContainer="playlists"
Jwt__Secret="<same-signing-secret-used-by-Identity>"
Jwt__Issuer="SpotibudsIdentity"
Jwt__Audience="SpotibudsApp"
dotnet run --project Music.csproj
```

ASP.NET Core also accepts these settings from other configuration providers. Never commit connection strings, JWT secrets, SAS tokens, publish profiles, or `.env` files.

Swagger is available at `/swagger`. Health checks are:

```text
GET /health
GET /health/mongodb
```

## Docker

```bash
docker build -t spotibuds-music-api .
docker run --rm -p 8080:80 \
  -e ConnectionStrings__MongoDb="mongodb://host.docker.internal:27017/spotibuds" \
  -e ConnectionStrings__Redis="host.docker.internal:6379" \
  -e Cors__AllowedOrigins="http://localhost:3000" \
  -e Jwt__Secret="<same-signing-secret-used-by-Identity>" \
  spotibuds-music-api
```

Azure Blob Storage settings are also required for media upload and download routes.

## Tests

```bash
dotnet test tests/Music.Tests/Music.Tests.csproj --configuration Release
```

The test project uses mocks, so it does not need Azure, MongoDB, or Redis credentials. It covers playlist owner/Admin decisions, MongoDB failures, invalid media input, Blob Storage errors, and Redis-to-Blob fallback.

## Route groups

| Area | Routes |
| --- | --- |
| Catalogue | `/api/songs`, `/api/albums`, `/api/artists`, `/api/playlists` |
| Search | `/api/search` |
| Media | `/api/media/image`, `/api/media/audio` |
| Health | `/health`, `/health/mongodb` |

## Still to do

- Add end-to-end tests with disposable MongoDB, Redis, and Blob Storage dependencies.
- Finish the remaining nullable-flow warning fixes in Blob Storage and controller paths.
- Improve monitoring for cache failures that currently fall back to Blob Storage.
