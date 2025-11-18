using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Aevatar.BusinessServer;

public class AuthTestAppService : BusinessServerAppService, IAuthTestAppService
{
    public Task<AuthTestDto> GetPublicDataAsync()
    {
        return Task.FromResult(new AuthTestDto
        {
            Message = "Public data - no authentication required",
            UserName = CurrentUser.UserName ?? "Anonymous",
            UserId = CurrentUser.Id?.ToString() ?? "None",
            Timestamp = DateTime.UtcNow,
            IsAuthenticated = CurrentUser.IsAuthenticated
        });
    }

    [Authorize]
    public Task<AuthTestDto> GetAuthenticatedDataAsync()
    {
        return Task.FromResult(new AuthTestDto
        {
            Message = "Authenticated data - login required",
            UserName = CurrentUser.UserName ?? "Unknown",
            UserId = CurrentUser.Id?.ToString() ?? "Unknown",
            Timestamp = DateTime.UtcNow,
            IsAuthenticated = CurrentUser.IsAuthenticated
        });
    }
}

