using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace JiraProxyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JiraProxyController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JiraProxyController> _logger;

    public JiraProxyController(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<JiraProxyController> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // Helper method: Önce IConfiguration'dan, sonra Environment variable'dan oku
    private string? GetConfigValue(string key)
    {
        // Önce IConfiguration'dan dene (Railway environment variables otomatik olarak buraya gelir)
        var configValue = _configuration[key];
        if (!string.IsNullOrWhiteSpace(configValue))
        {
            return configValue;
        }

        // IConfiguration'da yoksa Environment variable'dan oku
        return Environment.GetEnvironmentVariable(key);
    }

    [HttpGet("issue")]
    public async Task<IActionResult> GetIssue([FromQuery] string jiraKey)
    {
        if (string.IsNullOrWhiteSpace(jiraKey))
        {
            return BadRequest(new { error = "Jira key gerekli" });
        }

        try
        {
            // Önce IConfiguration'dan, sonra Environment variable'dan oku (garanti için her ikisini de dene)
            var jiraBaseUrl = GetConfigValue("JIRA_BASE_URL");
            if (string.IsNullOrWhiteSpace(jiraBaseUrl))
            {
                return BadRequest(new { 
                    error = "JIRA_BASE_URL environment variable ayarlanmamış.",
                    hint = "Lütfen Railway'de JIRA_BASE_URL environment variable'ını ayarlayın."
                });
            }
            
            var jiraEmail = GetConfigValue("JIRA_EMAIL");
            if (string.IsNullOrWhiteSpace(jiraEmail))
            {
                return BadRequest(new { 
                    error = "JIRA_EMAIL environment variable ayarlanmamış.",
                    hint = "Lütfen Railway'de JIRA_EMAIL environment variable'ını ayarlayın."
                });
            }
            
            var jiraApiToken = GetConfigValue("JIRA_API_TOKEN");
            if (string.IsNullOrWhiteSpace(jiraApiToken))
            {
                return BadRequest(new { 
                    error = "JIRA_API_TOKEN environment variable ayarlanmamış.",
                    hint = "Lütfen Railway'de JIRA_API_TOKEN environment variable'ını ayarlayın."
                });
            }

            // Debug için log (production'da kaldırın)
            _logger.LogInformation("Jira API çağrısı: BaseUrl={BaseUrl}, Email={Email}, Key={Key}, TokenLength={TokenLength}", 
                jiraBaseUrl, jiraEmail, jiraKey, jiraApiToken?.Length ?? 0);

            var jiraApiUrl = $"{jiraBaseUrl}/rest/api/3/issue/{jiraKey}";
            
            // Jira Cloud için Basic Auth: email:token formatı
            // Bazı durumlarda sadece token kullanılabilir, önce email:token deneyelim
            var authString = $"{jiraEmail}:{jiraApiToken}";
            var authBytes = Encoding.UTF8.GetBytes(authString);
            var authBase64 = Convert.ToBase64String(authBytes);
            
            // Debug: Authentication bilgilerini logla (production'da kaldırın)
            _logger.LogInformation("Auth - Email: {Email}, TokenLength: {TokenLength}, Base64Length: {Base64Length}", 
                jiraEmail, jiraApiToken?.Length ?? 0, authBase64.Length);

            // HttpClient ile Jira API'ye istek yap
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64);
            httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            // User-Agent ekle (bazı API'ler bunu ister)
            httpClient.DefaultRequestHeaders.Add("User-Agent", "JiraProxyApi/1.0");

            _logger.LogInformation("Jira API isteği gönderiliyor: {Url}", jiraApiUrl);
            var response = await httpClient.GetAsync(jiraApiUrl);
            
            // Response headers'ı logla
            _logger.LogInformation("Jira API yanıtı: StatusCode={StatusCode}, ContentType={ContentType}", 
                response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? "null");
            
            // Eğer 401 Unauthorized alırsak, farklı authentication yöntemlerini dene
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("401 Unauthorized alındı, alternatif authentication yöntemlerini deniyoruz...");
                
                // Yöntem 1: Sadece token ile deneme
                var tokenOnlyAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{jiraApiToken}"));
                httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", tokenOnlyAuth);
                
                response = await httpClient.GetAsync(jiraApiUrl);
                _logger.LogInformation("Token-only auth denemesi: StatusCode={StatusCode}", response.StatusCode);
                
                // Yöntem 2: Bearer token (hala 401 ise)
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    httpClient.DefaultRequestHeaders.Authorization = null;
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {jiraApiToken}");
                    response = await httpClient.GetAsync(jiraApiUrl);
                    _logger.LogInformation("Bearer token auth denemesi: StatusCode={StatusCode}", response.StatusCode);
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Jira API hatası: {StatusCode} - {Error} - URL: {Url}", 
                    response.StatusCode, errorContent, jiraApiUrl);
                
                // Daha açıklayıcı hata mesajları
                var errorMessage = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.NotFound => "Jira issue bulunamadı veya erişim yetkiniz yok. Lütfen Jira key'in doğru olduğundan ve bu issue'a erişim yetkiniz olduğundan emin olun.",
                    System.Net.HttpStatusCode.Unauthorized => "Jira API kimlik doğrulama hatası. Lütfen email ve API token'ın doğru olduğundan emin olun.",
                    System.Net.HttpStatusCode.Forbidden => "Jira API erişim yetkisi yok. API token'ınızın bu issue'a erişim yetkisi olduğundan emin olun.",
                    _ => $"Jira API hatası: {errorContent}"
                };
                
                return StatusCode((int)response.StatusCode, new { 
                    error = errorMessage,
                    details = errorContent,
                    jiraKey = jiraKey,
                    jiraUrl = jiraApiUrl
                });
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var jiraData = System.Text.Json.JsonSerializer.Deserialize<object>(jsonContent);

            return Ok(jiraData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jira proxy hatası: {Message}", ex.Message);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok", timestamp = DateTime.UtcNow });
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        // Güvenlik: Production'da bu endpoint'i kaldırın veya sadece email gösterin
        var jiraBaseUrl = GetConfigValue("JIRA_BASE_URL");
        var jiraEmail = GetConfigValue("JIRA_EMAIL");
        var jiraApiToken = GetConfigValue("JIRA_API_TOKEN");

        var missingVars = new List<string>();
        if (string.IsNullOrWhiteSpace(jiraBaseUrl)) missingVars.Add("JIRA_BASE_URL");
        if (string.IsNullOrWhiteSpace(jiraEmail)) missingVars.Add("JIRA_EMAIL");
        if (string.IsNullOrWhiteSpace(jiraApiToken)) missingVars.Add("JIRA_API_TOKEN");

        // Hangi yöntemle okunduğunu kontrol et
        var sourceInfo = new List<string>();
        if (!string.IsNullOrWhiteSpace(_configuration["JIRA_BASE_URL"])) sourceInfo.Add("IConfiguration");
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JIRA_BASE_URL"))) sourceInfo.Add("Environment.GetEnvironmentVariable");

        if (missingVars.Any())
        {
            return StatusCode(500, new { 
                error = "Eksik environment variables",
                missingVariables = missingVars,
                hint = "Lütfen Railway'de tüm gerekli environment variables'ları ayarlayın."
            });
        }

        return Ok(new { 
            jiraBaseUrl = jiraBaseUrl,
            jiraEmail = jiraEmail,
            hasApiToken = true,
            tokenLength = jiraApiToken.Length,
            source = string.Join(" + ", sourceInfo.Distinct()),
            message = "Tüm environment variables ayarlı (her iki yöntemle de okunuyor)"
        });
    }

    [HttpGet("test-auth")]
    public async Task<IActionResult> TestAuth()
    {
        try
        {
            // Önce IConfiguration'dan, sonra Environment variable'dan oku (garanti için her ikisini de dene)
            var jiraBaseUrl = GetConfigValue("JIRA_BASE_URL");
            if (string.IsNullOrWhiteSpace(jiraBaseUrl))
            {
                return StatusCode(500, new { 
                    success = false,
                    error = "JIRA_BASE_URL environment variable ayarlanmamış",
                    hint = "Railway'de JIRA_BASE_URL environment variable'ını ayarlayın"
                });
            }
            
            var jiraEmail = GetConfigValue("JIRA_EMAIL");
            if (string.IsNullOrWhiteSpace(jiraEmail))
            {
                return StatusCode(500, new { 
                    success = false,
                    error = "JIRA_EMAIL environment variable ayarlanmamış",
                    hint = "Railway'de JIRA_EMAIL environment variable'ını ayarlayın"
                });
            }
            
            var jiraApiToken = GetConfigValue("JIRA_API_TOKEN");
            if (string.IsNullOrWhiteSpace(jiraApiToken))
            {
                return StatusCode(500, new { 
                    success = false,
                    error = "JIRA_API_TOKEN environment variable ayarlanmamış",
                    hint = "Railway'de JIRA_API_TOKEN environment variable'ını ayarlayın"
                });
            }

            var userUrl = $"{jiraBaseUrl}/rest/api/3/myself";

            // Test 1: email:token formatı (Jira Cloud standard)
            var httpClient1 = _httpClientFactory.CreateClient();
            httpClient1.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            var authString1 = $"{jiraEmail}:{jiraApiToken}";
            var authBase64_1 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString1));
            httpClient1.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64_1);

            var userResponse = await httpClient1.GetAsync(userUrl);

            if (userResponse.IsSuccessStatusCode)
            {
                var userContent = await userResponse.Content.ReadAsStringAsync();
                var userData = System.Text.Json.JsonSerializer.Deserialize<object>(userContent);
                
                return Ok(new { 
                    success = true,
                    message = "API token çalışıyor (email:token formatı)",
                    authenticatedUser = userData,
                    email = jiraEmail,
                    authMethod = "email:token"
                });
            }

            // Test 2: Sadece token (bazı Jira Cloud kurulumlarında)
            var httpClient2 = _httpClientFactory.CreateClient();
            httpClient2.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            var authString2 = $":{jiraApiToken}";
            var authBase64_2 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString2));
            httpClient2.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64_2);

            userResponse = await httpClient2.GetAsync(userUrl);

            if (userResponse.IsSuccessStatusCode)
            {
                var userContent = await userResponse.Content.ReadAsStringAsync();
                var userData = System.Text.Json.JsonSerializer.Deserialize<object>(userContent);
                
                return Ok(new { 
                    success = true,
                    message = "API token çalışıyor (sadece token formatı)",
                    authenticatedUser = userData,
                    email = jiraEmail,
                    authMethod = "token-only"
                });
            }

            // Test 3: Bearer token (Jira Cloud API token için)
            var httpClient3 = _httpClientFactory.CreateClient();
            httpClient3.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            httpClient3.DefaultRequestHeaders.Add("Authorization", $"Bearer {jiraApiToken}");
            
            userResponse = await httpClient3.GetAsync(userUrl);

            if (userResponse.IsSuccessStatusCode)
            {
                var userContent = await userResponse.Content.ReadAsStringAsync();
                var userData = System.Text.Json.JsonSerializer.Deserialize<object>(userContent);
                
                return Ok(new { 
                    success = true,
                    message = "API token çalışıyor (Bearer token formatı)",
                    authenticatedUser = userData,
                    email = jiraEmail,
                    authMethod = "bearer"
                });
            }

            // Tüm yöntemler başarısız
            var errorContent = await userResponse.Content.ReadAsStringAsync();
            return StatusCode((int)userResponse.StatusCode, new { 
                success = false,
                message = "Tüm authentication yöntemleri başarısız",
                statusCode = userResponse.StatusCode,
                error = errorContent,
                testedMethods = new[] { "email:token", "token-only", "bearer" },
                email = jiraEmail,
                tokenLength = jiraApiToken?.Length ?? 0,
                hint = "Lütfen email ve API token'ın doğru olduğundan emin olun. Jira'da yeni bir API token oluşturmayı deneyin."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }
}

