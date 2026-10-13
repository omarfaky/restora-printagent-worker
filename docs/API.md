# Local API v1

All endpoints bind to `127.0.0.1`. Send `X-Agent-Token` on every `/api/v1` request. `GET /health` is unauthenticated.

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/v1/printers` | List enabled profiles |
| POST | `/api/v1/print/receipt` | Queue a structured receipt |
| POST | `/api/v1/print/image` | Queue a PNG or JPEG |
| POST | `/api/v1/print/raw` | Queue raw ESC/POS when enabled |
| GET | `/api/v1/jobs/{jobId}` | Read status |
| POST | `/api/v1/jobs/{jobId}/cancel` | Cancel a pending/retrying job |

`receipt` accepts JSON:

```json
{"jobId":"sale-1001","printerId":"receipt","receipt":{"cafeName":"مقهى المثال","orderType":"DineIn","orderNumber":104,"createdAt":"2026-10-14T18:30:00+02:00","items":[],"totalAmount":0,"payments":[]},"copies":1,"openCashDrawer":false}
```

`image` and `raw` use `multipart/form-data`: a `metadata` field containing `{"jobId":"sale-1002","printerId":"receipt"}` and a `file` field. Images must be `image/png` or `image/jpeg`; raw data must use `application/octet-stream`.

Every successful submit returns `202` with a status URL. Status contains `Pending`, `Retrying`, `Printing`, `Completed`, `Failed`, `Cancelled`, or `DeliveryUnknown`; it never returns receipt bytes.
