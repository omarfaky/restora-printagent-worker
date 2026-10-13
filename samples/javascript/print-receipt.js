const token = "replace-with-local-token";
const response = await fetch("http://127.0.0.1:5055/api/v1/print/receipt", {
  method: "POST",
  headers: { "Content-Type": "application/json", "X-Agent-Token": token },
  body: JSON.stringify({
    jobId: crypto.randomUUID(),
    printerId: "receipt",
    receipt: { title: "فاتورة", items: [] },
    copies: 1
  })
});
console.log(await response.json());
