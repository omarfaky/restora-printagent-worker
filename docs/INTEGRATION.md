# Integration

The submit call is asynchronous. Generate and retain a stable `jobId`; if the HTTP call times out, repeat the exact same request with the same ID. Poll the returned status URL rather than sending another job ID.

See `samples/javascript/print-receipt.js` for a browser example. Browser applications should keep the local token outside publicly distributed frontend bundles when possible; a localhost integration is still a trusted-machine boundary, not a public API.

## Connecting your own cloud backend

This repository deliberately does not contain a cloud URL, credentials, pairing flow, or provider SDK. The adopting product owns that part. For a cloud that cannot call a workstation's `127.0.0.1` API directly, run a small connector on the workstation (or alongside the agent) that polls your backend and uses this local API:

1. Claim a cloud job using your backend's own authentication and lease mechanism.
2. Map its payload and printer selection to `POST /api/v1/print/receipt`, `/image`, or `/raw`.
3. Use the cloud job's stable ID as `jobId`; retry the exact same payload with that ID after timeouts. The Agent's idempotency behavior prevents a second local job while the record is retained.
4. Poll `GET /api/v1/jobs/{jobId}` until a terminal state, then report that state through your backend's API.
5. Renew the cloud lease while printing if the backend supports leases. Do not claim a job is printed merely because the local API returned `202`.

Map `Completed` to transport acknowledgement, not proof that paper physically exited the printer. Report `DeliveryUnknown` as uncertain and do not silently claim or submit a replacement job: bytes may already have reached the printer. `Failed` means the Agent exhausted its configured retries or rejected the job. Keep the local token on the workstation, not in a public browser bundle, and map only trusted configured `printerId` values; never accept a printer address from a cloud job without validating it against local configuration.

`samples/cloud-adapter/` contains a provider-neutral bridge outline and the local API client portion. The adopter supplies the backend-specific claim, lease-renewal, and report calls. No change to the Agent's print core is needed for a connector that uses HTTP.
