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

            bool prePlantPreview = false;
            bool unsupportedSeedPreview = false;
            if (crop == null)
            {
                if (!farmland.CanPlant()) return "";

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

            if (PumpkinForecastSystem.IsPumpkinCrop(crop))
            {
                PumpkinForecastSystem.AdvisoryReport pumpkinReport = prePlantPreview
                    ? PumpkinForecastSystem.PredictNewPlantingAdvice(world.Api, pos, crop, farmland)
                    : PumpkinForecastSystem.PredictCurrentCropAdvice(world.Api, pos, crop, farmland);

                return BuildPumpkinInfo(pumpkinReport, prePlantPreview, world.Api.World.Calendar.HoursPerDay);
            }

            CropForecastSystem.ForecastReport report = prePlantPreview
                ? CropForecastSystem.PredictNewPlantingViability(world.Api, pos, crop, farmland)
                : CropForecastSystem.PredictCurrentCropViability(world.Api, pos, crop, farmland);

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
                        ? "<font color=\"#66ff66\">Forecast: Viable to sow now</font>"
                        : "<font color=\"#66ff66\">Forecast: Expected to survive to harvest</font>");
                    break;

                case CropForecastSystem.ForecastResult.WillFreezeToDeath:
                    int daysToFreeze = GetDayEstimate(world.Api.World.Calendar.HoursPerDay, report.ExpectedDeathHour);
                    sb.AppendLine(prePlantPreview
                        ? $"<font color=\"#66ccff\">Warning: If sown now, will freeze in ~{daysToFreeze} days</font>"
                        : $"<font color=\"#66ccff\">Warning: Will freeze in ~{daysToFreeze} days</font>");
                    break;

                case CropForecastSystem.ForecastResult.WillBurnToDeath:
                    int daysToBurn = GetDayEstimate(world.Api.World.Calendar.HoursPerDay, report.ExpectedDeathHour);
                    sb.AppendLine(prePlantPreview
                        ? $"<font color=\"#ff6666\">Warning: If sown now, will burn in ~{daysToBurn} days</font>"
                        : $"<font color=\"#ff6666\">Warning: Will burn in ~{daysToBurn} days</font>");
                    break;

                case CropForecastSystem.ForecastResult.SprawlingCropNotSupported:
                    sb.AppendLine("<font color=\"#d8c18a\">Forecast: Not supported for sprawling crops</font>");
                    break;

                case CropForecastSystem.ForecastResult.InsufficientLight:
                    sb.AppendLine(prePlantPreview
                        ? "<font color=\"#ffcc66\">Warning: If sown now, sunlight is insufficient</font>"
                        : "<font color=\"#ffcc66\">Warning: Insufficient sunlight to mature reliably</font>");
                    break;

                case CropForecastSystem.ForecastResult.UnsupportedCrop:
                    sb.AppendLine(UnsupportedCropMessage);
                    break;
            }

            return sb.ToString();
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
    }
}
