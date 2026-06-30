# SQLServer Examples

> Advanced SQL Server scripts, CLR extensions, and integrations — async procedure execution, external API calls from T-SQL, OpenAI audio transcription, and performance administration.

![SQL Server](https://img.shields.io/badge/SQL%20Server-2019%2B-red)
![CLR](https://img.shields.io/badge/CLR-C%23%20Extensions-blue)
![Level](https://img.shields.io/badge/Level-Advanced-orange)

---

## Overview

A curated set of SQL Server solutions that go beyond standard T-SQL — including CLR-based extensions that allow SQL Server to make HTTP requests, execute async background tasks, and integrate with external AI services. These patterns solve real production problems where stored procedures need to reach beyond the database boundary.

---

## Examples

### CLR Extensions (C# inside SQL Server)

| Project | Description |
|---|---|
| [SQLServerDLL-Procedure-Assincrona](https://github.com/LucasRios/SQLServerDLL-Procedure-Assincrona) | Execute stored procedures asynchronously from T-SQL — runs background tasks without blocking the calling transaction |
| [SQLServerDLL-GET-POST-API](https://github.com/LucasRios/SQLServerDLL-GET-POST-API) | Make HTTP GET/POST calls directly from SQL Server — enables T-SQL to consume REST APIs |
| [SQLServerDLL-OpenAITraduzirAudio](https://github.com/LucasRios/SQLServerDLL-OpenAITraduzirAudio) | Transcribe and translate audio files using OpenAI Whisper, triggered from SQL Server |

### Administration & Performance

| Project | Description |
|---|---|
| [SQLServer-Comandos](https://github.com/LucasRios/SQLServer-Comandos) | Production DBA scripts — index fragmentation analysis, missing indexes, CPU consumption, table sizing, and automated maintenance |

---

## Patterns Covered

- **CLR integration** — extending SQL Server with compiled C# assemblies
- **Async execution** — background task patterns that avoid transaction blocking
- **External HTTP calls** — REST API consumption from stored procedures
- **AI integration** — OpenAI API invocation from inside the database layer
- **Performance tuning** — index analysis, fragmentation repair, query diagnosis

---

## Use Cases

These patterns are particularly relevant when:
- Legacy applications use stored procedures as the primary logic layer
- Data pipelines need to call external services without application middleware
- DBA teams need automation scripts that run inside the database engine
- Background processing must not block OLTP transactions

---

## Tech Stack

- **SQL Server 2019+** — primary platform
- **T-SQL** — stored procedures and scripts
- **C# CLR** — SQL Server extensions
- **OpenAI API** — audio transcription (Whisper)

---

## License

MIT
