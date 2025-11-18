using Aevatar.Agents.AuthServer.User;

namespace Aevatar.Agents.AuthServer.Provider;

public interface IUserInformationProvider
{
    Task<bool> SaveUserExtensionInfoAsync(UserExtensionDto userExtensionDto);

    Task<UserExtensionDto> GetUserExtensionInfoByIdAsync(Guid userId);

    Task<UserExtensionDto> GetUserExtensionInfoByWalletAddressAsync(string address);
}

