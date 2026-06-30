# SQLServer Examples

> Advanced SQL Server scripts, CLR extensions, and integrations — async procedure execution, external API calls from T-SQL, OpenAI audio transcription, and performance administration.

![SQL Server](https://img.shields.io/badge/SQL%20Server-2019%2B-red)
![CLR](https://img.shields.io/badge/CLR-C%23%20Extensions-blue)
![Level](https://img.shields.io/badge/Level-Advanced-orange)

---

## Overview

A curated set of SQL Server solutions that go beyond standard T-SQL — including CLR-based extensions that allow SQL Server to make HTTP requests, execute async background tasks, and integrate with external AI services. These patterns solve real production problems where stored procedures need to reach beyond the database boundary.

---

## Project Structure

```
SQLServer-Examples/
├── SQLProcedureAssincrona/  ← C# CLR — async background procedure execution
├── SQLPostAPI/              ← C# CLR — HTTP, FTP, and WebDAV from T-SQL
└── OpenAITraduzirAudio/     ← C# CLR — Whisper audio transcription via SQL
```

---

## Projects

### CLR Extensions (C# inside SQL Server)

| Project | Description |
|---|---|
| [SQLProcedureAssincrona](SQLProcedureAssincrona/) | Execute stored procedures asynchronously from T-SQL — dispatches background tasks via ThreadPool without blocking the calling transaction |
| [SQLPostAPI](SQLPostAPI/) | Make HTTP GET/POST calls directly from SQL Server — also handles FTP upload, WebDAV upload, and multipart/form-data (Facebook Graph API) |
| [OpenAITraduzirAudio](OpenAITraduzirAudio/) | Transcribe audio files using OpenAI Whisper and store results via WebDAV — triggered as a SQL Server stored procedure |

---

## Getting Started

**Prerequisites:**
- SQL Server 2019+ with CLR integration enabled
- Visual Studio 2019+ for building the `.dll` assemblies
- SQL Server configured with `TRUSTWORTHY ON` or a certificate for `UNSAFE` assemblies

**Enable CLR integration** (run once in SQL Server):

```sql
sp_configure 'clr enabled', 1;
RECONFIGURE;
```

**Deploy an assembly** (example for SQLPostAPI):

```sql
-- Register the compiled DLL as a SQL Server assembly
CREATE ASSEMBLY SQLPostAPI
FROM 'C:\path\to\SQLPostAPI.dll'
WITH PERMISSION_SET = UNSAFE;

-- Create the stored procedure wrapper
CREATE PROCEDURE dbo.SQLApiPost (@url NVARCHAR(500), @body NVARCHAR(MAX), @result NVARCHAR(MAX) OUTPUT)
AS EXTERNAL NAME SQLPostAPI.[SQLPostAPI.UserDefinedFunctions].SQLApiPost;
```

Set environment variables before starting the SQL Server service (see Security Notes below).

---

## Patterns Covered

- **CLR integration** — extending SQL Server with compiled C# assemblies (`PERMISSION_SET = UNSAFE`)
- **Async execution** — ThreadPool-based background task pattern that avoids transaction blocking
- **External HTTP calls** — REST API consumption (GET/POST), file download, WebDAV and FTP upload
- **AI integration** — OpenAI Whisper audio transcription invoked from a T-SQL stored procedure
- **Multipart uploads** — building `multipart/form-data` requests in C# for third-party APIs (e.g. Facebook Graph)

## Security Notes

All sensitive values (API keys, server IPs, database credentials, FTP/WebDAV passwords) are read from **environment variables** — never hardcoded. Configure them in the Windows service environment before loading the assembly:

```sql
-- Example: setting an environment variable for the SQL Server service account
-- (run in the OS, not in SQL Server)
-- setx SQL_CLR_SERVER   "your-server-hostname"
-- setx SQL_CLR_USER     "sql-login"
-- setx SQL_CLR_PASSWORD "sql-password"
-- setx OPENAI_API_KEY   "sk-..."
-- setx WEBDAV_USER      "dav-user"
-- setx WEBDAV_PASSWORD  "dav-password"
-- setx FTP_USER         "ftp-user"
-- setx FTP_PASSWORD     "ftp-password"
```

Files excluded from this repo by `.gitignore`: `*.pfx`, `*.snk` (signing keys and certificates).

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
