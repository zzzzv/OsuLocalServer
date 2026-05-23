# OsuLocalServer

Local HTTP servers that expose osu! game data via REST APIs. Two projects:

- **Stable** — serves files from the osu!stable installation directory
- **Lazer** — queries Realm database from osu!lazer installation

## API — Stable (port 5167)

| Method | Route | Parameters | Description |
| ------ | ----- | ---------- | ----------- |
| GET | `/api/status` | — | Server status and osu! root path |
| GET | `/files/{**relativePath}` | — | Serve any file from the osu! directory. Use `*` as a wildcard in any path segment to match files without knowing the exact name. The first match is returned with its real filename. |

Examples:

```http
# Exact match (original behaviour)
GET /files/Data/r/7b143bd479e4284d1b219afd6e69615d-134239924909535197.osr

# Wildcard match – * matches any characters (returns the file above)
GET /files/Data/r/7b143bd479e4284d1b219afd6e69615d-*.osr
```

## API — Lazer (port 5048)

All query endpoints accept RQL (Realm Query Language) strings and an optional `depth` parameter for nested object expansion (`depth=0` by default, returns only value types and strings).

| Method | Route | Parameters | Description |
| ------ | ----- | ---------- | ----------- |
| GET | `/api/status` | — | Server status and data directory |
| GET | `/api/scores` | `rql`, `depth` | Query `ScoreInfo` |
| GET | `/api/beatmaps` | `rql`, `depth` | Query `BeatmapInfo` |
| GET | `/api/beatmap-sets` | `rql`, `depth` | Query `BeatmapSetInfo` |
| GET | `/api/collections` | `rql`, `depth` | Query `BeatmapCollection` |
| GET | `/files/{hash}` | — | Serve file by content hash from `<DataDirectory>/files/` |

At `depth=0`, only value types (int, long, double, bool, Guid, DateTimeOffset, enums, etc.) and strings are included. At `depth>0`, nested `RealmObject` references and `IList<T>` collections are recursively expanded as dictionaries.

## Configuration

The config file is created automatically on first launch with default values. Edit it to override settings:

### Stable (`Stable/OsuLocalServer.Stable.json`)

```json
{
  "Urls": "http://localhost:5167",
  "AppSettings": {
    "OsuRootPath": null               // auto-detected from registry if null
  }
}
```

### Lazer (`Lazer/OsuLocalServer.Lazer.json`)

```json
{
  "Urls": "http://localhost:5048",
  "LazerPaths": {
    "LazerCurrentDirectory": null,    // defaults to %LOCALAPPDATA%/osulazer/current
    "DataDirectory": null,            // defaults to %APPDATA%/osu
    "TempDirectory": null             // defaults to %TEMP%/lazer
  }
}
```
