using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BCG_RPAS.Controllers
{
    [Authorize]
    public class RentalRequestsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RentalRequestsController> _logger;

        public RentalRequestsController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<RentalRequestsController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        // ==========================================
        // 1. Razor View Pages
        // ==========================================

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            if (id <= 0)
            {
                return RedirectToAction(nameof(Index));
            }
            ViewBag.RequestId = id;
            return View();
        }

        [HttpGet]
        public IActionResult Details(int id)
        {
            if (id <= 0)
            {
                return RedirectToAction(nameof(Index));
            }
            ViewBag.RequestId = id;
            return View();
        }

        // ==========================================
        // 2. BFF / AJAX Proxy Endpoints
        // ==========================================

        [HttpPost]
        public async Task<IActionResult> GetDataTable([FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            return await ForwardPostAsync("/api/rental-requests/datatable", payload, cancellationToken);
        }

        [HttpGet]
        public async Task<IActionResult> GetMasters(CancellationToken cancellationToken)
        {
            try
            {
                var client = CreateApiClient();

                var contractsTask = client.GetAsync("/api/master/contract", cancellationToken);
                var costCentersTask = client.GetAsync("/api/master/cost-center", cancellationToken);

                await Task.WhenAll(contractsTask, costCentersTask);

                var contractsRes = await contractsTask;
                var costCentersRes = await costCentersTask;

                if (contractsRes.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    costCentersRes.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return Unauthorized(new { detail = "เซสชันหมดอายุ หรือไม่มีสิทธิ์เข้าถึง API" });
                }

                if (!contractsRes.IsSuccessStatusCode || !costCentersRes.IsSuccessStatusCode)
                {
                    return StatusCode(500, new { message = "ไม่สามารถโหลดข้อมูล Master ได้" });
                }

                var contractsJson = await contractsRes.Content.ReadAsStringAsync(cancellationToken);
                var costCentersJson = await costCentersRes.Content.ReadAsStringAsync(cancellationToken);

                var contracts = JsonSerializer.Deserialize<JsonElement>(contractsJson);
                var costCenters = JsonSerializer.Deserialize<JsonElement>(costCentersJson);

                return Json(new
                {
                    contracts,
                    costCenters
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching master data from API");
                return StatusCode(500, new { message = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ Master Data ได้" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDetailJson(int id, CancellationToken cancellationToken)
        {
            return await ForwardGetAsync($"/api/rental-requests/{id}", cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> SaveDraft([FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            return await ForwardPostAsync("/api/rental-requests", payload, cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> Submit(int id, [FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            return await ForwardPostAsync($"/api/rental-requests/{id}/submit", payload, cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> Cancel(int id, [FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            return await ForwardPostAsync($"/api/rental-requests/{id}/cancel", payload, cancellationToken);
        }

        // ==========================================
        // 3. Helper Methods
        // ==========================================

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
                return StatusCode(500, new { message = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ได้" });
            }
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
                return StatusCode(500, new { message = "เกิดข้อผิดพลาดในการส่งข้อมูลไปยัง API" });
            }
        }
    }
}
