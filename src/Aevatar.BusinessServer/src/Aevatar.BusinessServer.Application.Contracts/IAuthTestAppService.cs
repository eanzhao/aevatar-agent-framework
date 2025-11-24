using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Aevatar.BusinessServer;

public interface IAuthTestAppService : IApplicationService
{
    /// <summary>
    /// Public endpoint - no authentication required
    /// </summary>
    Task<AuthTestDto> GetPublicDataAsync();

    /// <summary>
    /// Protected endpoint - requires authentication only
    /// </summary>
    Task<AuthTestDto> GetAuthenticatedDataAsync();
}

