using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CropForecaster
{
    public class PumpkinForecastSystem
    {
        // These states only describe the mother plant getting far enough for vine growth to matter.
        // They do not try to promise anything about final fruit outcome.
        public enum TemperatureOutcome
        {
            NotApplicable,
            NoLongerProductive,
            SafeToVineStage,
            AlreadyEstablished,
            WillFreezeBeforeVines,
            WillBurnBeforeVines
        }

        public enum AdvisorySummary
        {
            NoCrop,
            NoLongerProductive,
            Viable,
            Established,
            Constrained
        }

        public struct AdvisoryReport
        {
            public AdvisorySummary Summary;
            public TemperatureOutcome Temperature;
            public bool LightSufficient;
            public bool HasVineSpace;
            public int AvailableVineSpaces;
            public double HoursUntilVineStage;
            public float LowestTempExperienced;
            public float HighestTempExperienced;
            public float ExpectedFailureHour;
        }

        private const int DefaultVineGrowthStage = 3;
        // Vanilla keeps the vine growth stage tucked away in PumpkinCropBehavior,
        // so we peek at it once here instead of hard-coding assumptions everywhere.
        private static readonly FieldInfo? VineGrowthStageField = typeof(PumpkinCropBehavior).GetField("vineGrowthStage", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsPumpkinCrop(BlockCrop crop)
        {
            return CropForecastSystem.IsPumpkinCrop(crop);
        }

        public static AdvisoryReport PredictNewPlantingAdvice(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland)
        {
            double firstStageHours = EstimateStageDurationHours(api, farmlandPos, crop, farmland);
            return PredictAdvice(api, farmlandPos, crop, farmland, 1, firstStageHours);
        }

        public static AdvisoryReport PredictCurrentCropAdvice(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland)
        {
            double currentTotalHours = api.World.Calendar.TotalHours;
            double hoursUntilNextStage = Math.Max(0, farmland.TotalHoursForNextStage - currentTotalHours);
            return PredictAdvice(api, farmlandPos, crop, farmland, crop.CurrentStage(), hoursUntilNextStage);
        }

        private static AdvisoryReport PredictAdvice(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland, int currentStage, double hoursUntilNextStage)
        {
            AdvisoryReport report = new AdvisoryReport
            {
                Summary = AdvisorySummary.NoCrop,
                LightSufficient = true,
                LowestTempExperienced = 999f,
                HighestTempExperienced = -999f
            };

            if (crop.CropProps == null || !IsPumpkinCrop(crop))
            {
                return report;
            }

            if (IsWitheredMotherPlant(crop, currentStage))
            {
                report.Temperature = TemperatureOutcome.NoLongerProductive;
                report.LightSufficient = false;
                report.HasVineSpace = false;
                report.Summary = AdvisorySummary.NoLongerProductive;
                report.HoursUntilVineStage = 0;
                return report;
            }

            // This is the pumpkin-specific part the normal crop forecaster cannot answer:
            // even if temperature is perfect, pumpkins still need room to spread.
            report.AvailableVineSpaces = CountPotentialVineSpaces(api, farmlandPos, out bool alreadySpread);
            report.HasVineSpace = alreadySpread || report.AvailableVineSpaces > 0;

            double lightGrowthSpeedFactor = CropForecastSystem.GetLightGrowthSpeedFactor(api, farmlandPos);
            report.LightSufficient = lightGrowthSpeedFactor > 0;

            int vineGrowthStage = GetVineGrowthStage(crop);
            int remainingTransitions = Math.Max(0, vineGrowthStage - currentStage);

            if (remainingTransitions == 0)
            {
                // Once the mother plant has already reached the vine-growth threshold,
                // we stop pretending we can forecast the whole pumpkin lifecycle exactly.
                report.Temperature = TemperatureOutcome.AlreadyEstablished;
                report.HoursUntilVineStage = 0;
            }
            else if (!report.LightSufficient)
            {
                report.Temperature = TemperatureOutcome.NotApplicable;
                report.HoursUntilVineStage = double.PositiveInfinity;
            }
            else
            {
                report = SimulateMotherPlantToVineStage(api, farmlandPos, crop, farmland, currentStage, hoursUntilNextStage, remainingTransitions, report, lightGrowthSpeedFactor);
            }

            bool constrained = !report.LightSufficient
                || !report.HasVineSpace
                || report.Temperature == TemperatureOutcome.WillFreezeBeforeVines
                || report.Temperature == TemperatureOutcome.WillBurnBeforeVines;

            report.Summary = report.Temperature == TemperatureOutcome.AlreadyEstablished && report.LightSufficient && report.HasVineSpace
                ? AdvisorySummary.Established
                : constrained ? AdvisorySummary.Constrained : AdvisorySummary.Viable;

            return report;
        }

        private static bool IsWitheredMotherPlant(BlockCrop crop, int currentStage)
        {
            return crop.CropProps != null && currentStage >= crop.CropProps.GrowthStages;
        }

        private static AdvisoryReport SimulateMotherPlantToVineStage(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland, int currentStage, double hoursUntilNextStage, int remainingTransitions, AdvisoryReport report, double lightGrowthSpeedFactor)
        {
            double defaultStageDuration = CropForecastSystem.GetBaseStageDurationHours(api, crop, farmland)
                / lightGrowthSpeedFactor
                / CropForecastSystem.GetConfiguredCropGrowthRateMultiplier(api);
            double futureHourOffset = Math.Max(0, hoursUntilNextStage);
            double simulatedGrowthHours = futureHourOffset + Math.Max(0, remainingTransitions - 1) * defaultStageDuration;

            report.HoursUntilVineStage = simulatedGrowthHours;
            bool hasGreenhouseBonus = CropForecastSystem.HasGreenhouseBonus(farmland);
            CropForecastSystem.TemperatureSimulationReport temperatureReport = CropForecastSystem.SimulateTemperatureWindow(
                api,
                farmlandPos,
                crop,
                hasGreenhouseBonus,
                futureHourOffset,
                simulatedGrowthHours
            );

            report.LowestTempExperienced = temperatureReport.LowestTempExperienced;
            report.HighestTempExperienced = temperatureReport.HighestTempExperienced;
            report.ExpectedFailureHour = temperatureReport.ExpectedFailureHour;

            if (temperatureReport.Failure == CropForecastSystem.TemperatureFailure.Freeze)
            {
                report.Temperature = TemperatureOutcome.WillFreezeBeforeVines;
                return report;
            }

            if (temperatureReport.Failure == CropForecastSystem.TemperatureFailure.Burn)
            {
                report.Temperature = TemperatureOutcome.WillBurnBeforeVines;
                return report;
            }

            report.Temperature = TemperatureOutcome.SafeToVineStage;
            return report;
        }

        private static double EstimateStageDurationHours(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland)
        {
            return CropForecastSystem.GetLightAdjustedStageDurationHours(api, farmlandPos, crop, farmland);
        }

        private static int GetVineGrowthStage(BlockCrop crop)
        {
            if (crop.CropProps?.Behaviors == null) return DefaultVineGrowthStage;

            foreach (var behavior in crop.CropProps.Behaviors)
            {
                // If vanilla changes this internals later, we still fall back to the default stage.
                if (behavior is PumpkinCropBehavior pumpkinBehavior && VineGrowthStageField?.GetValue(pumpkinBehavior) is int vineGrowthStage)
                {
                    return vineGrowthStage;
                }
            }

            return DefaultVineGrowthStage;
        }

        private static int CountPotentialVineSpaces(ICoreAPI api, BlockPos farmlandPos, out bool alreadySpread)
        {
            alreadySpread = false;
            int viableSpaces = 0;
            BlockPos motherPlantPos = farmlandPos.UpCopy();

            foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
            {
                BlockPos candidatePos = motherPlantPos.AddCopy(facing);
                Block block = api.World.BlockAccessor.GetBlock(candidatePos);
                string blockCode = block?.Code?.Path ?? string.Empty;

                // If vines or fruit are already out there, space is no longer the real blocker.
                // The plant has already crossed the "can spread" threshold.
                if (IsProductivePumpkinSpreadBlock(blockCode))
                {
                    alreadySpread = true;
                }

                if (!IsReplaceableForPumpkin(block)) continue;
                if (!PumpkinCropBehavior.CanSupportPumpkin(api, candidatePos.DownCopy())) continue;

                viableSpaces++;
            }

            return viableSpaces;
        }

        private static bool IsReplaceableForPumpkin(Block? block)
        {
            if (block == null) return true;
            string blockPath = block.Code?.Path ?? string.Empty;
            // Match vanilla's general idea here: pumpkins should not treat occupied pumpkin blocks
            // as open space, and random solid blocks obviously should not count either.
            return block.Replaceable >= 6000 && !blockPath.Contains("pumpkin");
        }

        private static bool IsProductivePumpkinSpreadBlock(string blockCode)
        {
            if (string.IsNullOrEmpty(blockCode)) return false;
            if (blockCode.StartsWith("pumpkin-fruit")) return true;
            if (!blockCode.StartsWith("pumpkin-vine")) return false;
            return !blockCode.EndsWith("-withered");
        }
    }
}
