# Jira Proxy API

Azure DevOps Extension için Jira API proxy servisi (.NET 8 Web API)

## Özellikler

- ✅ CORS desteği (Extension'dan çağrı yapabilir)
- ✅ Jira API entegrasyonu
- ✅ Environment variables desteği
- ✅ Swagger/OpenAPI desteği
- ✅ Error handling ve logging

## Hızlı Başlangıç

### 1. Projeyi Çalıştırma

```bash
cd JiraProxyApi
dotnet restore
dotnet build
dotnet run
```

### 2. Test

Tarayıcıda açın:
```
https://localhost:5001/api/jiraproxy/issue?jiraKey=DIGI-25832
```

Swagger UI:
```
https://localhost:5001/swagger
```

### 3. Configuration

`appsettings.json` dosyasını düzenleyin veya environment variables kullanın:

```bash
# Windows
set JIRA_API_TOKEN=your_token_here

# Linux/Mac
export JIRA_API_TOKEN=your_token_here
```

## API Endpoints

### GET /api/jiraproxy/issue?jiraKey={key}

Jira issue bilgilerini getirir.

**Query Parameters:**
- `jiraKey` (required): Jira issue key (örn: DIGI-25832)

**Response:**
```json
{
  "key": "DIGI-25832",
  "fields": {
    "summary": "Task başlığı",
    "status": { "name": "In Progress" },
    ...
  }
}
```

### GET /api/jiraproxy/health

API sağlık kontrolü.

**Response:**
```json
{
  "status": "ok",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## Deploy

Detaylı deploy rehberi için `DOTNET_API_SETUP.md` dosyasına bakın.

### Ücretsiz Hosting Seçenekleri:

1. **Azure App Service (F1 Free Tier)** - Önerilen
2. **Railway** - $5 ücretsiz kredi
3. **Render** - Ücretsiz tier
4. **Fly.io** - Ücretsiz tier

## Güvenlik

- Production'da API token'ı environment variable olarak saklayın
- `appsettings.json`'ı Git'e commit etmeyin
- HTTPS kullanın


