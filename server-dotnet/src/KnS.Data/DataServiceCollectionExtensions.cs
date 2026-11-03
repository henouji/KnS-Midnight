using KnS.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace KnS.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database, one <see cref="DataSession"/> per scope (opened on first use) and
    /// the repositories over it.
    /// </summary>
    public static IServiceCollection AddKnSData(this IServiceCollection services, DatabaseOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<GameDatabase>();
        services.AddScoped(provider => provider.GetRequiredService<GameDatabase>().OpenSession());

        services.AddScoped<PlayerRepository>();
        services.AddScoped<HeroRepository>();
        services.AddScoped<ItemRepository>();
        services.AddScoped<GoodsRepository>();
        services.AddScoped<FellowRepository>();
        services.AddScoped<StageRepository>();
        services.AddScoped<MasterDataRepository>();
        services.AddScoped<PlatformAccountRepository>();
        services.AddScoped<TesterRepository>();
        services.AddScoped<FeatureFlagRepository>();
        services.AddScoped<AccountAdminRepository>();
        return services;
    }
}
