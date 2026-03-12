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
            
           var jiraApiToken = _configuration["Jira:ApiToken"] 
    ?? Environment.GetEnvironmentVariable("JIRA_API_TOKEN")
    ?? throw new InvalidOperationException($"JIRA_API_TOKEN bulunamadı. Env vars: {string.Join(", ", Environment.GetEnvironmentVariableNames().Where(n => n.Contains("JIRA")))}");

            // API token kontrolü
            if (jiraApiToken == "YOUR_JIRA_API_TOKEN_HERE" || string.IsNullOrWhiteSpace(jiraApiToken))
            {
                return BadRequest(new { 
                    error = "Jira API token ayarlanmamış. Lütfen appsettings.json veya environment variable olarak JIRA_API_TOKEN ayarlayın.",
                    hint = "appsettings.json dosyasındaki 'Jira:ApiToken' değerini güncelleyin."
                });
            }

            // Debug için log (production'da kaldırın)
            _logger.LogInformation("Jira API çağrısı: BaseUrl={BaseUrl}, Email={Email}, Key={Key}", 
                jiraBaseUrl, jiraEmail, jiraKey);

            var jiraApiUrl = $"{jiraBaseUrl}/rest/api/3/issue/{jiraKey}";
            
            // Basic Auth için base64 encode
            var authBytes = Encoding.UTF8.GetBytes($"{jiraEmail}:{jiraApiToken}");
            var authBase64 = Convert.ToBase64String(authBytes);

            // HttpClient ile Jira API'ye istek yap
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64);
            httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await httpClient.GetAsync(jiraApiUrl);

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

