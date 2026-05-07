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
        public string[] CoolingBlockCodes = IceCellarConfigDefaults.CoolingBlockCodes;
        public string[] CoolingBlockCodePatterns = IceCellarConfigDefaults.CoolingBlockCodePatterns;
    }

    public static class IceCellarConfigDefaults
    {
        public static readonly string[] CoolingBlockCodes =
        {
            "iceiscellar:icebricks",
            "game:packedglacierice"
        };

        public static readonly string[] CoolingBlockCodePatterns =
        {
            "game:agedwallpaperplanks-*",
            "game:log-*",
            "game:carvedlog-*",
            "game:debarkedlog-*",
            "game:logquad-placed-*",
            "game:logquad-debarked-*",
            "game:logquad-barkedcorner-*",
            "game:logquad-debarkedcorner-*",
            "game:logsection-*",
            "game:planks-*"
        };
    }

    // Room objects can be rebuilt by the game, but the bounds are the part we
    // actually care about. A record struct keeps the key small without the old
    // hand-written equality/hash boilerplate.
    readonly record struct RoomKey(
        int X1,
        int Y1,
        int Z1,
        int X2,
        int Y2,
        int Z2)
    {
        public RoomKey(Room room) : this(
            room.Location.Start.X,
            room.Location.Start.Y,
            room.Location.Start.Z,
            room.Location.End.X,
            room.Location.End.Y,
            room.Location.End.Z)
        { }
    }

    public class IceCellarModSystem : ModSystem
    {
        const string ConfigFileName = "iceiscellarmodconfig.json";
        const string LegacyConfigFileName = "coolingmodconfig.json";

        ICoreAPI api = null!;
        IceCellarConfig config = null!;

        // Cache only the final yes/no result. We do not need to keep the whole
        // room entry around now that block changes simply clear the cache.
        readonly Dictionary<RoomKey, bool> iceRoomCache = new();

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            api.RegisterBlockBehaviorClass("IceCooling", typeof(BlockBehaviorIceCooling));
            config = LoadOrCreateConfig();
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            AttachCoolingBehaviorsFromConfig(api);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            // A place/break can change room shape, cooling walls, or the ice
            // ratio. Clearing the whole cache is simpler and cheap compared to
            // trying to surgically find every affected room.
            sapi.Event.DidBreakBlock += (byPlayer, oldBlockId, blockSel)
                => iceRoomCache.Clear();
            sapi.Event.DidPlaceBlock += (byPlayer, oldblockId, blockSel, withItemStack)
                => iceRoomCache.Clear();
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

            if (!iceRoomCache.TryGetValue(roomKey, out bool isIceCellar))
            {
                isIceCellar = ComputeIsIceCellar(room, pos, world);
                iceRoomCache[roomKey] = isIceCellar;
            }

            if (!isIceCellar) return null;

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
            try
            {
                IceCellarConfig? loadedConfig = api.LoadModConfig<IceCellarConfig>(ConfigFileName);
                if (loadedConfig != null)
                {
                    bool configMissingFields = ConfigIsMissingFields(
                        ConfigFileName,
                        nameof(IceCellarConfig.LightPenaltyMultiplier),
                        nameof(IceCellarConfig.CoolingBlockCodes),
                        nameof(IceCellarConfig.CoolingBlockCodePatterns));

                    return SanitizeConfig(loadedConfig, ConfigFileName, configMissingFields);
                }

                IceCellarConfig? legacyConfig = null;

                try
                {
                    legacyConfig = api.LoadModConfig<IceCellarConfig>(LegacyConfigFileName);
                }
                catch (Exception legacyEx)
                {
                    api.Logger.Warning(
                        "[IceCellar] Failed to load legacy config {0}, backing it up and generating {1}. Exception: {2}",
                        LegacyConfigFileName,
                        ConfigFileName,
                        legacyEx);

                    BackupBrokenConfig(LegacyConfigFileName);
                }

                if (legacyConfig != null)
                {
                    api.Logger.Notification(
                        "[IceCellar] Migrating legacy config {0} to {1}.",
                        LegacyConfigFileName,
                        ConfigFileName);

                    return SanitizeConfig(legacyConfig, ConfigFileName, true);
                }

                var defaultConfig = new IceCellarConfig();
                api.StoreModConfig(defaultConfig, ConfigFileName);
                return defaultConfig;
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[IceCellar] Failed to load {0}, backing it up and regenerating defaults. Exception: {1}", ConfigFileName, ex);

                BackupBrokenConfig(ConfigFileName);

                var defaultConfig = new IceCellarConfig();

                try
                {
                    api.StoreModConfig(defaultConfig, ConfigFileName);
                }
                catch (Exception saveEx)
                {
                    api.Logger.Error("[IceCellar] Failed to write default {0} after load failure. Exception: {1}", ConfigFileName, saveEx);
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

            if (loadedConfig.CoolingBlockCodes == null)
            {
                loadedConfig.CoolingBlockCodes = IceCellarConfigDefaults.CoolingBlockCodes;
                changed = true;
            }

            if (loadedConfig.CoolingBlockCodePatterns == null)
            {
                loadedConfig.CoolingBlockCodePatterns = IceCellarConfigDefaults.CoolingBlockCodePatterns;
                changed = true;
            }

            // Extreme temperature bonuses may be intentional for modpacks, so only warn.
            if (loadedConfig.TemperatureBonus < 0f || loadedConfig.TemperatureBonus > 20f)
            {
                api.Logger.Warning("[IceCellar] {0} has an unusual TemperatureBonus value: {1}", configFileName, loadedConfig.TemperatureBonus);
            }

            if (!changed) return loadedConfig;

            api.Logger.Warning("[IceCellar] Updated {0} with sanitized or newly added values and rewrote the file.", configFileName);

            try
            {
                api.StoreModConfig(loadedConfig, configFileName);
            }
            catch (Exception saveEx)
            {
                api.Logger.Error("[IceCellar] Failed to write sanitized {0}. Exception: {1}", configFileName, saveEx);
            }

            return loadedConfig;
        }

        void AttachCoolingBehaviorsFromConfig(ICoreAPI api)
        {
            var matchedBlocks = new HashSet<Block>();
            int added = 0;

            foreach (string codeText in config.CoolingBlockCodes ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(codeText)) continue;

                Block? block = api.World.GetBlock(new AssetLocation(codeText));
                if (block == null || block.Id == 0)
                {
                    api.Logger.Warning("[IceCellar] CoolingBlockCodes entry did not resolve to a block: {0}", codeText);
                    continue;
                }

                matchedBlocks.Add(block);
            }

            foreach (string pattern in config.CoolingBlockCodePatterns ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;

                // Let Vintage Story resolve wildcards. That keeps our config
                // patterns aligned with the game's own block-code matching
                // rules and avoids maintaining a second matcher here.
                Block[] patternBlocks = api.World.SearchBlocks(new AssetLocation(pattern));
                foreach (Block block in patternBlocks)
                {
                    if (block == null || block.Id == 0) continue;
                    matchedBlocks.Add(block);
                }

                if (patternBlocks.Length == 0)
                {
                    api.Logger.Warning("[IceCellar] CoolingBlockCodePatterns entry matched no blocks: {0}", pattern);
                }
            }

            foreach (Block block in matchedBlocks)
            {
                if (HasIceCoolingBehavior(block)) continue;

                BlockBehavior[] behaviors = block.BlockBehaviors ?? Array.Empty<BlockBehavior>();
                Array.Resize(ref behaviors, behaviors.Length + 1);
                behaviors[behaviors.Length - 1] = new BlockBehaviorIceCooling(block);
                block.BlockBehaviors = behaviors;

                added++;
            }

            api.Logger.Notification("[IceCellar] Added IceCooling behavior to {0} configured blocks.", added);
        }

        static bool HasIceCoolingBehavior(Block block)
        {
            // Use the engine's behavior lookup instead of walking the behavior
            // array ourselves. It is shorter and respects inherited behavior
            // types if this ever grows a derived cooling behavior.
            return block.GetBehavior(typeof(BlockBehaviorIceCooling), true) != null;
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
                api.Logger.Warning("[IceCellar] Failed checking {0} for missing fields. Exception: {1}", configFileName, ex);
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
                api.Logger.Error("[IceCellar] Failed to back up invalid {0}. Exception: {1}", configFileName, backupEx);
            }
        }

        // Surface-only scan with early exit for performance.
        // A room qualifies when enough cooling-wall blocks are from the configured
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
            // Same lookup as attachment uses, so the room scan and the behavior
            // injection path agree on what "has IceCooling" means.
            return block?.GetBehavior(typeof(BlockBehaviorIceCooling), true) != null;
        }
    }
}
