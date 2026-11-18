using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BCG_RPAS.Controllers
{
    [Authorize]
    public class ErpMonitoringController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ErpMonitoringController> _logger;

        public ErpMonitoringController(
            IHttpClientFactory httpClientFactory,
            ILogger<ErpMonitoringController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        private bool IsRequesterRole()
        {
            var roleCode = User.FindFirst("RoleCode")?.Value;
            return string.Equals(roleCode, "REQUESTER", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (IsRequesterRole())
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        {
            if (IsRequesterRole())
            {
                return StatusCode(403, new { detail = "ไม่มีสิทธิ์เข้าถึงส่วนการติดตาม ERP" });
            }

            return await ForwardGetAsync("/api/erp/monitoring", cancellationToken);
        }

        [HttpGet]
        public async Task<IActionResult> GetAttempts(int id, CancellationToken cancellationToken)
        {
            if (IsRequesterRole())
            {
                return StatusCode(403, new { detail = "ไม่มีสิทธิ์เข้าถึงส่วนการติดตาม ERP" });
            }

            return await ForwardGetAsync($"/api/erp/monitoring/{id}/attempts", cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> RetryJob(int id, CancellationToken cancellationToken)
        {
            if (IsRequesterRole())
            {
                return StatusCode(403, new { detail = "ไม่มีสิทธิ์เข้าถึงส่วนการติดตาม ERP" });
            }

            return await ForwardPostEmptyAsync($"/api/erp/monitoring/{id}/retry", cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessDue(CancellationToken cancellationToken)
        {
            if (IsRequesterRole())
            {
                return StatusCode(403, new { detail = "ไม่มีสิทธิ์เข้าถึงส่วนการติดตาม ERP" });
            }

            return await ForwardPostEmptyAsync("/api/erp/monitoring/process-due", cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> SetMode([FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            if (IsRequesterRole())
            {
                return StatusCode(403, new { detail = "ไม่มีสิทธิ์เข้าถึงส่วนการติดตาม ERP" });
            }

            try
            {
                var client = CreateApiClient();
                var jsonString = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("/api/erp/monitoring/mode", httpContent, cancellationToken);
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
                _logger.LogError(ex, "Error setting ERP mock mode");
                return StatusCode(500, new { detail = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ API ได้" });
            }
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

        private async Task<IActionResult> ForwardGetAsync(string endpoint, CancellationToken cancellationToken)
        {
            try
            {
                var client = CreateApiClient();
                var response = await client.GetAsync(endpoint, cancellationToken);
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
                _logger.LogError(ex, "Error during GET {Endpoint}", endpoint);
                return StatusCode(500, new { detail = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ได้" });
            }
        }

        private async Task<IActionResult> ForwardPostEmptyAsync(string endpoint, CancellationToken cancellationToken)
        {
            try
            {
                var client = CreateApiClient();
                var httpContent = new StringContent("{}", Encoding.UTF8, "application/json");

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
                return StatusCode(500, new { detail = "เกิดข้อผิดพลาดในการส่งคำสั่งไปยัง API" });
            }
        }
    }
}
