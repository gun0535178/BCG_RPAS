using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BCG_RPAS.Controllers
{
    [Authorize]
    public class ApprovalsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ApprovalsController> _logger;

        public ApprovalsController(
            IHttpClientFactory httpClientFactory,
            ILogger<ApprovalsController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetInbox(CancellationToken cancellationToken)
        {
            try
            {
                var client = CreateApiClient();
                var response = await client.GetAsync("/api/approvals/inbox", cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                return new ContentResult
                {
                    Content = content,
                    ContentType = "application/json; charset=utf-8",
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during GET /api/approvals/inbox");
                return StatusCode(500, new { detail = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ได้" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Approve(int id, [FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            return await ForwardPostAsync($"/api/approvals/{id}/approve", payload, cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> Return(int id, [FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            return await ForwardPostAsync($"/api/approvals/{id}/return", payload, cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> Reject(int id, [FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            return await ForwardPostAsync($"/api/approvals/{id}/reject", payload, cancellationToken);
        }

        private HttpClient CreateApiClient()
        {
            var client = _httpClientFactory.CreateClient("RPAS_API");
            var token = User.FindFirst("AccessToken")?.Value;

            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return client;
        }

        private async Task<IActionResult> ForwardPostAsync(string endpoint, JsonElement payload, CancellationToken cancellationToken)
        {
            try
            {
                var client = CreateApiClient();
                var jsonString = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(endpoint, httpContent, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                return new ContentResult
                {
                    Content = content,
                    ContentType = "application/json; charset=utf-8",
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during POST {Endpoint}", endpoint);
                return StatusCode(500, new { detail = "เกิดข้อผิดพลาดในการส่งคำสั่งอนุมัติไปยัง API" });
            }
        }
    }
}
