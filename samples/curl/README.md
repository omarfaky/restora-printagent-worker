# curl examples

Submit a receipt:

```powershell
curl.exe -X POST http://127.0.0.1:5055/api/v1/print/receipt `
  -H "X-Agent-Token: replace-with-local-token" -H "Content-Type: application/json" `
  --data-binary "{\"jobId\":\"sale-1001\",\"printerId\":\"receipt\",\"receipt\":{\"cafeName\":\"Example\",\"orderType\":\"DineIn\",\"orderNumber\":104,\"createdAt\":\"2026-10-14T18:30:00+02:00\",\"items\":[],\"totalAmount\":0,\"payments\":[]}}"
```

Submit an image:

```powershell
curl.exe -X POST http://127.0.0.1:5055/api/v1/print/image `
  -H "X-Agent-Token: replace-with-local-token" `
  -F 'metadata={"jobId":"image-1001","printerId":"receipt"};type=application/json' `
  -F "file=@receipt.png;type=image/png"
```

Use the `statusUrl` returned by the submit response to poll job status.
