using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Aevatar.Agents.AuthServer.Data;

public class UsersOptions
{
    public string AdminPassword { get; set; } = "1q2W3e*";
}

public class OpenIddictDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IOpenIddictApplicationRepository _openIddictApplicationRepository;
    private readonly IAbpApplicationManager _applicationManager;
    private readonly IOpenIddictScopeRepository _openIddictScopeRepository;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly IStringLocalizer _localizer;
    private readonly IdentityUserManager _identityUserManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly UsersOptions _usersOptions;
    private readonly IPermissionManager _permissionManager;

    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager,
        IPermissionDataSeeder permissionDataSeeder,
        IdentityUserManager identityUserManager,
        IOptionsSnapshot<UsersOptions> userOptions,
        IStringLocalizer localizer,
        IPermissionManager permissionManager,
        IdentityRoleManager roleManager)
    {
        _configuration = configuration;
        _openIddictApplicationRepository = openIddictApplicationRepository;
        _applicationManager = applicationManager;
        _openIddictScopeRepository = openIddictScopeRepository;
        _scopeManager = scopeManager;
        _permissionDataSeeder = permissionDataSeeder;
        _localizer = localizer;
        _identityUserManager = identityUserManager;
        _usersOptions = userOptions.Value;
        _permissionManager = permissionManager;
        _roleManager = roleManager;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await CreateScopesAsync();
        await CreateApplicationsAsync();
        await SeedAdminUserAsync();
    }

    private async Task CreateScopesAsync()
    {
        if (await _openIddictScopeRepository.FindByNameAsync("Aevatar") == null)
        {
            await _scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "Aevatar",
                DisplayName = "Aevatar API",
                Resources = { "Aevatar" }
            });
        }
    }

    private async Task SeedAdminUserAsync()
    {
        var adminUser = await _identityUserManager.FindByNameAsync("admin");
        if (adminUser != null)
        {
            // Admin user exists, reset password to configured value
            var adminPassword = _usersOptions.AdminPassword;
            var token = await _identityUserManager.GeneratePasswordResetTokenAsync(adminUser);
            var result = await _identityUserManager.ResetPasswordAsync(adminUser, token, adminPassword);
            if (!result.Succeeded)
            {
                throw new Exception("Failed to set admin password: " +
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
        // If admin user doesn't exist, it will be created by ABP's IdentityDataSeedContributor
        // with the password set in DataSeedContext properties
        
        await SeedPermissionsFromConfigurationAsync();
    }

    private async Task SeedPermissionsFromConfigurationAsync()
    {
        var permissionMappings = _configuration.GetSection("PermissionMappings")
            .Get<Dictionary<string, List<string>>>();
        if (permissionMappings == null) return;

        int count = 0;
        foreach (var mapping in permissionMappings)
        {
            var roleName = mapping.Key;
            var permissions = mapping.Value;

            var roleExists = await _roleManager.RoleExistsAsync(roleName);
            if (!roleExists)
            {
                var identityRole = new IdentityRole(Guid.NewGuid(), roleName);
                identityRole.IsPublic = true;
                identityRole.IsStatic = true;
                if (count == 0)
                {
                    identityRole.IsDefault = true;
                }
                count++;
                var result = await _roleManager.CreateAsync(identityRole);
                if (!result.Succeeded)
                {
                    throw new Exception($"Failed to create role '{roleName}': " +
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            foreach (var permission in permissions)
            {
                await _permissionManager.SetAsync(
                    permission,
                    RolePermissionValueProvider.ProviderName,
                    roleName,
                    true
                );
            }
        }
    }

    private async Task CreateApplicationsAsync()
    {
        var commonScopes = new List<string>
        {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "Aevatar"
        };

        var configurationSection = _configuration.GetSection("OpenIddict:Applications");

        // Swagger Client
        var swaggerClientId = configurationSection["Aevatar_Swagger:ClientId"];
        if (!string.IsNullOrWhiteSpace(swaggerClientId))
        {
            var swaggerRootUrl = configurationSection["Aevatar_Swagger:RootUrl"]?.TrimEnd('/');

            await CreateApplicationAsync(
                name: swaggerClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Swagger Application",
                secret: null,
                grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode },
                scopes: commonScopes,
                redirectUri: $"{swaggerRootUrl}/swagger/oauth2-redirect.html",
                clientUri: swaggerRootUrl
            );
        }

        // AuthServer Client
        var authServerClientId = configurationSection["AevatarAuthServer:ClientId"];
        if (!string.IsNullOrWhiteSpace(authServerClientId))
        {
            var authServerRootUrl = configurationSection["AevatarAuthServer:RootUrl"]?.TrimEnd('/');

            await CreateApplicationAsync(
                name: authServerClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "AevatarAuthServer Application",
                secret: null,
                grantTypes: new List<string>
                {
                    OpenIddictConstants.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.GrantTypes.Password,
                    OpenIddictConstants.GrantTypes.ClientCredentials,
                    OpenIddictConstants.GrantTypes.RefreshToken,
                    "signature",
                    "login"
                },
                scopes: commonScopes,
                redirectUri: authServerRootUrl,
                clientUri: authServerRootUrl,
                postLogoutRedirectUri: authServerRootUrl
            );
        }
    }

    private async Task CreateApplicationAsync(
        string name,
        string type,
        string consentType,
        string displayName,
        string? secret,
        List<string> grantTypes,
        List<string> scopes,
        string? clientUri = null,
        string? redirectUri = null,
        string? postLogoutRedirectUri = null,
        List<string>? permissions = null)
    {
        if (!string.IsNullOrEmpty(secret) && string.Equals(type, OpenIddictConstants.ClientTypes.Public,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("NoClientSecretCanBeSetForPublicApplications");
        }

        if (string.IsNullOrEmpty(secret) && string.Equals(type, OpenIddictConstants.ClientTypes.Confidential,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("TheClientSecretIsRequiredForConfidentialApplications");
        }

        var client = await _openIddictApplicationRepository.FindByClientIdAsync(name);

        var application = new AbpApplicationDescriptor
        {
            ClientId = name,
            ClientType = type,
            ClientSecret = secret,
            ConsentType = consentType,
            DisplayName = displayName,
            ClientUri = clientUri,
        };

        Check.NotNullOrEmpty(grantTypes, nameof(grantTypes));
        Check.NotNullOrEmpty(scopes, nameof(scopes));

        if (new[] { OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.Implicit }
            .All(grantTypes.Contains))
        {
            application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdToken);

            if (string.Equals(type, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdTokenToken);
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(redirectUri) || !string.IsNullOrWhiteSpace(postLogoutRedirectUri))
        {
            // Logout endpoint is automatically available when redirect URIs are configured
        }

        var buildInGrantTypes = new[]
        {
            OpenIddictConstants.GrantTypes.Implicit,
            OpenIddictConstants.GrantTypes.Password,
            OpenIddictConstants.GrantTypes.AuthorizationCode,
            OpenIddictConstants.GrantTypes.ClientCredentials,
            OpenIddictConstants.GrantTypes.DeviceCode,
            OpenIddictConstants.GrantTypes.RefreshToken
        };

        foreach (var grantType in grantTypes)
        {
            if (grantType == OpenIddictConstants.GrantTypes.AuthorizationCode)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
            }

            if (grantType == OpenIddictConstants.GrantTypes.AuthorizationCode ||
                grantType == OpenIddictConstants.GrantTypes.Implicit)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            }

            if (grantType == OpenIddictConstants.GrantTypes.AuthorizationCode ||
                grantType == OpenIddictConstants.GrantTypes.ClientCredentials ||
                grantType == OpenIddictConstants.GrantTypes.Password ||
                grantType == OpenIddictConstants.GrantTypes.RefreshToken ||
                grantType == OpenIddictConstants.GrantTypes.DeviceCode)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Revocation);
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
            }

            if (grantType == OpenIddictConstants.GrantTypes.ClientCredentials)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
            }

            if (grantType == OpenIddictConstants.GrantTypes.Implicit)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Implicit);
            }

            if (grantType == OpenIddictConstants.GrantTypes.Password)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Password);
            }

            if (grantType == OpenIddictConstants.GrantTypes.RefreshToken)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
            }

            if (grantType == OpenIddictConstants.GrantTypes.DeviceCode)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.DeviceCode);
                // Device endpoint is automatically available for DeviceCode grant type
            }

            if (grantType == OpenIddictConstants.GrantTypes.Implicit)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdToken);
                if (string.Equals(type, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
                {
                    application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdTokenToken);
                    application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Token);
                }
            }

            if (!buildInGrantTypes.Contains(grantType))
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.GrantType + grantType);
            }
        }

        var buildInScopes = new[]
        {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles
        };

        foreach (var scope in scopes)
        {
            if (buildInScopes.Contains(scope))
            {
                application.Permissions.Add(scope);
            }
            else
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
            }
        }

        if (redirectUri != null)
        {
            if (!string.IsNullOrEmpty(redirectUri))
            {
                if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString())
                {
                    throw new BusinessException($"InvalidRedirectUri: {redirectUri}");
                }

                if (application.RedirectUris.All(x => x != uri))
                {
                    application.RedirectUris.Add(uri);
                }
            }
        }

        if (postLogoutRedirectUri != null)
        {
            if (!string.IsNullOrEmpty(postLogoutRedirectUri))
            {
                if (!Uri.TryCreate(postLogoutRedirectUri, UriKind.Absolute, out var uri) ||
                    !uri.IsWellFormedOriginalString())
                {
                    throw new BusinessException($"InvalidPostLogoutRedirectUri: {postLogoutRedirectUri}");
                }

                if (application.PostLogoutRedirectUris.All(x => x != uri))
                {
                    application.PostLogoutRedirectUris.Add(uri);
                }
            }
        }

        if (permissions != null)
        {
            await _permissionDataSeeder.SeedAsync(
                ClientPermissionValueProvider.ProviderName,
                name,
                permissions,
                null
            );
        }

        if (client == null)
        {
            await _applicationManager.CreateAsync(application);
            return;
        }

        if (!HasSameRedirectUris(client, application))
        {
            client.RedirectUris = JsonSerializer.Serialize(application.RedirectUris.Select(q => q.ToString().TrimEnd('/')));
            client.PostLogoutRedirectUris = JsonSerializer.Serialize(application.PostLogoutRedirectUris.Select(q => q.ToString().TrimEnd('/')));

            await _applicationManager.UpdateAsync(client.ToModel());
        }

        if (!HasSameScopes(client, application))
        {
            client.Permissions = JsonSerializer.Serialize(application.Permissions.Select(q => q.ToString()));
            await _applicationManager.UpdateAsync(client.ToModel());
        }
    }

    private bool HasSameRedirectUris(OpenIddictApplication existingClient, AbpApplicationDescriptor application)
    {
        return existingClient.RedirectUris == JsonSerializer.Serialize(application.RedirectUris.Select(q => q.ToString().TrimEnd('/')));
    }

    private bool HasSameScopes(OpenIddictApplication existingClient, AbpApplicationDescriptor application)
    {
        return existingClient.Permissions == JsonSerializer.Serialize(application.Permissions.Select(q => q.ToString().TrimEnd('/')));
    }
}

