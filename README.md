# Spotibuds Music API

The Music API is the ASP.NET Core service for music catalogue and playlist operations in Spotibuds. It exposes REST endpoints for songs, albums, artists, playlists, search, and media delivery.

## Responsibilities

- Catalogue and playlist CRUD backed by MongoDB.
- Audio, image, cover, and snippet delivery through Azure Blob Storage.
- Two-level media caching: process-local `IMemoryCache`, followed by Redis with a six-hour media entry lifetime.
- Swagger/OpenAPI and health endpoints for local and deployed diagnostics.

The deployed platform also contains separate Identity and User APIs. PostgreSQL and Entity Framework migrations belong to the Identity service; this service does not use PostgreSQL for catalogue storage.

## Architecture

See [`docs/architecture.md`](docs/architecture.md) for the request flow, dependencies, and current limitations.

## Run locally

Requirements: .NET 8 SDK, MongoDB, Redis, and an Azure Blob Storage account (or a test-compatible storage endpoint).

1. Restore and build:

   ```bash
   dotnet restore Music.csproj
   dotnet build Music.csproj --configuration Release
   ```

2. Supply configuration through environment variables. The double-underscore form is understood by ASP.NET Core configuration:

   ```bash
   ConnectionStrings__MongoDb="mongodb://localhost:27017/spotibuds"
   ConnectionStrings__Redis="localhost:6379"
   Cors__AllowedOrigins="http://localhost:3000"
   AzureStorage__ConnectionString="<redacted-storage-connection-string>"
   AzureStorage__SongsContainer="songs"
   AzureStorage__ArtistsContainer="artists"
   AzureStorage__AlbumsContainer="albums"
   AzureStorage__PlaylistsContainer="playlists"
   dotnet run --project Music.csproj
   ```

   Never commit real connection strings, SAS tokens, publish profiles, or `.env` files. The checked-in appsettings files intentionally contain empty values.

3. Check the process and dependency health:

   ```text
   GET /health
   GET /health/mongodb
   ```

Swagger is available at `/swagger` when the service is running.

## Docker

```bash
docker build -t spotibuds-music-api .
docker run --rm -p 8080:80 \
  -e ConnectionStrings__MongoDb="mongodb://host.docker.internal:27017/spotibuds" \
  -e ConnectionStrings__Redis="host.docker.internal:6379" \
  -e Cors__AllowedOrigins="http://localhost:3000" \
  spotibuds-music-api
```

Azure Blob Storage variables are required for media upload/download endpoints. The image does not contain application secrets.

## Verification

There is currently no automated test project in this repository. A local Release build succeeds, but it reports nullable-flow and unused-exception warnings. End-to-end catalogue, blob-storage, Redis-cache, and authorization behaviour still require tests against disposable dependencies before this service should be described as production-hardened.

## Selected endpoint groups

| Area | Routes |
| --- | --- |
| Catalogue | `/api/songs`, `/api/albums`, `/api/artists`, `/api/playlists` |
| Search | `/api/search` |
| Media | `/api/media/image`, `/api/media/audio` |
| Health | `/health`, `/health/mongodb` |

## Security notes

Application secrets are supplied through runtime configuration and are not committed. Administrative write routes and operational cache routes also need explicit authentication and authorization before public deployment.
