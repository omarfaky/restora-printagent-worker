import json
import urllib.request
import uuid

payload = {
    "jobId": str(uuid.uuid4()),
    "printerId": "receipt",
    "receipt": {
        "cafeName": "Example Cafe",
        "currencyCode": "EGP",
        "orderType": "DineIn",
        "orderNumber": 104,
        "createdAt": "2026-10-14T18:30:00+02:00",
        "items": [{"name": "Coffee", "quantity": 1, "lineTotal": 75, "addons": []}],
        "subtotal": 75,
        "totalAmount": 75,
        "payments": [{"methodName": "Cash", "amount": 75}],
    },
}
request = urllib.request.Request(
    "http://127.0.0.1:5055/api/v1/print/receipt",
    data=json.dumps(payload).encode("utf-8"),
    headers={"Content-Type": "application/json", "X-Agent-Token": "replace-with-local-token"},
    method="POST",
)
with urllib.request.urlopen(request) as response:
    print(response.status, response.read().decode("utf-8"))
