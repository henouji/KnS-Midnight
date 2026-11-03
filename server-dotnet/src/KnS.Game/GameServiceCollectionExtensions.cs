using KnS.Game.Access;
using KnS.Game.Fellows;
using KnS.Game.Gateway;
using KnS.Game.Inventory;
using KnS.Game.Players;
using KnS.Game.Protocols.Fellows;
using KnS.Game.Protocols.Heroes;
using KnS.Game.Protocols.Items;
using KnS.Game.Protocols.Profile;
using KnS.Game.Protocols.Session;
using KnS.Game.Protocols.Stages;
using KnS.Game.Randomness;
using KnS.Game.Stages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnS.Game;

public static class GameServiceCollectionExtensions
{
    /// <summary>Registers the game gateway, its services and one handler per protocol (needs the data services).</summary>
    public static IServiceCollection AddKnSGame(this IServiceCollection services, GameOptions options)
    {
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<LastLoadedPlayer>();
        services.AddSingleton<IRandomSource>(options.RandomSeed is { } seed
            ? new SeededRandomSource(seed)
            : new SystemRandomSource());

        services.AddScoped<GatewayTrace>();
        services.AddScoped<PlayerDirectory>();
        services.AddScoped<PlayerAllowlist>();
        services.AddScoped<FeatureFlags>();
        services.AddScoped<PlayerProfileService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<HeroGearRules>();
        services.AddScoped<ItemOptionRoller>();
        services.AddScoped<FellowService>();
        services.AddScoped<MonsterInfoBuilder>();
        services.AddScoped<StageProgressService>();
        services.AddScoped<StageRewardService>();
        services.AddScoped<BagExpander>();
        services.AddScoped<GameGateway>();

        AddHandler<VersionCheckHandler>(services, GameProtocol.VersionCheck);
        AddHandler<LoginHandler>(services, GameProtocol.Login);
        AddHandler<HubUidHandler>(services, GameProtocol.HubUid);
        AddHandler<HubDeviceHandler>(services, GameProtocol.HubDevice);
        AddHandler<EventInfoHandler>(services, GameProtocol.EventInfo);
        AddHandler<LoadUserHandler>(services, GameProtocol.LoadUser);
        AddHandler<InventoryHandler>(services, GameProtocol.Inventory);
        AddHandler<StageInfoHandler>(services, GameProtocol.StageInfo);
        AddHandler<StageStartHandler>(services, GameProtocol.StageStart);
        AddHandler<StageResultHandler>(services, GameProtocol.StageResult);
        AddHandler<StageContinueHandler>(services, GameProtocol.StageContinue);
        AddHandler<AreaRewardHandler>(services, GameProtocol.AreaReward);
        AddHandler<EquipItemHandler>(services, GameProtocol.EquipItem);
        AddHandler<PowerUpItemHandler>(services, GameProtocol.PowerUpItem);
        AddHandler<EnhanceItemHandler>(services, GameProtocol.EnhanceItem);
        AddHandler<SellItemsHandler>(services, GameProtocol.SellItems);
        AddHandler<LockItemHandler>(services, GameProtocol.LockItem);
        AddHandler<FellowDrawHandler>(services, GameProtocol.FellowDraw);
        AddHandler<ExpandItemBagHandler>(services, GameProtocol.ExpandItemBag);
        AddHandler<HeroBuyHandler>(services, GameProtocol.HeroBuy);
        AddHandler<HeroChangeHandler>(services, GameProtocol.HeroChange);
        AddHandler<ExpandAllyBagHandler>(services, GameProtocol.ExpandAllyBag);
        AddHandler<FellowSetHandler>(services, GameProtocol.FellowSet);
        AddHandler<FellowPowerUpHandler>(services, GameProtocol.FellowPowerUp);
        AddHandler<FellowSellHandler>(services, GameProtocol.FellowSell);
        AddHandler<FellowEnhanceHandler>(services, GameProtocol.FellowEnhance);
        return services;
    }

    private static void AddHandler<THandler>(IServiceCollection services, GameProtocol protocol)
        where THandler : class, IProtocolHandler =>
        services.AddKeyedScoped<IProtocolHandler, THandler>((int)protocol);
}
