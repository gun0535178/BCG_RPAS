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
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<RentalRequestsController> _logger;

        private const long MaxPdfSizeBytes = 15 * 1024 * 1024; // 15 MB per file

        public RentalRequestsController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IWebHostEnvironment env,
            ILogger<RentalRequestsController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _env = env;
            _logger = logger;
        }

        // ==========================================
        // 1. Razor View Pages
        // ==========================================

        private bool IsRequesterRole()
        {
            var roleCode = User.FindFirst("RoleCode")?.Value;
            return string.Equals(roleCode, "REQUESTER", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Create()
        {
            if (!IsRequesterRole())
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            return View();
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            if (!IsRequesterRole())
            {
                return RedirectToAction("AccessDenied", "Account");
            }
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
                    return StatusCode(500, new { message = "ไม่สามารถโหลดข้อมูลหลักได้" });
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
                return StatusCode(500, new { message = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ข้อมูลหลักได้" });
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
            if (!IsRequesterRole())
            {
                return StatusCode(403, new { detail = "เฉพาะผู้ขออนุมัติ (REQUESTER) เท่านั้นที่สามารถสร้างหรือแก้ไขใบคำขอค่าเช่าได้" });
            }

            try
            {
                var client = CreateApiClient();
                var jsonString = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("/api/rental-requests", httpContent, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(content);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var reqId) && reqId > 0)
                        {
                            string? contractNumber = null;
                            if (root.TryGetProperty("contractNumber", out var cnEl) && cnEl.ValueKind == JsonValueKind.String)
                            {
                                contractNumber = cnEl.GetString();
                            }

                            RelocateRequestAttachmentFolder(reqId, contractNumber);
                        }
                    }
                    catch (Exception moveEx)
                    {
                        _logger.LogWarning(moveEx, "Error relocating attachment folder after SaveDraft");
                    }
                }

                return new ContentResult
                {
                    Content = content,
                    ContentType = "application/json; charset=utf-8",
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during POST /api/rental-requests");
                return StatusCode(500, new { detail = "เกิดข้อผิดพลาดในการส่งข้อมูลไปยัง API" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Submit(int id, [FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            if (!IsRequesterRole())
            {
                return StatusCode(403, new { detail = "เฉพาะผู้ขออนุมัติ (REQUESTER) เท่านั้นที่สามารถส่งใบคำขออนุมัติได้" });
            }
            return await ForwardPostAsync($"/api/rental-requests/{id}/submit", payload, cancellationToken);
        }

        [HttpPost]
        public async Task<IActionResult> Cancel(int id, [FromBody] JsonElement payload, CancellationToken cancellationToken)
        {
            if (!IsRequesterRole())
            {
                return StatusCode(403, new { detail = "เฉพาะผู้ขออนุมัติ (REQUESTER) เท่านั้นที่สามารถยกเลิกใบคำขอได้" });
            }
            return await ForwardPostAsync($"/api/rental-requests/{id}/cancel", payload, cancellationToken);
        }

        // ==========================================
        // 3. PDF Attachment Upload, Relocation & View
        // ==========================================

        [HttpPost]
        public async Task<IActionResult> UploadAttachments(
            [FromForm] int requestId,
            [FromForm] string? contractNumber,
            [FromForm] List<IFormFile> files,
            CancellationToken cancellationToken)
        {
            if (!IsRequesterRole())
            {
                return StatusCode(403, new { detail = "เฉพาะผู้ขออนุมัติ (REQUESTER) เท่านั้นที่สามารถอัปโหลดเอกสารแนบได้" });
            }

            if (requestId <= 0)
            {
                return BadRequest(new { detail = "ไม่พบเลขที่ใบคำขอสำหรับจัดเก็บไฟล์" });
            }

            if (files == null || files.Count == 0)
            {
                return Ok(new { fileNames = Array.Empty<string>() });
            }

            // 1. Validate all files are valid PDFs before saving
            foreach (var file in files)
            {
                if (file.Length <= 0)
                {
                    return BadRequest(new { detail = $"ไฟล์ '{file.FileName}' ไม่มีข้อมูล" });
                }

                if (file.Length > MaxPdfSizeBytes)
                {
                    return BadRequest(new { detail = $"ไฟล์ '{file.FileName}' มีขนาดเกิน 15 MB" });
                }

                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { detail = $"อนุญาตให้อัปโหลดเฉพาะไฟล์ .pdf เท่านั้น ('{file.FileName}')" });
                }

                // Check PDF magic bytes (%PDF-)
                byte[] headerBytes = new byte[5];
                await using (var checkStream = file.OpenReadStream())
                {
                    var readCount = await checkStream.ReadAsync(headerBytes.AsMemory(0, 5), cancellationToken);
                    if (readCount < 5 || Encoding.ASCII.GetString(headerBytes) != "%PDF-")
                    {
                        return BadRequest(new { detail = $"ไฟล์ '{file.FileName}' ไม่ใช่ไฟล์ PDF ที่ถูกต้อง" });
                    }
                }
            }

            // 2. Relocate any existing folder if contractNumber changed
            var targetDir = RelocateRequestAttachmentFolder(requestId, contractNumber);
            Directory.CreateDirectory(targetDir);

            // 3. Save each PDF with format YYYYMMDD_HHmmssfff.pdf
            var savedFileNames = new List<string>();
            var usedTimestamps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var now = DateTime.Now;
                var baseStamp = now.ToString("yyyyMMdd_HHmmssfff");
                var generatedName = $"{baseStamp}.pdf";
                var fullPath = Path.Combine(targetDir, generatedName);

                // Ensure uniqueness if multiple files uploaded in the same millisecond
                while (usedTimestamps.Contains(generatedName) || System.IO.File.Exists(fullPath))
                {
                    await Task.Delay(2, cancellationToken);
                    now = DateTime.Now;
                    baseStamp = now.ToString("yyyyMMdd_HHmmssfff");
                    generatedName = $"{baseStamp}.pdf";
                    fullPath = Path.Combine(targetDir, generatedName);
                }

                usedTimestamps.Add(generatedName);

                await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await file.CopyToAsync(fs, cancellationToken);
                }

                savedFileNames.Add(generatedName);
            }

            return Ok(new
            {
                requestId,
                contractFolder = SanitizeFolderName(contractNumber),
                fileNames = savedFileNames
            });
        }

        [HttpGet]
        public IActionResult ViewAttachment(int requestId, string fileName, string? contractNumber = null)
        {
            if (requestId <= 0 || string.IsNullOrWhiteSpace(fileName))
            {
                return NotFound("ไม่พบข้อมูลไฟล์เอกสารแนบ");
            }

            // Prevent path traversal
            var safeFileName = Path.GetFileName(fileName);
            if (!string.Equals(Path.GetExtension(safeFileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("อนุญาตให้เปิดดูเฉพาะไฟล์ PDF เท่านั้น");
            }

            var attachmentRoot = GetAttachmentRootPath();
            if (!Directory.Exists(attachmentRoot))
            {
                return NotFound("ไม่พบโฟลเดอร์จัดเก็บเอกสารแนบ");
            }

            // 1. Try direct path if contractNumber provided
            if (!string.IsNullOrWhiteSpace(contractNumber))
            {
                var directFolder = Path.Combine(attachmentRoot, SanitizeFolderName(contractNumber), requestId.ToString());
                var directFile = Path.Combine(directFolder, safeFileName);
                if (System.IO.File.Exists(directFile))
                {
                    return PhysicalFile(directFile, "application/pdf");
                }
            }

            // 2. Try Temp/<requestId>/<safeFileName>
            var tempFile = Path.Combine(attachmentRoot, "Temp", requestId.ToString(), safeFileName);
            if (System.IO.File.Exists(tempFile))
            {
                return PhysicalFile(tempFile, "application/pdf");
            }

            // 3. Search across any contract subfolder Attachment/*/<requestId>/<safeFileName>
            foreach (var subDir in Directory.GetDirectories(attachmentRoot))
            {
                var candidate = Path.Combine(subDir, requestId.ToString(), safeFileName);
                if (System.IO.File.Exists(candidate))
                {
                    return PhysicalFile(candidate, "application/pdf");
                }
            }

            return NotFound($"ไม่พบไฟล์เอกสาร '{safeFileName}' บนเซิร์ฟเวอร์");
        }

        // ==========================================
        // 4. Helper Methods
        // ==========================================

        private string GetAttachmentRootPath()
        {
            return Path.Combine(_env.ContentRootPath, "Attachment");
        }

        private static string SanitizeFolderName(string? contractNumber)
        {
            if (string.IsNullOrWhiteSpace(contractNumber))
            {
                return "Temp";
            }

            var cleaned = contractNumber.Trim();
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                cleaned = cleaned.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(cleaned) ? "Temp" : cleaned;
        }

        /// <summary>
        /// ย้ายไฟล์จาก Attachment/Temp/{requestId} หรือโฟลเดอร์สัญญาเดิม ไปยัง Attachment/{contractFolder}/{requestId} อัตโนมัติ
        /// </summary>
        private string RelocateRequestAttachmentFolder(int requestId, string? contractNumber)
        {
            var root = GetAttachmentRootPath();
            Directory.CreateDirectory(root);

            var targetContractFolder = SanitizeFolderName(contractNumber);
            var targetRequestDir = Path.Combine(root, targetContractFolder, requestId.ToString());

            var reqFolderName = requestId.ToString();

            // ตรวจสอบทุกโฟลเดอร์ย่อยใน Attachment/*/{requestId} ว่ามีไฟล์อยู่ในโฟลเดอร์อื่นหรือไม่
            foreach (var contractDir in Directory.GetDirectories(root))
            {
                var candidateReqDir = Path.Combine(contractDir, reqFolderName);
                if (!Directory.Exists(candidateReqDir))
                {
                    continue;
                }

                if (string.Equals(
                    Path.GetFullPath(candidateReqDir).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(targetRequestDir).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // ย้ายไฟล์ทั้งหมดจากโฟลเดอร์เดิม (เช่น Temp/{id}) ไปยังโฟลเดอร์เป้าหมาย (เช่น RENT-2026-001/{id})
                Directory.CreateDirectory(targetRequestDir);
                foreach (var file in Directory.GetFiles(candidateReqDir))
                {
                    var destFile = Path.Combine(targetRequestDir, Path.GetFileName(file));
                    System.IO.File.Move(file, destFile, overwrite: true);
                }

                // ลบโฟลเดอร์เก่าที่ว่างเปล่าออก
                if (Directory.GetFiles(candidateReqDir).Length == 0 &&
                    Directory.GetDirectories(candidateReqDir).Length == 0)
                {
                    Directory.Delete(candidateReqDir, recursive: false);
                }
            }

            return targetRequestDir;
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
