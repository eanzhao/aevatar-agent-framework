# Aevatar.Agents.AuthServer

Authentication server components migrated from old Aevatar.AuthServer project.

## Migrated Components

### Provider
- `IUserInformationProvider` - Interface for user information operations
- `UserInformationProvider` - Implementation for managing user extension information

## Dependencies

This project depends on:
- `UserExtensionDto` - User extension DTO (should be defined in Application.Contracts)
- `IdentityUserExtension` - User extension entity (should be defined in Domain)
- ABP Framework modules (Identity, OpenIddict, etc.)

## Notes

- Namespace changed from `Aevatar.Provider` to `Aevatar.Agents.AuthServer.Provider`
- Code structure remains unchanged, only namespace updated

