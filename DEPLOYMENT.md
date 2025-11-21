# Aevatar Agent Framework - Deployment Guide

## Prerequisites

**Required:**
- .NET 9.0 SDK
- MongoDB (running on `mongodb://localhost:27017`)
- Git

**Not Required:**
- ❌ ABP CLI is NOT required for deployment

## Step 1: Clone Repository

```bash
git clone <repository-url>
cd aevatar-agent-framework
git checkout feature/auth-server-admin-ui
```

## Step 2: Restore Frontend Libraries

The `wwwroot/libs` directories are not tracked by Git (ignored by `.gitignore`). You need to restore them using one of these methods:

### Option A: Using ABP CLI (Recommended if available)

```bash
# Install ABP CLI (if not installed)
dotnet tool install -g Volo.Abp.Cli

# Restore AuthServer frontend libraries
cd src/Aevatar.BusinessServer/src/Aevatar.AuthServer
abp install-libs

# Restore BusinessServer frontend libraries
cd ../Aevatar.BusinessServer.Web
abp install-libs
```

### Option B: Manual Copy Script (If ABP CLI fails)

Run this script to manually copy frontend libraries from `node_modules`:

```bash
#!/bin/bash
cd src/Aevatar.BusinessServer

echo "Restoring AuthServer frontend libraries..."
cd src/Aevatar.AuthServer
mkdir -p wwwroot/libs

# Core ABP libraries
cp -r ../../../../Aevatar.BusinessServer/src/Aevatar.BusinessServer.Web/wwwroot/libs/* wwwroot/libs/ 2>/dev/null || true

echo "Restoring BusinessServer frontend libraries..."
cd ../Aevatar.BusinessServer.Web

# Install npm packages
npm install

# Copy from node_modules to wwwroot/libs
mkdir -p wwwroot/libs
cp -r node_modules/@abp/aspnetcore-mvc-ui-theme-shared/wwwroot/libs/* wwwroot/libs/
cp -r node_modules/@fortawesome/fontawesome-free wwwroot/libs/@fortawesome/
cp -r node_modules/bootstrap/dist wwwroot/libs/bootstrap/
cp -r node_modules/jquery/dist wwwroot/libs/jquery/
# ... (add other libraries as needed)

echo "Frontend libraries restored!"
```

### Option C: Request Archive from Team

If Options A and B fail, contact the team lead to get a compressed archive of the `wwwroot/libs` directories.

## Step 3: Restore NuGet Packages

```bash
cd src/Aevatar.BusinessServer
dotnet restore
```

## Step 4: Initialize Database

```bash
cd src/Aevatar.BusinessServer.DbMigrator
dotnet run
```

This will:
- Create MongoDB database `New`
- Seed initial data (admin user, roles, permissions)
- Configure OpenIddict clients

**Default Admin Credentials:**
- Username: `admin` or `admin@abp.io`
- Password: `1q2w3E*`

## Step 5: Run AuthServer

```bash
cd ../src/Aevatar.AuthServer
dotnet run
```

AuthServer will start at: `https://localhost:44320`

**Verify:**
- Open `https://localhost:44320`
- Login with admin credentials
- Check: Administration → Identity → Users/Roles
- Check: Administration → Identity → Roles → Actions → Permissions (should be editable)

## Step 6: Run BusinessServer

```bash
cd ../Aevatar.BusinessServer.Web
dotnet run
```

BusinessServer will start at: `https://localhost:44345`

## Step 7: Verify Client Authentication

Test JWT authentication:

```bash
# Get token from AuthServer
curl -X POST https://localhost:44320/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=BusinessServer" \
  -d "client_secret=BusinessServerSecret" \
  -d "username=admin" \
  -d "password=1q2w3E*" \
  -d "scope=Aevatar" \
  -k

# Use token to access protected API
curl https://localhost:44345/api/identity/users \
  -H "Authorization: Bearer <access_token>" \
  -k
```

**Expected Results:**
- ✅ Token request: HTTP 200 with `access_token`
- ✅ Without token: HTTP 401 (Unauthorized)
- ✅ With valid token: HTTP 200 or HTTP 403 (depending on permissions)

## Troubleshooting

### Issue: `wwwroot/libs folder does not exist or empty`

**Solution:** You forgot Step 2. Run `abp install-libs` or use the manual copy script.

### Issue: `Undefined setting: Abp.Account.EnableLocalLogin`

**Solution:** This should not happen in the current version. If it does, rebuild the project:

```bash
dotnet clean
dotnet build
```

### Issue: Login shows "Invalid username or password"

**Solution:** Re-run DbMigrator to ensure data is seeded:

```bash
cd src/Aevatar.BusinessServer.DbMigrator
dotnet run
```

### Issue: Permission checkboxes are grayed out

**Solution:** This is fixed in the current version. If you still see this:
1. Check AuthServer logs for errors
2. Verify `AbpPermissionManagementDomainIdentityModule` and `AbpPermissionManagementDomainOpenIddictModule` are in `AuthServerModule.cs` dependencies
3. Clear browser cache and refresh

### Issue: Menu not displaying after login

**Solution:** Check browser console for JavaScript errors. Ensure `wwwroot/libs` is properly restored.

## Architecture Overview

```
┌─────────────────────────────────────┐
│         AuthServer (44320)          │
│  - OpenID Connect / OAuth 2.0       │
│  - User/Role Management UI          │
│  - Permission Management UI         │
│  - JWT Token Issuer                 │
└─────────────────────────────────────┘
                  ↓ (JWT Token)
┌─────────────────────────────────────┐
│      BusinessServer (44345)         │
│  - Agent Management APIs            │
│  - JWT Bearer Authentication        │
│  - Business Logic                   │
└─────────────────────────────────────┘
                  ↓
┌─────────────────────────────────────┐
│        MongoDB (27017)              │
│  - Database: "New"                  │
│  - Shared by both servers           │
└─────────────────────────────────────┘
```

## Key Configuration Files

- `src/Aevatar.BusinessServer/src/Aevatar.AuthServer/appsettings.json`
  - AuthServer URL: `https://localhost:44320`
  - MongoDB connection string

- `src/Aevatar.BusinessServer/src/Aevatar.BusinessServer.Web/appsettings.json`
  - BusinessServer URL: `https://localhost:44345`
  - AuthServer authority URL
  - JWT audience: `Aevatar`

- `src/Aevatar.BusinessServer/src/Aevatar.BusinessServer.DbMigrator/appsettings.json`
  - OpenIddict client configurations
  - Admin user credentials

## Notes

- **No ABP CLI Required**: This project can be deployed using only .NET CLI tools
- **Frontend Libraries**: Must be restored manually or via `abp install-libs`
- **Shared Database**: Both servers share the same MongoDB database
- **Default Port**: 
  - AuthServer: 44320 (HTTPS)
  - BusinessServer: 44345 (HTTPS)
- **Multi-tenancy**: Disabled (`MultiTenancyConsts.IsEnabled = false`)

## Support

For issues, check:
1. This troubleshooting guide
2. Server logs (`/tmp/authserver-*.log`, `/tmp/businessserver-*.log`)
3. MongoDB data (`mongosh "mongodb://localhost:27017/New"`)
4. Browser console for frontend errors

