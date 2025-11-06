# Dependency Notes - Aevatar Agent Framework

## Required NuGet Packages

### Core Project Dependencies

The `Aevatar.Agents.Core` project requires the following NuGet packages:

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.0.0" />
```

#### Microsoft.Extensions.Caching.Memory

Required for the improved event deduplication mechanism (`MemoryCacheEventDeduplicator`).

**Important Notes:**
- Some methods like `Compact()` and `Clear()` are only available on the concrete `MemoryCache` class, not the `IMemoryCache` interface
- Always use type checking when calling implementation-specific methods:
  ```csharp
  if (_cache is MemoryCache memoryCache)
  {
      memoryCache.Compact(0.1);
  }
  ```

### Installation

To restore all packages:
```bash
dotnet restore
```

If you encounter SSL certificate issues, you can temporarily bypass them (not recommended for production):
```bash
dotnet restore --disable-parallel
```

Or update your NuGet configuration to trust the certificate:
```bash
dotnet nuget update source nuget.org --valid-authentication-types basic,negotiate,digest,ntlm,kerberos
```

## Version Compatibility

All packages are targeting .NET 9.0 and should use compatible versions:
- Use version `9.0.0` or higher for Microsoft.Extensions.* packages
- Ensure all projects in the solution target the same .NET version

## Known Issues

1. **SSL Certificate Errors**: Some environments may experience SSL certificate validation issues with NuGet.org. This typically happens in corporate networks with proxy servers.

2. **MemoryCache API Differences**: The `IMemoryCache` interface doesn't expose all methods available on the concrete `MemoryCache` class. Always use type checking for implementation-specific features.

---

*Last Updated: 2025-01-05*

