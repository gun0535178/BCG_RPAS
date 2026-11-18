using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BCG_RPAS.Controllers
{
    [Authorize]
    public class MasterDataController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MasterDataController> _logger;

        public MasterDataController(
            IHttpClientFactory httpClientFactory,
            ILogger<MasterDataController> logger)
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
                return StatusCode(403, new { detail = "พนักงานผู้ขออนุมัติ (Requester) ไม่มีสิทธิ์จัดการข้อมูลหลัก" });
            }

            try
            {
                var client = CreateApiClient();

                var contractsTask = client.GetAsync("/api/master/contract", cancellationToken);
                var vendorsTask = client.GetAsync("/api/master/vendor", cancellationToken);
                var costCentersTask = client.GetAsync("/api/master/cost-center", cancellationToken);
                var rentalTypesTask = client.GetAsync("/api/master/rental-type", cancellationToken);

                await Task.WhenAll(contractsTask, vendorsTask, costCentersTask, rentalTypesTask);

                var responses = new[]
                {
                    await contractsTask,
                    await vendorsTask,
                    await costCentersTask,
                    await rentalTypesTask
                };

                if (responses.Any(r => r.StatusCode == System.Net.HttpStatusCode.Unauthorized))
                {
                    return Unauthorized(new { detail = "เซสชันหมดอายุ หรือไม่มีสิทธิ์เข้าถึง API" });
                }

                if (responses.Any(r => !r.IsSuccessStatusCode))
                {
                    return StatusCode(500, new { detail = "ไม่สามารถโหลดข้อมูล Master Data บางส่วนได้" });
                }

                var contracts = JsonSerializer.Deserialize<JsonElement>(await responses[0].Content.ReadAsStringAsync(cancellationToken));
                var vendors = JsonSerializer.Deserialize<JsonElement>(await responses[1].Content.ReadAsStringAsync(cancellationToken));
                var costCenters = JsonSerializer.Deserialize<JsonElement>(await responses[2].Content.ReadAsStringAsync(cancellationToken));
                var rentalTypes = JsonSerializer.Deserialize<JsonElement>(await responses[3].Content.ReadAsStringAsync(cancellationToken));

                return Json(new
                {
                    contracts,
                    vendors,
                    costCenters,
                    rentalTypes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all master data");
                return StatusCode(500, new { detail = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ Master Data API ได้" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveContract([FromBody] JsonElement payload, CancellationToken cancellationToken)
            => await ForwardPostAsync("/api/master/contract", payload, cancellationToken);

        [HttpPost]
        public async Task<IActionResult> SaveVendor([FromBody] JsonElement payload, CancellationToken cancellationToken)
            => await ForwardPostAsync("/api/master/vendor", payload, cancellationToken);

        [HttpPost]
        public async Task<IActionResult> SaveCostCenter([FromBody] JsonElement payload, CancellationToken cancellationToken)
            => await ForwardPostAsync("/api/master/cost-center", payload, cancellationToken);

        [HttpPost]
        public async Task<IActionResult> SaveRentalType([FromBody] JsonElement payload, CancellationToken cancellationToken)
            => await ForwardPostAsync("/api/master/rental-type", payload, cancellationToken);

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
            if (IsRequesterRole())
            {
                return StatusCode(403, new { detail = "พนักงานผู้ขออนุมัติ (Requester) ไม่มีสิทธิ์จัดการข้อมูลหลัก" });
            }

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
                return StatusCode(500, new { detail = "เกิดข้อผิดพลาดในการส่งข้อมูลไปยัง Master API" });
            }
        }
    }
}
