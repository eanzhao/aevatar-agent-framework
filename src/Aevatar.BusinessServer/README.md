# BusinessServer

业务服务器 - 专注业务逻辑，认证授权统一由AuthServer管理。

## 快速启动

### 1. AuthServer (认证中心)
```bash
cd ../Aevatar.AuthServer/src/Aevatar.AuthServer.Web
dotnet run
```
访问: https://localhost:44320

### 2. BusinessServer (业务服务)
```bash
cd src/Aevatar.BusinessServer.Web
dotnet run
```
访问: https://localhost:44345

### 3. 默认账号
```
用户名: admin
密码: 1q2w3E*
```

---

## 架构

```
AuthServer (44320)      BusinessServer (44345)
     ↓                         ↓
  用户/角色/权限管理       业务逻辑 + Token验证
         ↓
    共享数据库 (MongoDB: New)
```

- **AuthServer**: 统一管理用户、角色、权限
- **BusinessServer**: 只验证JWT Token，专注业务

---

## API调用

### 认证测试API

```bash
# 1. 公共API（无需认证）
curl -k https://localhost:44345/api/app/auth-test/public-data

# 2. 获取Token
TOKEN=$(curl -k -s -X POST https://localhost:44320/connect/token \
  -d "grant_type=password" \
  -d "username=admin" \
  -d "password=1q2w3E*" \
  -d "client_id=New_AuthServer" \
  -d "scope=openid profile email roles Aevatar" \
  | jq -r '.access_token')

# 3. 受保护API（需要认证）
curl -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:44345/api/app/auth-test/authenticated-data
```

测试结果：
- 无Token访问受保护API → HTTP 401 ✓
- 有Token访问受保护API → HTTP 200 ✓

---

## 配置说明

**appsettings.json:**
- `ConnectionStrings:Default`: mongodb://localhost:27017/New (共享数据库)
- `AuthServer:Authority`: https://localhost:44320/
- `AuthServer:RequireHttpsMetadata`: false (开发环境)

**认证方案:**
- JWT Bearer: API请求
- Cookie: Web UI请求
- Smart Selector: 根据请求头自动选择

---

## 权限管理

### 在AuthServer UI管理
1. 访问 https://localhost:44320
2. Administration → Identity → Roles/Users
3. Actions → Permissions

### Client API权限

如需API权限（如 `/api/identity/users`），需配置Client权限：

```javascript
// MongoDB命令
use New
db.AbpPermissionGrants.insertMany([
  {
    TenantId: null,
    Name: "AbpIdentity.Users",
    ProviderName: "C",
    ProviderKey: "New_AuthServer",
    Granted: true
  }
  // ... 其他权限
])
```

或在代码中配置（推荐）：`OpenIddictDataSeedContributor.cs`

---

## 前端库

保留最小集合：
- abp (ABP核心)
- bootstrap (UI框架)
- @fortawesome (图标)
- jquery (DOM操作)
- jquery-validation (表单验证)
- luxon (日期时间)

已移除复杂UI组件（datatables, select2, sweetalert等），因为BusinessServer不再有复杂的管理UI。

---

更多信息: https://docs.abp.io/
