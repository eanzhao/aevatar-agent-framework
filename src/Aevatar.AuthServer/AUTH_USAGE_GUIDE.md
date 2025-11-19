# Aevatar AuthServer 使用指南

## 🎯 快速开始

### 1. 启动服务

```bash
# 启动 AuthServer
cd src/Aevatar.AuthServer/src/Aevatar.AuthServer.Web
dotnet run

# 启动 BusinessServer (在新终端)
cd src/Aevatar.BusinessServer/src/Aevatar.BusinessServer.Web
dotnet run
```

### 2. 获取 Token

```bash
curl -k -X POST https://localhost:44320/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=New_App" \
  -d "username=admin" \
  -d "password=1q2w3E*" \
  -d "scope=openid profile email phone address roles Aevatar offline_access"
```

**响应示例:**
```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6...",
  "token_type": "Bearer",
  "expires_in": 3599,
  "refresh_token": "eyJhbGciOiJSU0EtT0FFUCIsImVuYyI6..."
}
```

### 3. 使用 Token 调用 API

```bash
TOKEN="<your_access_token>"

curl -k -X GET https://localhost:44345/api/your-endpoint \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json"
```

## 📋 Token Payload 说明

生成的 Token 包含以下关键字段：

```json
{
  "iss": "https://localhost:44320/",       // Issuer
  "aud": "Aevatar",                        // Audience (same as old project)
  "scope": "openid profile ... Aevatar",   // Scopes
  "sub": "7648c8b7-3c1a-78ed-...",        // User ID
  "preferred_username": "admin",            // Username
  "email": "admin@abp.io",                 // Email
  "role": "admin",                         // Role
  "exp": 1763452655,                       // Expiration time
  "client_id": "New_App"                   // Client ID
}
```

## 🔧 Configuration

### AuthServer
- **URL:** https://localhost:44320
- **Authority:** https://localhost:44320/
- **Database:** mongodb://localhost:27017/New

### BusinessServer  
- **URL:** https://localhost:44345
- **Database:** mongodb://localhost:27017/BusinessServer
- **Audience:** Aevatar
- **Authority:** https://localhost:44320/

### Available Clients

| Client ID | Type | Grant Types | Purpose |
|-----------|------|-------------|---------|
| New_App | Public | password, authorization_code, refresh_token | Web/Console apps |
| New_Swagger | Public | authorization_code | Swagger UI |
| New_AuthServer | Public | password, authorization_code, refresh_token | AuthServer itself |
| BusinessServer | Confidential | client_credentials, authorization_code | API server (secret: BusinessServerSecret) |

## 🔄 Refresh Token

When access token expires:

```bash
curl -k -X POST https://localhost:44320/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=refresh_token" \
  -d "client_id=New_App" \
  -d "refresh_token=<your_refresh_token>"
```

## 🔐 Other Grant Types

### Client Credentials (Service-to-Service)

```bash
curl -k -X POST https://localhost:44320/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=BusinessServer" \
  -d "client_secret=BusinessServerSecret" \
  -d "scope=Aevatar"
```

### Authorization Code (Web Applications)

1. Get authorization code:
```
https://localhost:44320/connect/authorize?
  client_id=New_App&
  redirect_uri=https://localhost:4200&
  response_type=code&
  scope=openid profile email Aevatar
```

2. Exchange for token:
```bash
curl -k -X POST https://localhost:44320/connect/token \
  -d "grant_type=authorization_code" \
  -d "client_id=New_App" \
  -d "code=<authorization_code>" \
  -d "redirect_uri=https://localhost:4200"
```

## ⚠️ Differences from Old Project

### ✅ Same
- Token format fully compatible
- Audience: "Aevatar"
- Scope: "Aevatar"
- All standard Claims
- JWT Bearer validation

### ⚠️ Differences
1. **security_stamp**: Not present in new token
   - Impact: No impact on API authorization
   - Usage: Old project used it to force re-login
   - Solution: Can be added via AuthServer configuration if needed

2. **client_id**: Old uses "AevatarAuthServer", new uses "New_App"
   - Impact: No functional impact, just naming

## 🐛 Troubleshooting

### Token Validation Failed
1. Check Audience matches: "Aevatar"
2. Check Issuer format: Must end with "/"
3. Check Scope contains: "Aevatar"

### OpenIddict Errors
- BusinessServer has removed OpenIddict Validation
- Only uses JWT Bearer validation
- Check module dependencies if OpenIddict errors occur

## 📝 Development Default Account

| Username | Password | Role |
|----------|----------|------|
| admin | 1q2w3E* | admin |

## 🌐 Endpoints

### AuthServer
- Token Endpoint: `POST /connect/token`
- Authorization: `GET /connect/authorize`
- UserInfo: `GET /connect/userinfo`
- Discovery: `GET /.well-known/openid-configuration`
- Logout: `POST /connect/logout`

### BusinessServer
- Application Config: `GET /api/abp/application-configuration`
- Swagger UI: `GET /swagger`

---

## 🔧 Fixed Issues

### 1. Scope Creation Bug
**File:** `OpenIddictDataSeedContributor.cs:61`

**Problem:** Checking for wrong scope name
```csharp
// ❌ Wrong
if (await _openIddictScopeRepository.FindByNameAsync("New") == null)
{
    await _scopeManager.CreateAsync(new OpenIddictScopeDescriptor {
        Name = "Aevatar",  // Creating "Aevatar" but checking "New"
        ...
    });
}
```

**Solution:**
```csharp
// ✅ Correct
if (await _openIddictScopeRepository.FindByNameAsync("Aevatar") == null)
{
    await _scopeManager.CreateAsync(new OpenIddictScopeDescriptor {
        Name = "Aevatar",
        DisplayName = "Aevatar API",
        Resources = { "Aevatar" }
    });
}
```

### 2. Audience Mismatch
**File:** `BusinessServerWebModule.cs:158`

**Problem:** BusinessServer expecting different audience
```csharp
// ❌ Wrong
options.Audience = "BusinessServer";
```

**Solution:**
```csharp
// ✅ Correct
options.Audience = "Aevatar"; // Same as old HttpApi.Host
```

### 3. OpenIddict Validation Conflict
**File:** `BusinessServerWebModule.cs:113-119`

**Problem:** ABP modules automatically registered OpenIddict Validation, causing conflict with JWT Bearer

**Solution:**
```csharp
// Remove OpenIddict Validation services - we only use JWT Bearer
var openIddictServices = context.Services
    .Where(s => s.ServiceType.FullName?.Contains("OpenIddict.Validation") == true)
    .ToList();
foreach (var service in openIddictServices)
{
    context.Services.Remove(service);
}
```

---

**✅ Fully compatible with old project's token validation mechanism**

