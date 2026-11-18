using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BCG_RPAS.Controllers
{
    [Authorize]
    public class ApprovalAssignmentsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ApprovalAssignmentsController> _logger;

        public ApprovalAssignmentsController(
            IHttpClientFactory httpClientFactory,
            ILogger<ApprovalAssignmentsController> logger)
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
                return StatusCode(403, new { detail = "เฉพาะผู้ดูแลระบบ (Administrator) เท่านั้นที่มีสิทธิ์จัดการสายอนุมัติ" });
            }

            try
            {
                var client = CreateApiClient();
                var response = await client.GetAsync("/api/approval-assignments", cancellationToken);
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
                _logger.LogError(ex, "Error fetching approval assignments");
                return StatusCode(500, new { detail = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ API ได้" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            if (!IsAdminRole())
            {
                return StatusCode(403, new { detail = "เฉพาะผู้ดูแลระบบ (Administrator) เท่านั้นที่มีสิทธิ์จัดการสายอนุมัติ" });
            }

            try
            {
                var client = CreateApiClient();
                var jsonString = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("/api/approval-assignments", httpContent, cancellationToken);
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
                _logger.LogError(ex, "Error saving approval assignment");
                return StatusCode(500, new { detail = "เกิดข้อผิดพลาดในการบันทึกสายอนุมัติ" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            if (!IsAdminRole())
            {
                return StatusCode(403, new { detail = "เฉพาะผู้ดูแลระบบ (Administrator) เท่านั้นที่มีสิทธิ์จัดการสายอนุมัติ" });
            }

            try
            {
                var client = CreateApiClient();
                var response = await client.PostAsync($"/api/approval-assignments/{id}/delete", new StringContent("{}", Encoding.UTF8, "application/json"), cancellationToken);
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
                _logger.LogError(ex, "Error deleting approval assignment");
                return StatusCode(500, new { detail = "เกิดข้อผิดพลาดในการลบสายอนุมัติ" });
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
