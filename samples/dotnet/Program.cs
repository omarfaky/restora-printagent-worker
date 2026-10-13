using System.Net.Http.Json;

using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5055") };
client.DefaultRequestHeaders.Add("X-Agent-Token", "replace-with-local-token");
var request = new
{
    jobId = Guid.NewGuid().ToString("N"),
    printerId = "receipt",
    receipt = new
    {
        cafeName = "Example Cafe",
        currencyCode = "EGP",
        orderType = "DineIn",
        orderNumber = 104,
        createdAt = DateTimeOffset.Now,
        items = new[] { new { name = "Coffee", quantity = 1, lineTotal = 75m, addons = Array.Empty<object>() } },
        subtotal = 75m,
        totalAmount = 75m,
        payments = new[] { new { methodName = "Cash", amount = 75m } }
    }
};
using var response = await client.PostAsJsonAsync("/api/v1/print/receipt", request);
Console.WriteLine(await response.Content.ReadAsStringAsync());
