using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BCG_RPAS.Controllers
{
    [Authorize]
    public class UsersController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            IHttpClientFactory httpClientFactory,
            ILogger<UsersController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        private bool IsAdminRole()
        {
            var roleCode = User.FindFirst("RoleCode")?.Value;
            return string.Equals(roleCode, "ADMINISTRATOR", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (!IsAdminRole())
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        {
            if (!IsAdminRole())
            {
                return StatusCode(403, new { detail = "เฉพาะผู้ดูแลระบบเท่านั้นที่มีสิทธิ์จัดการผู้ใช้งาน" });
            }

            try
            {
                var client = CreateApiClient();
                var response = await client.GetAsync("/api/users", cancellationToken);
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
                _logger.LogError(ex, "Error fetching users");
                return StatusCode(500, new { detail = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ API ได้" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            if (!IsAdminRole())
            {
                return StatusCode(403, new { detail = "เฉพาะผู้ดูแลระบบเท่านั้นที่มีสิทธิ์จัดการผู้ใช้งาน" });
            }

            try
            {
                var client = CreateApiClient();
                var jsonString = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("/api/users", httpContent, cancellationToken);
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
                _logger.LogError(ex, "Error saving user");
                return StatusCode(500, new { detail = "เกิดข้อผิดพลาดในการบันทึกข้อมูลผู้ใช้งาน" });
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
    }
}
