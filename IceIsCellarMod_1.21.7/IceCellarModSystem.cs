using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace IceCellarMod
{


    public class IceCellarConfig
    {
        public float IceRatioThreshold = 0.5f;
        public float TemperatureBonus = 5.0f;
        public float MinPerishRate = 0.1f;
        public float LightPenaltyMultiplier = 0.5f;
    }
    public class IceRoomEntry 
    {
        public Cuboidi Location;
        public bool IsIceCellar;

        public IceRoomEntry(Room room, bool isIceCellar)
        {
            Location = room.Location;
            IsIceCellar = isIceCellar;
        }
    }

    //Stupid rooms can get funky so we stop using string keys.
    struct RoomKey
    {
        public readonly int X1, Y1, Z1;
        public readonly int X2, Y2, Z2;

        public RoomKey(Room room)
        {
            X1 = room.Location.Start.X;
            Y1 = room.Location.Start.Y;
            Z1 = room.Location.Start.Z;

            X2 = room.Location.End.X;
            Y2 = room.Location.End.Y;
            Z2 = room.Location.End.Z;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X1, Y1, Z1, X2, Y2, Z2);
        }

        public override bool Equals(object? obj)
        {
            return obj is RoomKey other &&
                X1 == other.X1 && Y1 == other.Y1 && Z1 == other.Z1 &&
                X2 == other.X2 && Y2 == other.Y2 && Z2 == other.Z2;
        }
    }

    public class IceCellarModSystem : ModSystem
    {
        ICoreAPI api = null!;
        IceCellarConfig config = null!;

        // Updated to use unique keys
        readonly Dictionary<RoomKey, IceRoomEntry> iceRoomCache = new();

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            api.RegisterBlockBehaviorClass("IceCooling", typeof(BlockBehaviorIceCooling));
            config = LoadOrCreateConfig();
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            sapi.Event.DidBreakBlock += (byPlayer, oldBlockId, blockSel)
                => InvalidateCacheAt(blockSel.Position);
            sapi.Event.DidPlaceBlock += (byPlayer, oldblockId, blockSel, withItemStack)
                => InvalidateCacheAt(blockSel.Position);    
        }

        void InvalidateCacheAt(BlockPos pos)
        {
           var keysToRemove = new List<RoomKey>();

            foreach (var entry in iceRoomCache)
            {
                if (entry.Value.Location.Contains(pos.X, pos.Y, pos.Z))
                {
                    keysToRemove.Add(entry.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                iceRoomCache.Remove(key);
            }
        }

        public float? GetIceCellarPerishRateOverride(BlockPos pos, IWorldAccessor world)
        {
            var roomRegistry = api.ModLoader.GetModSystem<RoomRegistry>();
            if (roomRegistry == null) return null;

            Room? room = roomRegistry.GetRoomForPosition(pos);
            if (room == null || room.ExitCount != 0 || !room.IsSmallRoom) return null;
            if (room.CoolingWallCount <= 0) return null;

            // Use room bounds as unique identifier
            RoomKey roomKey = new RoomKey(room);

            if (!iceRoomCache.TryGetValue(roomKey, out IceRoomEntry? entry))
            {
                bool isIce = ComputeIsIceCellar(room, pos, world);
                entry = new IceRoomEntry(room, isIce);
                iceRoomCache[roomKey] = entry;
            }

            if (!entry.IsIceCellar) return null;

            float outsideTemp = world.BlockAccessor
                .GetClimateAt(pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly,
                              world.Calendar.TotalDays)
                .Temperature;

            float cellarScore = GameMath.Clamp(
                (float)room.CoolingWallCount / (room.CoolingWallCount + room.NonCoolingWallCount),
                0f, 1f);

            float skylightProportion = (float)room.SkylightCount
                / Math.Max(1, room.SkylightCount + room.NonSkylightCount);

            int sunlightLevel = world.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.OnlySunLight);
            float sunlightStrength = GameMath.Clamp(sunlightLevel - 11, 0, 10);

            float lightImportance = GameMath.Clamp(
                0.1f + 0.2f * cellarScore + 1.25f * skylightProportion,
                0f, 1.25f);

            float lightWarming = sunlightStrength * lightImportance * config.LightPenaltyMultiplier;
            float adjustedOutsideTemp = outsideTemp + lightWarming;

            float vanillaTemp = adjustedOutsideTemp <= 5f
                ? adjustedOutsideTemp
                : 5f + (adjustedOutsideTemp - 5f) * (1f - cellarScore);

            float iceTemp = vanillaTemp - config.TemperatureBonus;

            float minPerishRate = GameMath.Clamp(config.MinPerishRate, 0f, 2.4f);

            float rate = Math.Max(minPerishRate,
                Math.Min(2.4f, (float)Math.Pow(3.0, iceTemp / 19.0 - 1.2) - 0.1f));

            if (rate >= 2.4f) return null;

            return rate;
        }

        IceCellarConfig LoadOrCreateConfig()
        {
            const string configFileName = "coolingmodconfig.json";

            try
            {
                IceCellarConfig? loadedConfig = api.LoadModConfig<IceCellarConfig>(configFileName);
                if (loadedConfig != null)
                {
                    bool configMissingFields = ConfigIsMissingFields(configFileName, nameof(IceCellarConfig.LightPenaltyMultiplier));
                    return SanitizeConfig(loadedConfig, configFileName, configMissingFields);
                }

                var defaultConfig = new IceCellarConfig();
                api.StoreModConfig(defaultConfig, configFileName);
                return defaultConfig;
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[IceCellar] Failed to load coolingmodconfig.json, backing it up and regenerating defaults. Exception: {0}", ex);

                BackupBrokenConfig(configFileName);

                var defaultConfig = new IceCellarConfig();

                try
                {
                    api.StoreModConfig(defaultConfig, configFileName);
                }
                catch (Exception saveEx)
                {
                    api.Logger.Error("[IceCellar] Failed to write default coolingmodconfig.json after load failure. Exception: {0}", saveEx);
                }

                return defaultConfig;
            }
        }

        IceCellarConfig SanitizeConfig(IceCellarConfig loadedConfig, string configFileName, bool rewriteForMissingFields = false)
        {
            bool changed = rewriteForMissingFields;

            // Ratios are only meaningful between 0 and 1.
            if (loadedConfig.IceRatioThreshold < 0f)
            {
                loadedConfig.IceRatioThreshold = 0f;
                changed = true;
            }
            else if (loadedConfig.IceRatioThreshold > 1f)
            {
                loadedConfig.IceRatioThreshold = 1f;
                changed = true;
            }

            // Negative perish rates do not make sense, but high values are still a user choice.
            if (loadedConfig.MinPerishRate < 0f)
            {
                loadedConfig.MinPerishRate = 0f;
                changed = true;
            }

            if (loadedConfig.LightPenaltyMultiplier < 0f)
            {
                loadedConfig.LightPenaltyMultiplier = 0f;
                changed = true;
            }
            else if (loadedConfig.LightPenaltyMultiplier > 1f)
            {
                loadedConfig.LightPenaltyMultiplier = 1f;
                changed = true;
            }

            // Extreme temperature bonuses may be intentional for modpacks, so only warn.
            if (loadedConfig.TemperatureBonus < 0f || loadedConfig.TemperatureBonus > 20f)
            {
                api.Logger.Warning("[IceCellar] coolingmodconfig.json has an unusual TemperatureBonus value: {0}", loadedConfig.TemperatureBonus);
            }

            if (!changed) return loadedConfig;

            api.Logger.Warning("[IceCellar] Updated coolingmodconfig.json with sanitized or newly added values and rewrote the file.");

            try
            {
                api.StoreModConfig(loadedConfig, configFileName);
            }
            catch (Exception saveEx)
            {
                api.Logger.Error("[IceCellar] Failed to write sanitized coolingmodconfig.json. Exception: {0}", saveEx);
            }

            return loadedConfig;
        }

        bool ConfigIsMissingFields(string configFileName, params string[] fieldNames)
        {
            try
            {
                string modConfigPath = api.GetOrCreateDataPath("ModConfig");
                string configPath = Path.Combine(modConfigPath, configFileName);
                if (!File.Exists(configPath)) return false;

                string rawConfig = File.ReadAllText(configPath);
                foreach (string fieldName in fieldNames)
                {
                    if (rawConfig.IndexOf($"\"{fieldName}\"", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[IceCellar] Failed checking coolingmodconfig.json for missing fields. Exception: {0}", ex);
            }

            return false;
        }

        void BackupBrokenConfig(string configFileName)
        {
            try
            {
                string modConfigPath = api.GetOrCreateDataPath("ModConfig");
                string configPath = Path.Combine(modConfigPath, configFileName);

                if (!File.Exists(configPath)) return;

                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string backupPath = Path.Combine(modConfigPath, $"{configFileName}.broken-{timestamp}");

                File.Move(configPath, backupPath);
                api.Logger.Warning("[IceCellar] Backed up invalid config to {0}", backupPath);
            }
            catch (Exception backupEx)
            {
                api.Logger.Error("[IceCellar] Failed to back up invalid coolingmodconfig.json. Exception: {0}", backupEx);
            }
        }

        // Surface-only scan with early exit for performance.
        // A room qualifies when enough cooling-wall blocks are from the patched
        // materials this mod recognizes.
        bool ComputeIsIceCellar(Room room, BlockPos pos, IWorldAccessor world)
        {
            int iceWalls = 0;
            int coolingWalls = room.CoolingWallCount;
            if (coolingWalls == 0) return false;

            float threshold = coolingWalls * config.IceRatioThreshold;

            var blockAccessor = world.BlockAccessor;
            var scanPos = new BlockPos(0, 0, 0, pos.dimension);

            int x1 = room.Location.Start.X;
            int y1 = room.Location.Start.Y;
            int z1 = room.Location.Start.Z;

            int x2 = room.Location.End.X;
            int y2 = room.Location.End.Y;
            int z2 = room.Location.End.Z;

            // Floor & Ceiling
            for (int x = x1; x <= x2; x++)
            for (int z = z1; z <= z2; z++)
            {
                scanPos.Set(x, y1, z);
                var block = blockAccessor.GetBlock(scanPos);
                if (IsIceCoolingBlock(block))
                    if (++iceWalls >= threshold) return true;

                if (y2 != y1)
                {
                    scanPos.Set(x, y2, z);
                    block = blockAccessor.GetBlock(scanPos);
                    if (IsIceCoolingBlock(block))
                        if (++iceWalls >= threshold) return true;
                }
            }

            // North & South
            for (int x = x1; x <= x2; x++)
            for (int y = y1 + 1; y < y2; y++)
            {
                scanPos.Set(x, y, z1);
                var block = blockAccessor.GetBlock(scanPos);
                if (IsIceCoolingBlock(block))
                    if (++iceWalls >= threshold) return true;

                if (z2 != z1)
                {
                    scanPos.Set(x, y, z2);
                    block = blockAccessor.GetBlock(scanPos);
                    if (IsIceCoolingBlock(block))
                        if (++iceWalls >= threshold) return true;
                }
            }

            // East & West
            for (int z = z1 + 1; z < z2; z++)
            for (int y = y1 + 1; y < y2; y++)
            {
                scanPos.Set(x1, y, z);
                var block = blockAccessor.GetBlock(scanPos);
                if (IsIceCoolingBlock(block))
                    if (++iceWalls >= threshold) return true;

                if (x2 != x1)
                {
                    scanPos.Set(x2, y, z);
                    block = blockAccessor.GetBlock(scanPos);
                    if (IsIceCoolingBlock(block))
                        if (++iceWalls >= threshold) return true;
                }
            }

            return false;
        }

        // A block counts if it has the custom cooling behavior ONLY.
        public static bool IsIceCoolingBlock(Block? block)
        {
            if (block == null) return false;

            if (block.BlockBehaviors != null)
            {
                foreach (var b in block.BlockBehaviors)
                {
                    if (b is BlockBehaviorIceCooling)
                        return true;
                }
            }

            return false;
        }
    }
}
