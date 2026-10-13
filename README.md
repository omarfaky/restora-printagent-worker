# RESTORA Print Agent

RESTORA Print Agent is a local-first Windows service for reliable, silent printing to thermal ESC/POS printers. Your POS, web app, or workstation connector submits authenticated jobs to a localhost API; the agent saves them to SQLite and prints them asynchronously without showing a print dialog.

It is deliberately independent of any cloud provider or POS platform, so it can be used as-is or integrated into your own system.

## Why use it?

- **Local and private** — listens only on `127.0.0.1`; no public port is opened.
- **Reliable delivery workflow** — jobs are persisted in SQLite, processed in the background, and have queryable status.
- **Safe retries** — submissions are idempotent by `ClientId` and `JobId`, preventing duplicate jobs when clients retry.
- **Built for receipts** — prints structured JSON receipts, PNG/JPEG images, and optionally raw ESC/POS bytes.
- **Arabic-ready** — structured receipts are rasterized with an embedded Arabic font and bidirectional text support.
- **Flexible printer support** — supports network ESC/POS printers and Windows spooler printers.

> A successful submission means the agent accepted the job (`202 Accepted`), not that paper has physically left the printer. Always poll the job status before reporting completion upstream.

## Requirements

- Windows 10/11 or Windows Server for production printing
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) for local development
- A reachable network ESC/POS printer, or an installed Windows printer

The project can run its test suite on Linux, but the production print agent runs on Windows.

## Quick start

1. Clone the repository and enter its directory.

   ```powershell
   git clone https://github.com/omarfaky/restora-printagent-worker.git
   Set-Location restora-printagent-worker
   ```

2. Create your local configuration. This file is ignored by Git and must never be committed.

   ```powershell
   Copy-Item appsettings.template.json appsettings.json
   ```

3. Edit `appsettings.json`:

   - Set `PrintAgent:LocalToken` to a long, random secret.
   - Add the browser origins that may call the local API to `AllowedOrigins`.
   - Configure at least one printer profile under `Printers`.

4. Restore packages and run the agent.

   ```powershell
   dotnet restore
   dotnet run --project .\SilentPrintAgent.csproj
   ```

5. Verify that it is healthy.

   ```powershell
   Invoke-RestMethod http://127.0.0.1:5055/health
   ```

The default address is `http://127.0.0.1:5055`.

## Send your first receipt

Replace the token and printer ID below with values from your configuration.

```powershell
$headers = @{ "X-Agent-Token" = "replace-with-your-token" }
$body = @{
  jobId = "demo-receipt-001"
  printerId = "receipt"
  receipt = @{
    cafeName = "RESTORA Cafe"
    orderType = "DineIn"
    orderNumber = 104
    createdAt = (Get-Date).ToString("o")
    items = @()
    totalAmount = 0
    payments = @()
  }
  copies = 1
  openCashDrawer = $false
} | ConvertTo-Json -Depth 8

Invoke-RestMethod `
  -Method Post `
  -Uri "http://127.0.0.1:5055/api/v1/print/receipt" `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body
```

The response contains the job status URL. Poll it until the job reaches `Completed`, `Failed`, `Cancelled`, or `DeliveryUnknown`.

## Configuration at a glance

| Setting | Purpose |
| --- | --- |
| `DataDirectory` | Directory for the persistent SQLite queue. |
| `LocalApiPort` | Loopback port for the HTTP API; defaults to `5055`. |
| `LocalToken` | Shared bearer secret sent in `X-Agent-Token`. |
| `AllowedOrigins` | Explicit browser origins allowed by local CORS. |
| `AllowRawEscPos` | Enables raw printer bytes; keep `false` unless required. |
| `Printers` | Named, trusted local printer profiles. Clients submit a `PrinterId`, never a printer address. |

See [`appsettings.template.json`](appsettings.template.json) for a complete example.

## API overview

All `/api/v1` endpoints require the `X-Agent-Token` header. `GET /health` is public on localhost only.

| Method | Endpoint | Description |
| --- | --- | --- |
| `GET` | `/health` | Health check |
| `GET` | `/api/v1/printers` | Lists enabled configured printers |
| `POST` | `/api/v1/print/receipt` | Queues a structured JSON receipt |
| `POST` | `/api/v1/print/image` | Queues a PNG or JPEG upload |
| `POST` | `/api/v1/print/raw` | Queues raw ESC/POS data when enabled |
| `GET` | `/api/v1/jobs/{jobId}` | Gets job status |
| `POST` | `/api/v1/jobs/{jobId}/cancel` | Cancels a pending or retrying job |

Read the full request schemas and response behavior in [`docs/API.md`](docs/API.md), including the idempotency and uncertain-delivery rules.

## Run as a Windows service

For a production install, publish the project and run the included installer from an elevated PowerShell session:

```powershell
dotnet publish -c Release -o .\publish
.\Installer\install-service.ps1
```

The installer creates a protected configuration with a random token and runs the service as `LocalService` by default. Follow the complete verification and service-account guidance in [`docs/INSTALL-WINDOWS.md`](docs/INSTALL-WINDOWS.md).

## Integrating your product

Your application can call the local API directly when it runs on the same workstation. For a cloud backend, run a small local connector that polls or receives jobs from your service and forwards them to this API.

Use a stable job ID from your system, retry the exact same payload after timeouts, and report terminal agent status back to your backend. The repository includes ready-to-adapt examples:

- [`samples/javascript`](samples/javascript) — browser/Node.js examples
- [`samples/python`](samples/python) — Python example
- [`samples/dotnet`](samples/dotnet) — .NET example
- [`samples/curl`](samples/curl) — curl examples
- [`samples/cloud-adapter`](samples/cloud-adapter) — provider-neutral cloud bridge starter

The recommended integration and failure-handling model is documented in [`docs/INTEGRATION.md`](docs/INTEGRATION.md).

## Develop and contribute

```powershell
dotnet restore
dotnet build Restora.PrintAgent.sln
dotnet test .\tests\SilentPrintAgent.Tests\SilentPrintAgent.Tests.csproj
```

The codebase is organized to keep the HTTP API, queue, rendering, and transports isolated:

| Directory | Responsibility |
| --- | --- |
| `LocalApi/` | HTTP endpoints and localhost CORS policy |
| `Services/Queue/` | Persistent SQLite job storage and in-memory scheduling |
| `Services/Rendering/` and `Rendering/` | Receipt, image, and ESC/POS rendering |
| `Services/Transports/` | Network ESC/POS and Windows spooler transports |
| `Workers/` | Background queue processing |
| `tests/` | Unit and integration-oriented tests |

Before opening a change, keep secrets, local configuration, logs, and generated output out of commits. Add or update tests when behavior changes.

## Documentation

- [API reference](docs/API.md)
- [Windows installation](docs/INSTALL-WINDOWS.md)
- [Integration guide](docs/INTEGRATION.md)
- [Receipt JSON schema](docs/RECEIPT-SCHEMA.md)
- [Security model](docs/SECURITY.md)
- [Acceptance checklist](docs/ACCEPTANCE.md)

## Security notes

The API is intentionally limited to loopback, but any local process that knows the shared token can submit jobs. Protect `appsettings.json` with Windows permissions, do not place the token in a public frontend bundle, restrict `AllowedOrigins`, and rotate the token after exposure. Keep raw ESC/POS disabled unless you trust every caller.

For the full threat model and operational guidance, see [`docs/SECURITY.md`](docs/SECURITY.md).

## License

This project is licensed under the [MIT License](LICENSE). The bundled Noto Naskh Arabic font is distributed under its included [SIL Open Font License](Resources/Fonts/OFL.txt).
