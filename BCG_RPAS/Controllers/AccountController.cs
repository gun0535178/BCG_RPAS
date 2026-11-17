using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BCG_RPAS.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BCG_RPAS.Controllers
{
    public class AccountController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AccountController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginAjax([FromForm] LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

                return Json(new AjaxResponse
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(errors) ? "ข้อมูลที่กรอกไม่ถูกต้อง" : errors
                });
            }

            try
            {
                var apiBaseUrl = _configuration["ApiSettings:BaseUrl"] ?? "https://localhost:7050";
                var client = _httpClientFactory.CreateClient("RPAS_API");

                var loginPayload = new
                {
                    userName = model.UserName,
                    password = model.Password
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(loginPayload),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await client.PostAsync($"{apiBaseUrl}/api/auth/login", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var authData = JsonSerializer.Deserialize<ApiLoginResponse>(responseJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (authData?.User != null)
                    {
                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.NameIdentifier, authData.User.Id.ToString()),
                            new Claim(ClaimTypes.Name, authData.User.UserName),
                            new Claim("DisplayName", authData.User.DisplayName ?? authData.User.UserName),
                            new Claim(ClaimTypes.Role, authData.User.RoleName ?? "User"),
                            new Claim("RoleCode", authData.User.RoleCode ?? ""),
                            new Claim("AccessToken", authData.AccessToken ?? "")
                        };

                        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                        var authProperties = new AuthenticationProperties
                        {
                            IsPersistent = model.RememberMe,
                            ExpiresUtc = model.RememberMe 
                                ? DateTimeOffset.UtcNow.AddDays(7) 
                                : DateTimeOffset.UtcNow.AddHours(8)
                        };

                        await HttpContext.SignInAsync(
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            new ClaimsPrincipal(claimsIdentity),
                            authProperties
                        );

                        _logger.LogInformation("User {UserName} logged in successfully.", model.UserName);

                        var targetRedirect = (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                            ? model.ReturnUrl
                            : Url.Action("Index", "Home") ?? "/";

                        return Json(new AjaxResponse
                        {
                            Success = true,
                            Message = "เข้าสู่ระบบสำเร็จ กำลังนำท่านเข้าสู่ระบบ...",
                            RedirectUrl = targetRedirect
                        });
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return Json(new AjaxResponse
                    {
                        Success = false,
                        Message = "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง"
                    });
                }

                return Json(new AjaxResponse
                {
                    Success = false,
                    Message = "เข้าสู่ระบบไม่สำเร็จ กรุณาตรวจสอบข้อมูลอีกครั้ง"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while communicating with Auth API");
                return Json(new AjaxResponse
                {
                    Success = false,
                    Message = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ระบบยืนยันตัวตนได้ในขณะนี้"
                });
            }
        }

        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
