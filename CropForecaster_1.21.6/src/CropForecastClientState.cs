using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace CropForecaster
{
    public enum CropForecastMode
    {
        Off,
        Basic,
        Full
    }

    public class CropForecastClientConfig
    {
        public CropForecastMode Mode { get; set; } = CropForecastMode.Basic;
        public int ProbabilisticSimulationCount { get; set; } = 48;
        public int TooltipCacheMilliseconds { get; set; } = 350;
    }

    internal readonly struct TooltipCacheKey : IEquatable<TooltipCacheKey>
    {
        public TooltipCacheKey(
            int x,
            int y,
            int z,
            int cropId,
            int cropStage,
            bool prePlantPreview,
            bool unsupportedSeedPreview,
            CropForecastMode mode,
            int hoursUntilNextStageTenths,
            int worldHourBucket,
            string heldItemCode)
        {
            X = x;
            Y = y;
            Z = z;
            CropId = cropId;
            CropStage = cropStage;
            PrePlantPreview = prePlantPreview;
            UnsupportedSeedPreview = unsupportedSeedPreview;
            Mode = mode;
            HoursUntilNextStageTenths = hoursUntilNextStageTenths;
            WorldHourBucket = worldHourBucket;
            HeldItemCode = heldItemCode ?? string.Empty;
        }

        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public int CropId { get; }
        public int CropStage { get; }
        public bool PrePlantPreview { get; }
        public bool UnsupportedSeedPreview { get; }
        public CropForecastMode Mode { get; }
        public int HoursUntilNextStageTenths { get; }
        public int WorldHourBucket { get; }
        public string HeldItemCode { get; }

        public bool Equals(TooltipCacheKey other)
        {
            return X == other.X
                && Y == other.Y
                && Z == other.Z
                && CropId == other.CropId
                && CropStage == other.CropStage
                && PrePlantPreview == other.PrePlantPreview
                && UnsupportedSeedPreview == other.UnsupportedSeedPreview
                && Mode == other.Mode
                && HoursUntilNextStageTenths == other.HoursUntilNextStageTenths
                && WorldHourBucket == other.WorldHourBucket
                && string.Equals(HeldItemCode, other.HeldItemCode, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is TooltipCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(X);
            hash.Add(Y);
            hash.Add(Z);
            hash.Add(CropId);
            hash.Add(CropStage);
            hash.Add(PrePlantPreview);
            hash.Add(UnsupportedSeedPreview);
            hash.Add((int)Mode);
            hash.Add(HoursUntilNextStageTenths);
            hash.Add(WorldHourBucket);
            hash.Add(HeldItemCode, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }

    internal readonly struct TooltipCacheEntry
    {
        public TooltipCacheEntry(string value, long createdAtMs)
        {
            Value = value;
            CreatedAtMs = createdAtMs;
        }

        public string Value { get; }
        public long CreatedAtMs { get; }
    }

    internal static class CropForecastClientState
    {
        private const string ConfigFileName = "cropforecaster.client.json";
        private const int MaxCachedTooltips = 64;
        private static readonly Dictionary<TooltipCacheKey, TooltipCacheEntry> TooltipCache = new Dictionary<TooltipCacheKey, TooltipCacheEntry>();

        private static ICoreClientAPI? clientApi;
        private static CropForecastClientConfig config = new CropForecastClientConfig();

        public static CropForecastMode Mode => config.Mode;

        public static int ProbabilisticSimulationCount => Math.Max(1, config.ProbabilisticSimulationCount);

        public static void Initialize(ICoreClientAPI api)
        {
            clientApi = api;
            config = LoadConfig(api);
            InvalidateTooltipCache();
        }

        public static CropForecastMode CycleMode()
        {
            config.Mode = config.Mode switch
            {
                CropForecastMode.Off => CropForecastMode.Basic,
                CropForecastMode.Basic => CropForecastMode.Full,
                _ => CropForecastMode.Off
            };

            SaveConfig();
            InvalidateTooltipCache();
            return config.Mode;
        }

        public static bool TryGetCachedTooltip(TooltipCacheKey key, out string value)
        {
            value = string.Empty;

            if (clientApi == null)
            {
                return false;
            }

            long now = clientApi.InWorldEllapsedMilliseconds;
            PruneExpiredEntries(now);

            if (!TooltipCache.TryGetValue(key, out TooltipCacheEntry entry))
            {
                return false;
            }

            if (now - entry.CreatedAtMs > config.TooltipCacheMilliseconds)
            {
                TooltipCache.Remove(key);
                return false;
            }

            value = entry.Value;
            return true;
        }

        public static void StoreCachedTooltip(TooltipCacheKey key, string value)
        {
            if (clientApi == null)
            {
                return;
            }

            long now = clientApi.InWorldEllapsedMilliseconds;
            PruneExpiredEntries(now);

            if (TooltipCache.Count >= MaxCachedTooltips)
            {
                RemoveOldestEntry();
            }

            TooltipCache[key] = new TooltipCacheEntry(value, now);
        }

        public static void InvalidateTooltipCache()
        {
            TooltipCache.Clear();
        }

        private static CropForecastClientConfig LoadConfig(ICoreClientAPI api)
        {
            try
            {
                CropForecastClientConfig? loadedConfig = api.LoadModConfig<CropForecastClientConfig>(ConfigFileName);
                if (loadedConfig != null)
                {
                    loadedConfig.ProbabilisticSimulationCount = Math.Max(1, loadedConfig.ProbabilisticSimulationCount);
                    loadedConfig.TooltipCacheMilliseconds = Math.Max(0, loadedConfig.TooltipCacheMilliseconds);
                    return loadedConfig;
                }
            }
            catch (Exception exception)
            {
                api.Logger.Warning("CropForecaster: Failed to load client config, using defaults. Exception: {0}", exception);
            }

            return new CropForecastClientConfig();
        }

        private static void SaveConfig()
        {
            clientApi?.StoreModConfig(config, ConfigFileName);
        }

        private static void PruneExpiredEntries(long now)
        {
            if (TooltipCache.Count == 0)
            {
                return;
            }

            List<TooltipCacheKey>? expiredKeys = null;
            foreach ((TooltipCacheKey key, TooltipCacheEntry entry) in TooltipCache)
            {
                if (now - entry.CreatedAtMs <= config.TooltipCacheMilliseconds)
                {
                    continue;
                }

                expiredKeys ??= new List<TooltipCacheKey>();
                expiredKeys.Add(key);
            }

            if (expiredKeys == null)
            {
                return;
            }

            foreach (TooltipCacheKey key in expiredKeys)
            {
                TooltipCache.Remove(key);
            }
        }

        private static void RemoveOldestEntry()
        {
            TooltipCacheKey? oldestKey = null;
            long oldestTimestamp = long.MaxValue;

            foreach ((TooltipCacheKey key, TooltipCacheEntry entry) in TooltipCache)
            {
                if (entry.CreatedAtMs >= oldestTimestamp)
                {
                    continue;
                }

                oldestTimestamp = entry.CreatedAtMs;
                oldestKey = key;
            }

            if (oldestKey.HasValue)
            {
                TooltipCache.Remove(oldestKey.Value);
            }
        }
    }
}
