# Architecture Overview

## 架构说明

本项目采用**微服务架构**，包含两个独立的服务器：

### 1. AuthServer (认证服务器)
- **位置**: `src/Aevatar.AuthServer`
- **端口**: `https://localhost:44320`
- **职责**:
  - 用户认证和授权
  - 颁发 OAuth 2.0 / OpenID Connect 令牌
  - 管理用户、角色、权限
  - 提供管理界面
- **数据库**: MongoDB (`mongodb://localhost:27017/New`)
- **依赖**: 仅依赖 `DbMigrator` 初始化数据

### 2. BusinessServer (业务服务器)
- **位置**: `src/Aevatar.BusinessServer`
- **端口**: `https://localhost:44345`
- **职责**:
  - 提供业务 API
  - 验证 AuthServer 颁发的令牌
  - **不管理用户**（用户管理由 AuthServer 负责）
- **数据库**: MongoDB (`mongodb://localhost:27017/BusinessServer`)
- **依赖**: 依赖 AuthServer 进行认证

## 启动顺序

### 1. 初始化 AuthServer 数据库
```bash
cd src/Aevatar.AuthServer/src/Aevatar.AuthServer.DbMigrator
dotnet run
```

这会创建：
- 数据库结构
- Admin 用户（用户名: `admin`, 密码: `1q2w3E*`）
- OAuth 客户端（New_App, New_Swagger, New_AuthServer, BusinessServer）
- Scopes（New, BusinessServer）

### 2. 启动 AuthServer
```bash
cd src/Aevatar.AuthServer/src/Aevatar.AuthServer.Web
dotnet run
```

访问: `https://localhost:44320`

### 3. 启动 BusinessServer
```bash
cd src/Aevatar.BusinessServer/src/Aevatar.BusinessServer.Web
dotnet run
```

访问: `https://localhost:44345`

## OAuth 客户端配置

### AuthServer 中的客户端

1. **New_App** - 前端应用客户端
   - ClientId: `New_App`
   - Type: Public
   - Grant Types: AuthorizationCode, Password, ClientCredentials, RefreshToken

2. **New_Swagger** - Swagger UI 客户端
   - ClientId: `New_Swagger`
   - Type: Public
   - Grant Types: AuthorizationCode

3. **New_AuthServer** - AuthServer 自身认证
   - ClientId: `New_AuthServer`
   - Type: Public
   - Grant Types: AuthorizationCode, Password, ClientCredentials, RefreshToken

4. **BusinessServer** - 业务服务器客户端
   - ClientId: `BusinessServer`
   - Type: Confidential
   - ClientSecret: `BusinessServerSecret`
   - Grant Types: ClientCredentials, AuthorizationCode, RefreshToken

## Token 验证流程

```
Client Application
    ↓ (Request Token)
AuthServer (https://localhost:44320)
    ↓ (Issue Token)
Client Application
    ↓ (API Request with Token)
BusinessServer (https://localhost:44345)
    ↓ (Validate Token via Introspection)
AuthServer (https://localhost:44320/connect/introspect)
    ↓ (Token Valid)
BusinessServer (Process Request)
```

## 配置说明

### AuthServer 配置
- `appsettings.json`: 配置 AuthServer 的 Authority URL
- `DbMigrator/appsettings.json`: 配置 OAuth 客户端信息

### BusinessServer 配置
- `appsettings.json`: 
  - `AuthServer:Authority`: 指向 AuthServer 的 URL (`https://localhost:44320`)
  - `AuthServer:RequireHttpsMetadata`: 开发环境设为 `false`

## 注意事项

1. **AuthServer 是唯一的数据源**: 所有用户、角色、权限都在 AuthServer 中管理
2. **BusinessServer 不管理用户**: 业务服务器只验证令牌，不存储用户信息
3. **Token 验证**: BusinessServer 通过 Introspection 端点验证令牌
4. **生产环境**: 
   - 使用安全的 ClientSecret
   - 启用 HTTPS
   - 配置生产证书

