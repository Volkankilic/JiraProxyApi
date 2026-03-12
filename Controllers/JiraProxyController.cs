using Microsoft.AspNetCore.Mvc;
using System.Text;

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

    [HttpGet("issue")]
    public async Task<IActionResult> GetIssue([FromQuery] string jiraKey)
    {
        if (string.IsNullOrWhiteSpace(jiraKey))
        {
            return BadRequest(new { error = "Jira key gerekli" });
        }

        try
        {
            // Environment variables veya appsettings.json'dan al
            var jiraBaseUrl = _configuration["Jira:BaseUrl"] 
                ?? Environment.GetEnvironmentVariable("JIRA_BASE_URL") 
                ?? "https://atptech.atlassian.net";
            
            var jiraEmail = _configuration["Jira:Email"] 
                ?? Environment.GetEnvironmentVariable("JIRA_EMAIL") 
                ?? "volkan.kilic@atptech.com";
            
            // TEST İÇİN: Token koda gömülü (PRODUCTION'DA KALDIRIN!)
            var jiraApiToken = _configuration["Jira:ApiToken"] 
                ?? Environment.GetEnvironmentVariable("JIRA_API_TOKEN") 
                ?? "ATATT3xFfGF0WKePxb7e6rfXt2U8U7lHCAkCctjga-iWt2JLkVF99HDlcjE2Kyu21j5SF2j87p5cQG5_m9bVC6HwXEm1StYKM_-2TxWkNsMDwNu97aNQjFzrxskhr8plzc__vTIWKcWfCWx1xh-TBiNNv_28ITK199Lovb38_lB3q5AX9OTphMQ=1A126BDC";

            // API token kontrolü
            if (jiraApiToken == "YOUR_JIRA_API_TOKEN_HERE" || string.IsNullOrWhiteSpace(jiraApiToken))
            {
                return BadRequest(new { 
                    error = "Jira API token ayarlanmamış. Lütfen appsettings.json veya environment variable olarak JIRA_API_TOKEN ayarlayın.",
                    hint = "appsettings.json dosyasındaki 'Jira:ApiToken' değerini güncelleyin."
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
            
            // Eğer 401 Unauthorized alırsak, sadece token ile deneyelim
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("401 Unauthorized alındı, sadece token ile tekrar deniyoruz...");
                
                // Sadece token ile deneme (bazı Jira Cloud kurulumlarında gerekebilir)
                var tokenOnlyAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{jiraApiToken}"));
                httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", tokenOnlyAuth);
                
                response = await httpClient.GetAsync(jiraApiUrl);
                _logger.LogInformation("Token-only auth denemesi: StatusCode={StatusCode}", response.StatusCode);
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
        var jiraBaseUrl = _configuration["Jira:BaseUrl"] 
            ?? Environment.GetEnvironmentVariable("JIRA_BASE_URL") 
            ?? "https://atptech.atlassian.net";
        
        var jiraEmail = _configuration["Jira:Email"] 
            ?? Environment.GetEnvironmentVariable("JIRA_EMAIL") 
            ?? "volkan.kilic@atptech.com";
        
        var jiraApiToken = _configuration["Jira:ApiToken"] 
            ?? Environment.GetEnvironmentVariable("JIRA_API_TOKEN") 
            ?? "NOT_SET";

        var hasToken = !string.IsNullOrWhiteSpace(jiraApiToken) 
            && jiraApiToken != "YOUR_JIRA_API_TOKEN_HERE" 
            && jiraApiToken != "NOT_SET";

        return Ok(new { 
            jiraBaseUrl = jiraBaseUrl,
            jiraEmail = jiraEmail,
            hasApiToken = hasToken,
            tokenLength = hasToken ? jiraApiToken.Length : 0,
            message = hasToken ? "API token ayarlı" : "API token ayarlanmamış - appsettings.json'ı kontrol edin"
        });
    }
}

