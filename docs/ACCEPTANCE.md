# Manual acceptance checklist

Run local cases first with a disposable SQLite data directory and the fake transport tests. Then repeat hardware cases while signed in as the Windows Service account.

- [ ] `GET /health` responds while the Agent is bound only to `127.0.0.1`; no cloud configuration is required.
- [ ] Missing or incorrect `X-Agent-Token` returns `401`; a disallowed browser origin returns `403`.
- [ ] Submit the same receipt ID and content twice: both return `202` with one stored job. Reuse that ID with different content: `409`.
- [ ] Submit an Arabic structured receipt and verify glyph shaping, order, width, and cut position.
- [ ] Submit PNG and JPEG multipart requests; verify scaling at 58 mm and 80 mm. Corrupt and oversized images must be rejected without transport calls.
- [ ] Confirm raw ESC/POS returns `403` while disabled, then enable it only for a trusted client and verify a known safe test sequence.
- [ ] Test TCP and Windows Spooler transports with real devices. Verify offline-before-send reaches `Retrying`; interrupt after send starts and verify `DeliveryUnknown`.
- [ ] Restart the service with a pending job and verify it remains queued. Restart with an expired `Printing` lease and verify it becomes `DeliveryUnknown`.
- [ ] Race cancel against printing: pending work becomes `Cancelled`; already printing returns `409`.
- [ ] Verify Windows Service account can see its configured printer and read/write the data directory; confirm another standard user cannot read the token file.

Do not repeat an uncertain print with a new job ID until an operator decides whether a duplicate is acceptable.
