using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CropForecaster
{
    public class BlockBehaviorCropForecast : BlockBehavior
    {
        private const string UnsupportedCropMessage = "<font color=\"#d8c18a\">Forecast: Crop type not yet supported</font>";

        private readonly struct SeedPreviewResolution
        {
            public SeedPreviewResolution(BlockCrop? crop, bool unsupported)
            {
                Crop = crop;
                Unsupported = unsupported;
            }

            public BlockCrop? Crop { get; }
            public bool Unsupported { get; }
        }

        public BlockBehaviorCropForecast(Block block) : base(block)
        {
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            BlockEntityFarmland? farmland = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityFarmland;
            BlockCrop? crop = world.BlockAccessor.GetBlock(pos.UpCopy()) as BlockCrop;

            if (farmland == null) return "";
            if (CropForecastClientState.Mode == CropForecastMode.Off) return "";

            bool prePlantPreview = false;
            bool unsupportedSeedPreview = false;
            if (crop == null)
            {
                if (!farmland.CanPlant()) return "";

                // Pre-sow preview can only infer a baseline crop from the held seed.
                // Some mods mutate the planted crop later, so we label this path accordingly.
                SeedPreviewResolution resolution = ResolveCropFromHeldSeed(world, forPlayer);
                crop = resolution.Crop;
                unsupportedSeedPreview = resolution.Unsupported;
                prePlantPreview = crop != null || unsupportedSeedPreview;
            }

            if (crop == null)
            {
                return unsupportedSeedPreview ? BuildNeutralInfo(UnsupportedCropMessage) : "";
            }

            if (!CropForecastSystem.SupportsStandardForecast(crop) && !PumpkinForecastSystem.IsPumpkinCrop(crop))
            {
                return BuildNeutralInfo(UnsupportedCropMessage);
            }

            TooltipCacheKey cacheKey = BuildTooltipCacheKey(world, pos, crop, farmland, forPlayer, prePlantPreview, unsupportedSeedPreview);
            if (CropForecastClientState.TryGetCachedTooltip(cacheKey, out string cachedTooltip))
            {
                return cachedTooltip;
            }

            if (PumpkinForecastSystem.IsPumpkinCrop(crop))
            {
                PumpkinForecastSystem.AdvisoryReport pumpkinReport = prePlantPreview
                    ? PumpkinForecastSystem.PredictNewPlantingAdvice(world.Api, pos, crop, farmland)
                    : PumpkinForecastSystem.PredictCurrentCropAdvice(world.Api, pos, crop, farmland);

                string pumpkinInfo = BuildPumpkinInfo(pumpkinReport, prePlantPreview, world.Api.World.Calendar.HoursPerDay);
                CropForecastClientState.StoreCachedTooltip(cacheKey, pumpkinInfo);
                return pumpkinInfo;
            }

            CropForecastSystem.ForecastReport report = prePlantPreview
                ? CropForecastSystem.PredictNewPlantingViability(world.Api, pos, crop, farmland)
                : CropForecastSystem.PredictCurrentCropViability(world.Api, pos, crop, farmland);
            bool includeProbabilisticForecast = CropForecastClientState.Mode == CropForecastMode.Full;
            CropForecastSystem.ProbabilisticForecastReport probabilisticReport = default;
            if (includeProbabilisticForecast)
            {
                probabilisticReport = prePlantPreview
                    ? CropForecastSystem.PredictNewPlantingViabilityProbabilistic(world.Api, pos, crop, farmland, CropForecastClientState.ProbabilisticSimulationCount)
                    : CropForecastSystem.PredictCurrentCropViabilityProbabilistic(world.Api, pos, crop, farmland, CropForecastClientState.ProbabilisticSimulationCount);
            }

            if (report.Result == CropForecastSystem.ForecastResult.NoCrop) return "";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            
            switch (report.Result)
            {
                case CropForecastSystem.ForecastResult.ReadyToHarvest:
                    sb.AppendLine("<font color=\"#66ff66\">Forecast: Ready to harvest</font>");
                    break;

                case CropForecastSystem.ForecastResult.WillSurvive:
                    sb.AppendLine(prePlantPreview
                        ? "<font color=\"#66ff66\">Base seed forecast: Viable to sow now</font>"
                        : "<font color=\"#66ff66\">Forecast: Expected to survive to harvest</font>");
                    break;

                case CropForecastSystem.ForecastResult.WillFreezeToDeath:
                    int daysToFreeze = GetDayEstimate(world.Api.World.Calendar.HoursPerDay, report.ExpectedDeathHour);
                    sb.AppendLine(prePlantPreview
                        ? $"<font color=\"#66ccff\">Base seed forecast: If sown now, may freeze in ~{daysToFreeze} days</font>"
                        : $"<font color=\"#66ccff\">Warning: Will freeze in ~{daysToFreeze} days</font>");
                    break;

                case CropForecastSystem.ForecastResult.WillBurnToDeath:
                    int daysToBurn = GetDayEstimate(world.Api.World.Calendar.HoursPerDay, report.ExpectedDeathHour);
                    sb.AppendLine(prePlantPreview
                        ? $"<font color=\"#ff6666\">Base seed forecast: If sown now, may burn in ~{daysToBurn} days</font>"
                        : $"<font color=\"#ff6666\">Warning: Will burn in ~{daysToBurn} days</font>");
                    break;

                case CropForecastSystem.ForecastResult.SprawlingCropNotSupported:
                    sb.AppendLine("<font color=\"#d8c18a\">Forecast: Not supported for sprawling crops</font>");
                    break;

                case CropForecastSystem.ForecastResult.InsufficientLight:
                    sb.AppendLine(prePlantPreview
                        ? "<font color=\"#ffcc66\">Base seed forecast: Sunlight looks insufficient</font>"
                        : "<font color=\"#ffcc66\">Warning: Insufficient sunlight to mature reliably</font>");
                    break;

                case CropForecastSystem.ForecastResult.UnsupportedCrop:
                    sb.AppendLine(UnsupportedCropMessage);
                    break;
            }

            AppendClimateStressInfo(sb, report);

            if (includeProbabilisticForecast)
            {
                AppendProbabilisticForecastInfo(sb, probabilisticReport, world.Api.World.Calendar.HoursPerDay, prePlantPreview);
            }

            string info = sb.ToString();
            CropForecastClientState.StoreCachedTooltip(cacheKey, info);
            return info;
        }

        private static string BuildPumpkinInfo(PumpkinForecastSystem.AdvisoryReport report, bool prePlantPreview, float hoursPerDay)
        {
            if (report.Summary == PumpkinForecastSystem.AdvisorySummary.NoCrop) return "";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();

            switch (report.Summary)
            {
                case PumpkinForecastSystem.AdvisorySummary.NoLongerProductive:
                    sb.AppendLine("<font color=\"#c9a36b\">Pumpkin forecast: No longer productive</font>");
                    break;

                case PumpkinForecastSystem.AdvisorySummary.Viable:
                    sb.AppendLine(prePlantPreview
                        ? "<font color=\"#66ff66\">Pumpkin forecast: Viable to sow here</font>"
                        : "<font color=\"#66ff66\">Pumpkin forecast: Conditions look viable</font>");
                    break;

                case PumpkinForecastSystem.AdvisorySummary.Established:
                    sb.AppendLine("<font color=\"#66ff66\">Pumpkin forecast: Mother plant established</font>");
                    break;

                case PumpkinForecastSystem.AdvisorySummary.Constrained:
                    sb.AppendLine(prePlantPreview
                        ? "<font color=\"#d8c18a\">Pumpkin forecast: Advise before sowing</font>"
                        : "<font color=\"#d8c18a\">Pumpkin forecast: Advise for this plant</font>");
                    break;
            }

            switch (report.Temperature)
            {
                case PumpkinForecastSystem.TemperatureOutcome.NoLongerProductive:
                    sb.AppendLine("<font color=\"#c9a36b\">Mother plant: Withered and will not produce further</font>");
                    break;

                case PumpkinForecastSystem.TemperatureOutcome.SafeToVineStage:
                    sb.AppendLine("<font color=\"#66ff66\">Mother plant: Likely survives to vine stage</font>");
                    break;

                case PumpkinForecastSystem.TemperatureOutcome.AlreadyEstablished:
                    sb.AppendLine("<font color=\"#66ff66\">Mother plant: Already established for vine growth</font>");
                    break;

                case PumpkinForecastSystem.TemperatureOutcome.WillFreezeBeforeVines:
                    int daysToFreeze = GetDayEstimate(hoursPerDay, report.ExpectedFailureHour);
                    sb.AppendLine($"<font color=\"#66ccff\">Mother plant: Likely freezes before vines (~{daysToFreeze} days)</font>");
                    break;

                case PumpkinForecastSystem.TemperatureOutcome.WillBurnBeforeVines:
                    int daysToBurn = GetDayEstimate(hoursPerDay, report.ExpectedFailureHour);
                    sb.AppendLine($"<font color=\"#ff6666\">Mother plant: Likely burns before vines (~{daysToBurn} days)</font>");
                    break;
            }

            if (report.Summary != PumpkinForecastSystem.AdvisorySummary.NoLongerProductive)
            {
                sb.AppendLine(report.LightSufficient
                    ? "<font color=\"#66ff66\">Sunlight: Sufficient for pumpkin growth</font>"
                    : "<font color=\"#ffcc66\">Sunlight: Insufficient for reliable pumpkin growth</font>");

                sb.AppendLine(report.HasVineSpace
                    ? $"<font color=\"#66ff66\">Vines: Adjacent spread space available ({report.AvailableVineSpaces} open)</font>"
                    : "<font color=\"#ffcc66\">Vines: No adjacent room for pumpkin spread</font>");
            }

            sb.AppendLine("<font color=\"#d8c18a\">Note: Pumpkin fruiting remains partly random</font>");
            return sb.ToString();
        }

        private static string BuildNeutralInfo(string message)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(message);
            return sb.ToString();
        }

        private static int GetDayEstimate(float hoursPerDay, float forecastHours)
        {
            float safeHoursPerDay = Math.Max(1f, hoursPerDay);
            return (int)(forecastHours / safeHoursPerDay);
        }

        private static void AppendClimateStressInfo(StringBuilder sb, CropForecastSystem.ForecastReport report)
        {
            if (!CropForecastSystem.HasClimateStress(report))
            {
                return;
            }

            // We warn about yield risk without trying to mirror the exact vanilla harvest multiplier.
            sb.AppendLine(CropForecastSystem.HasSevereClimateStress(report)
                ? "<font color=\"#ffcc66\">Climate warning: Severe stress may reduce yield before harvest</font>"
                : "<font color=\"#d8c18a\">Climate warning: Expected reduced yield due to harsh climate</font>");
        }

        private static void AppendProbabilisticForecastInfo(StringBuilder sb, CropForecastSystem.ProbabilisticForecastReport report, float hoursPerDay, bool prePlantPreview)
        {
            if (report.MostLikelyResult is CropForecastSystem.ForecastResult.UnsupportedCrop
                or CropForecastSystem.ForecastResult.SprawlingCropNotSupported
                or CropForecastSystem.ForecastResult.InsufficientLight
                or CropForecastSystem.ForecastResult.NoCrop)
            {
                return;
            }

            int survivePercent = ToPercent(report.SurvivalProbability);
            int freezePercent = ToPercent(report.FreezeProbability);
            int burnPercent = ToPercent(report.BurnProbability);
            string prefix = prePlantPreview ? "Base seed forecast: " : "";

            sb.AppendLine($"<font color=\"#66ff66\">{prefix}Chance to mature: {survivePercent}%</font>");
            sb.AppendLine($"<font color=\"#66ccff\">{prefix}Chance to freeze first: {freezePercent}%</font>");
            sb.AppendLine($"<font color=\"#ff6666\">{prefix}Chance to burn first: {burnPercent}%</font>");

            if (double.IsFinite(report.MedianGrowthHours))
            {
                sb.AppendLine($"<font color=\"#d8c18a\">{prefix}Median maturity time: {FormatDuration(report.MedianGrowthHours, hoursPerDay)}</font>");
            }

            if (double.IsFinite(report.P10GrowthHours) && double.IsFinite(report.P90GrowthHours))
            {
                sb.AppendLine($"<font color=\"#d8c18a\">{prefix}Likely growth window: {FormatDuration(report.P10GrowthHours, hoursPerDay)} - {FormatDuration(report.P90GrowthHours, hoursPerDay)}</font>");
            }

            if (prePlantPreview)
            {
                // This makes the tradeoff explicit: the seed preview is useful, but the planted crop
                // is the only place where we can read the real post-sowing state.
                sb.AppendLine("<font color=\"#d8c18a\">Plant one seed for the most accurate forecast after sowing</font>");
            }
        }

        private static int ToPercent(double probability)
        {
            return (int)Math.Round(Math.Clamp(probability, 0, 1) * 100);
        }

        private static string FormatDuration(double hours, float hoursPerDay)
        {
            double safeHoursPerDay = Math.Max(1f, hoursPerDay);
            double days = hours / safeHoursPerDay;

            if (days >= 1)
            {
                return $"{days:0.0} days";
            }

            return $"{hours:0.#} h";
        }

        private SeedPreviewResolution ResolveCropFromHeldSeed(IWorldAccessor world, IPlayer forPlayer)
        {
            ItemSlot? heldSlot = forPlayer?.InventoryManager?.ActiveHotbarSlot;
            CollectibleObject? collectible = heldSlot?.Itemstack?.Collectible;
            if (collectible is not ItemPlantableSeed) return new SeedPreviewResolution(null, false);

            string? cropType = collectible.Variant?["type"];
            if (string.IsNullOrEmpty(cropType)) return new SeedPreviewResolution(null, false);

            Block? crop = world.GetBlock(collectible.CodeWithPath("crop-" + cropType + "-1"));
            if (crop is not BlockCrop blockCrop) return new SeedPreviewResolution(null, true);

            if (PumpkinForecastSystem.IsPumpkinCrop(blockCrop) || CropForecastSystem.SupportsStandardForecast(blockCrop))
            {
                return new SeedPreviewResolution(blockCrop, false);
            }

            return new SeedPreviewResolution(null, true);
        }

        private static TooltipCacheKey BuildTooltipCacheKey(IWorldAccessor world, BlockPos pos, BlockCrop? crop, BlockEntityFarmland farmland, IPlayer forPlayer, bool prePlantPreview, bool unsupportedSeedPreview)
        {
            double currentTotalHours = world.Api.World.Calendar.TotalHours;
            int cropStage = crop?.CurrentStage() ?? 0;
            int hoursUntilNextStageTenths = (int)Math.Round(Math.Max(0, farmland.TotalHoursForNextStage - currentTotalHours) * 10);
            int worldHourBucket = (int)Math.Floor(currentTotalHours);
            string heldItemCode = GetHeldItemCodePath(forPlayer);

            return new TooltipCacheKey(
                pos.X,
                pos.Y,
                pos.Z,
                crop?.Id ?? 0,
                cropStage,
                prePlantPreview,
                unsupportedSeedPreview,
                CropForecastClientState.Mode,
                hoursUntilNextStageTenths,
                worldHourBucket,
                heldItemCode
            );
        }

        private static string GetHeldItemCodePath(IPlayer forPlayer)
        {
            return forPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString() ?? string.Empty;
        }
    }
}
