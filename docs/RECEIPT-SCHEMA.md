# Structured receipt payload

The structured renderer uses the receipt shape produced by the existing upstream application renderer. It requires the top-level fields `orderType`, `items`, `totalAmount`, `payments`, and `createdAt`; optional display fields include `cafeName`, `currencyCode`, `invoiceNumber`, `customerName`, `customerPhone`, `cafePhone`, `headerLines`, and `footerLines`.

Each item requires `name`, integer `quantity`, decimal `lineTotal`, and an `addons` array. Add-ons in receipt payloads are objects with `name` and integer `quantityPerItem`. Payment entries require `methodName` and decimal `amount`. Dates should be ISO 8601 timestamps.

```json
{
  "cafeName": "مقهى المثال",
  "currencyCode": "EGP",
  "orderType": "DineIn",
  "orderNumber": 104,
  "createdAt": "2026-10-14T18:30:00+02:00",
  "items": [{ "name": "قهوة", "quantity": 1, "lineTotal": 75, "addons": [] }],
  "subtotal": 75,
  "totalAmount": 75,
  "payments": [{ "methodName": "نقدي", "amount": 75 }]
}
```
