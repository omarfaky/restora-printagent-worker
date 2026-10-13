const token = "replace-with-local-token";
const metadata = { jobId: crypto.randomUUID(), printerId: "receipt", copies: 1 };
const form = new FormData();
form.append("metadata", JSON.stringify(metadata));
form.append("file", document.querySelector("input[type=file]").files[0]);
const response = await fetch("http://127.0.0.1:5055/api/v1/print/image", {
  method: "POST",
  headers: { "X-Agent-Token": token },
  body: form
});
console.log(await response.json());
