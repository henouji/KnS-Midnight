using KnS.Platform.Accounts;
using KnS.Platform.ActiveUser;
using KnS.Platform.Identifiers;
using KnS.Platform.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnS.Platform;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>Registers the Com2uS platform surfaces (needs the data services).</summary>
    public static IServiceCollection AddKnSPlatform(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PlatformIdentifiers>();
        services.AddSingleton<PlatformReplies>();
        services.AddSingleton<ActiveUserGateway>();
        services.AddScoped<HubAccountService>();
        services.AddScoped<HiveGuestService>();
        return services;
    }
}
