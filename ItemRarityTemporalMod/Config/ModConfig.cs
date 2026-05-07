using System.Collections.Generic;
using System.IO;
using System.Linq;
using ItemRarity.Logging;
using ItemRarity.Rarities;
using Vintagestory.API.Common;

namespace ItemRarity.Config;

/// <summary>
/// Represents the configuration.
/// </summary>
public sealed class ModConfig
{
    private const string ConfigDirectoryName = "itemrarity";
    private static readonly string ConfigRaritiesFileName = Path.Combine(ConfigDirectoryName, "rarities.json");
    private static readonly string ConfigRarityChancesFileName = Path.Combine(ConfigDirectoryName, "rarity-chances.json");

    public required RaritiesConfig Rarity { get; init; }
    public required RarityChancesConfig Chance { get; init; }

    public static ModConfig Load(ICoreAPI api)
    {
        try
        {
            var rarities = api.LoadModConfig<RaritiesConfig>(ConfigRaritiesFileName);
            var chances = api.LoadModConfig<RarityChancesConfig>(ConfigRarityChancesFileName);

            if (rarities is null || chances is null)
            {
                var defaultConfig = GetDefaultConfig();
                Save(api, defaultConfig);
                Logger.Notification("Configuration not found. The default configuration has been saved.");
                return defaultConfig;
            }

            chances.ApplyDefaults(rarities.Rarities);
            var config = new ModConfig
            {
                Rarity = rarities,
                Chance = chances,
            };
            Save(api, config);
            Logger.Notification("Configuration loaded.");
            return config;
        }
        catch
        {
            Logger.Error("Failed to load configuration. Falling back to the default configuration (Will not overwrite existing configuration file, please backup your configuration before deleting the file).");
            return GetDefaultConfig();
        }
    }

    private static void Save(ICoreAPI api, ModConfig config)
    {
        // Persist both the rarity definitions and the tuned chances so the files stay in sync.
        api.StoreModConfig(config.Rarity, ConfigRaritiesFileName);
        api.StoreModConfig(config.Chance, ConfigRarityChancesFileName);
    }

    public static ModConfig GetDefaultConfig()
    {
        RarityModel[] rarities =
        [
            new()
            {
                Key = "cursed",
                Name = "Cursed",
                Level = -1,
                Color = "#606060",
                Weight = 8,
                DurabilityMultiplier = new[] { 0.2F, 0.9F },
                MiningSpeedMultiplier = new[] { 0.2F, 0.9F },
                AttackPowerMultiplier = new[] { 0.2F, 0.9F },
                PiercingPowerMultiplier = new[] { 0.2F, 0.9F },
                ArmorFlatDamageReductionMultiplier = new[] { 0.2F, 0.9F },
                ArmorPerTierFlatDamageProtectionLossMultiplier = new[] { 0.2F, 0.9F },
                ArmorRelativeProtectionMultiplier = new[] { 0.2F, 0.9F },
                ArmorPerTierRelativeProtectionLossMultiplier = new[] { 0.2F, 0.9F },
                ShieldProtectionMultiplier = new[] { 0.2F, 0.9F },
            },
            new()
            {
                Key = "common",
                Name = "Common",
                Level = 0,
                Color = "#FFFFFF",
                Weight = 40,
                DurabilityMultiplier = 1F,
                MiningSpeedMultiplier = 1F,
                AttackPowerMultiplier = 1F,
                PiercingPowerMultiplier = 1F,
                ArmorFlatDamageReductionMultiplier = 1F,
                ArmorPerTierFlatDamageProtectionLossMultiplier = 1F,
                ArmorRelativeProtectionMultiplier = 1F,
                ArmorPerTierRelativeProtectionLossMultiplier = 1F,
                ShieldProtectionMultiplier = 1F
            },
            new()
            {
                Key = "uncommon",
                Name = "Uncommon",
                Level = 1,
                Color = "#36FF00",
                Weight = 30,
                DurabilityMultiplier = 1.1F,
                MiningSpeedMultiplier = 1.1F,
                AttackPowerMultiplier = 1.1F,
                PiercingPowerMultiplier = 1.1F,
                ArmorFlatDamageReductionMultiplier = 1.1F,
                ArmorPerTierFlatDamageProtectionLossMultiplier = 1.1F,
                ArmorRelativeProtectionMultiplier = 1.1F,
                ArmorPerTierRelativeProtectionLossMultiplier = 1.1F,
                ShieldProtectionMultiplier = 1.1F
            },
            new()
            {
                Key = "rare",
                Name = "Rare",
                Level = 2,
                Color = "#13DBE8",
                Weight = 20,
                DurabilityMultiplier = 1.2F,
                MiningSpeedMultiplier = 1.2F,
                AttackPowerMultiplier = 1.2F,
                PiercingPowerMultiplier = 1.2F,
                ArmorFlatDamageReductionMultiplier = 1.2F,
                ArmorPerTierFlatDamageProtectionLossMultiplier = 1.2F,
                ArmorRelativeProtectionMultiplier = 1.2F,
                ArmorPerTierRelativeProtectionLossMultiplier = 1.2F,
                ShieldProtectionMultiplier = 1.2F
            },
            new()
            {
                Key = "epic",
                Name = "Epic",
                Level = 3,
                Color = "#8413E8",
                Weight = 12,
                DurabilityMultiplier = 1.4F,
                MiningSpeedMultiplier = 1.3F,
                AttackPowerMultiplier = 1.3F,
                PiercingPowerMultiplier = 1.3F,
                ArmorFlatDamageReductionMultiplier = 1.3F,
                ArmorPerTierFlatDamageProtectionLossMultiplier = 1.3F,
                ArmorRelativeProtectionMultiplier = 1.3F,
                ArmorPerTierRelativeProtectionLossMultiplier = 1.3F,
                ShieldProtectionMultiplier = 1.3F
            },
            new()
            {
                Key = "legendary",
                Name = "Legendary",
                Level = 4,
                Color = "#E08614",
                Weight = 8,
                DurabilityMultiplier = 1.6F,
                MiningSpeedMultiplier = 1.5F,
                AttackPowerMultiplier = 1.5F,
                PiercingPowerMultiplier = 1.5F,
                ArmorFlatDamageReductionMultiplier = 1.5F,
                ArmorPerTierFlatDamageProtectionLossMultiplier = 1.5F,
                ArmorRelativeProtectionMultiplier = 1.5F,
                ArmorPerTierRelativeProtectionLossMultiplier = 1.5F,
                ShieldProtectionMultiplier = 1.6F
            },
            new()
            {
                Key = "unique",
                Name = "Unique",
                Level = 10,
                Color = "#EC290E",
                Weight = 2,
                DurabilityMultiplier = 2F,
                MiningSpeedMultiplier = 1.9F,
                AttackPowerMultiplier = 1.9F,
                PiercingPowerMultiplier = 1.9F,
                ArmorFlatDamageReductionMultiplier = 1.9F,
                ArmorPerTierFlatDamageProtectionLossMultiplier = 1.9F,
                ArmorRelativeProtectionMultiplier = 1.9F,
                ArmorPerTierRelativeProtectionLossMultiplier = 1.9F,
                ShieldProtectionMultiplier = 1.9F,
                Effects = ["Thor"]
            }
        ];

        var rarityDictionary = rarities.ToDictionary(r => r.Key);

        return new()
        {
            Rarity = new()
            {
                Rarities = rarityDictionary,
            },
            // Spin up the default chance map so a newly written rarity-chances.json reflects the supposed defaults.
            Chance = RarityChancesConfig.CreateDefault(rarityDictionary)
        };
    }
}
