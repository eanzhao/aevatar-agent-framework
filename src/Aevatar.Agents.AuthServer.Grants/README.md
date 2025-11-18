# Aevatar.Agents.AuthServer.Grants

Authentication grant handlers and providers migrated from old Aevatar.AuthServer.Grants project.

## Migrated Components

### Options
- `SignatureGrantOptions` - Configuration options for signature grant
- `ChainOptions` - Chain configuration options
- `ChainInfo` - Chain information model

### Providers
- `IWalletLoginProvider` - Interface for wallet login operations
- `WalletLoginProvider` - Implementation for wallet signature verification
- `HolderInfoDto` - Holder information DTOs
- `CAHolderManager` - CA holder manager models
- `ManagerCacheDto` - Manager cache DTO
- `ManagerCheckHelper` - Helper for manager validation

## Dependencies

This project depends on:
- `UserChainAddressDto` - User chain address DTO (should be defined in Application.Contracts)
- AElf SDK for blockchain operations
- GraphQL.Client for GraphQL queries
- ABP Framework modules

## Notes

- Namespace changed from `Aevatar.AuthServer.Grants.*` to `Aevatar.Agents.AuthServer.Grants.*`
- Code structure remains unchanged, only namespace updated
- WalletLoginProvider contains wallet signature verification logic for both EOA and CA wallets

