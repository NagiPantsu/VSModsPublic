using System;
using System.Collections.Generic;
using ItemRarity.Rarities;
using Newtonsoft.Json;

namespace ItemRarity.Config;

public sealed class RarityChancesConfig
{
    // Holds the tunable weight map that lives in rarity-chances.json.
    public const float MinChance = 0.01F;
    public const float MaxChance = 1000F;

    [JsonProperty(Order = 0)]
    public Dictionary<string, float> Chances { get; set; } = new();

    public float GetEffectiveChance(string rarityKey, float fallback)
    {
        if (Chances.TryGetValue(rarityKey, out var value))
            return value;

        // fall back to the rarity's built-in weight if the tuning file forgets this key,
        // applying the same clamp that we use for user-provided values.
        return Math.Clamp(fallback, MinChance, MaxChance);
    }

    public void ApplyDefaults(IReadOnlyDictionary<string, RarityModel> rarities)
    {
        var sanitized = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (var rarity in rarities)
        {
            var rawWeight = rarity.Value.Weight;
            var configuredWeight = Chances.TryGetValue(rarity.Key, out var custom) ? custom : rawWeight;
            sanitized[rarity.Key] = Math.Clamp(configuredWeight, MinChance, MaxChance);
        }

        // Replace the dictionary so we never keep stale or out-of-range numbers around.
        Chances = sanitized;
    }

    public static RarityChancesConfig CreateDefault(IReadOnlyDictionary<string, RarityModel> rarities)
    {
        var config = new RarityChancesConfig();
        foreach (var rarity in rarities)
        {
            config.Chances[rarity.Key] = Math.Clamp(rarity.Value.Weight, MinChance, MaxChance);
        }

        // Pre-fills the map with safe defaults so the file is human-readable immediately.
        return config;
    }
}
