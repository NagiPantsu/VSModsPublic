using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CropForecaster
{
    public class CropForecastSystem
    {
        internal enum TemperatureFailure
        {
            None,
            Freeze,
            Burn
        }

        internal struct TemperatureSimulationReport
        {
            public TemperatureFailure Failure;
            public float LowestTempExperienced;
            public float HighestTempExperienced;
            public float ExpectedFailureHour;
        }

        public enum ForecastResult
        {
            NoCrop,
            ReadyToHarvest,
            WillSurvive,
            WillFreezeToDeath,
            WillBurnToDeath,
            InsufficientLight,
            UnsupportedCrop,
            SprawlingCropNotSupported
        }

        public struct ForecastReport
        {
            public ForecastResult Result;
            public double EstimatedGrowthHours;
            public float LowestTempExperienced;
            public float HighestTempExperienced;
            public float ExpectedDeathHour;
        }

        internal static bool IsPumpkinCrop(BlockCrop crop)
        {
            if (crop == null) return false;

            string cropPath = crop.Code?.Path ?? string.Empty;
            if (cropPath.StartsWith("crop-pumpkin")) return true;

            string? cropType = crop.Variant?["type"];
            if (cropType == "pumpkin") return true;

            return crop.CropProps?.Behaviors?.Any(behavior => behavior.GetType().Name.Contains("Pumpkin")) == true;
        }

        internal static bool SupportsStandardForecast(BlockCrop crop)
        {
            return crop?.CropProps != null && !IsPumpkinCrop(crop);
        }

        /// Predicts whether a crop will survive from a supplied stage/timing state to maturity.

        public static ForecastReport PredictCropViability(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland, int currentStage, double hoursUntilNextStage)
        {
            ForecastReport report = new ForecastReport()
            {
                LowestTempExperienced = 999f,
                HighestTempExperienced = -999f
            };

            if (crop.CropProps == null)
            {
                report.Result = ForecastResult.UnsupportedCrop;
                return report;
            }

            if (IsPumpkinCrop(crop)) return new ForecastReport { Result = ForecastResult.SprawlingCropNotSupported };

            int remainingStageTransitions = Math.Max(0, crop.CropProps.GrowthStages - currentStage);
            if (remainingStageTransitions == 0)
            {
                report.Result = ForecastResult.ReadyToHarvest;
                report.EstimatedGrowthHours = 0;
                return report;
            }

            double defaultStageDuration = GetLightAdjustedStageDurationHours(api, farmlandPos, crop, farmland);
            if (double.IsPositiveInfinity(defaultStageDuration))
            {
                report.Result = ForecastResult.InsufficientLight;
                report.EstimatedGrowthHours = double.PositiveInfinity;
                return report;
            }

            double currentTotalHours = api.World.Calendar.TotalHours;
            double futureHourOffset = Math.Max(0, hoursUntilNextStage);
            double simulatedGrowthHours = futureHourOffset + Math.Max(0, remainingStageTransitions - 1) * defaultStageDuration;
            report.EstimatedGrowthHours = simulatedGrowthHours;

            TemperatureSimulationReport temperatureReport = SimulateTemperatureWindow(api, farmlandPos, crop, farmland, futureHourOffset, simulatedGrowthHours);
            report.LowestTempExperienced = temperatureReport.LowestTempExperienced;
            report.HighestTempExperienced = temperatureReport.HighestTempExperienced;
            report.ExpectedDeathHour = temperatureReport.ExpectedFailureHour;

            if (temperatureReport.Failure == TemperatureFailure.Freeze)
            {
                report.Result = ForecastResult.WillFreezeToDeath;
                return report;
            }

            if (temperatureReport.Failure == TemperatureFailure.Burn)
            {
                report.Result = ForecastResult.WillBurnToDeath;
                return report;
            }

            report.Result = ForecastResult.WillSurvive;
            return report;
        }

        public static ForecastReport PredictCurrentCropViability(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland)
        {
            double currentTotalHours = api.World.Calendar.TotalHours;
            double hoursUntilNextStage = Math.Max(0, farmland.TotalHoursForNextStage - currentTotalHours);
            return PredictCropViability(api, farmlandPos, crop, farmland, crop.CurrentStage(), hoursUntilNextStage);
        }

        public static ForecastReport PredictNewPlantingViability(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland)
        {
            double firstStageHours = EstimateStageDurationHours(api, farmlandPos, crop, farmland);
            return PredictCropViability(api, farmlandPos, crop, farmland, 1, firstStageHours);
        }

        private static double EstimateStageDurationHours(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland)
        {
            if (crop.CropProps == null) return 0;
            return GetLightAdjustedStageDurationHours(api, farmlandPos, crop, farmland);
        }

        internal static double GetBaseStageDurationHours(ICoreAPI api, BlockCrop crop, BlockEntityFarmland farmland)
        {
            if (crop.CropProps == null) return 0;

            double totalDays = crop.CropProps.TotalGrowthDays;
            if (totalDays > 0)
            {
                totalDays = (totalDays / 12) * api.World.Calendar.DaysPerMonth;
            }
            else
            {
                totalDays = crop.CropProps.TotalGrowthMonths * api.World.Calendar.DaysPerMonth;
            }

            float stageHours = api.World.Calendar.HoursPerDay * (float)totalDays / Math.Max(1, crop.CropProps.GrowthStages - 1);
            float growthRate = Math.Max(0.01f, farmland.GetGrowthRate(crop.CropProps.RequiredNutrient));
            stageHours *= 1 / growthRate;
            return stageHours;
        }

        internal static double GetLightGrowthSpeedFactor(ICoreAPI api, BlockPos farmlandPos)
        {
            ModSystemFarming farmingSystem = api.ModLoader.GetModSystem<ModSystemFarming>();
            bool allowUndergroundFarming = api.World.Config.GetBool("allowUndergroundFarming", false);
            int lightPenalty = allowUndergroundFarming ? 0 : Math.Max(0, api.World.SeaLevel - farmlandPos.Y);
            EnumLightLevelType lightType = allowUndergroundFarming ? EnumLightLevelType.MaxLight : EnumLightLevelType.OnlySunLight;
            int sunlight = api.World.BlockAccessor.GetLightLevel(farmlandPos.UpCopy(), lightType);
            int delayGrowthBelowSunLight = farmingSystem?.Config?.DelayGrowthBelowSunLight ?? 19;
            float lossPerLevel = farmingSystem?.Config?.LossPerLevel ?? 0.1f;
            return GameMath.Clamp(1 - (delayGrowthBelowSunLight - (sunlight - lightPenalty)) * lossPerLevel, 0, 1);
        }

        internal static double GetLightAdjustedStageDurationHours(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland)
        {
            double stageHours = GetBaseStageDurationHours(api, crop, farmland);
            double lightGrowthSpeedFactor = GetLightGrowthSpeedFactor(api, farmlandPos);

            if (lightGrowthSpeedFactor <= 0)
            {
                return double.PositiveInfinity;
            }

            return stageHours / lightGrowthSpeedFactor / GetConfiguredCropGrowthRateMultiplier(api);
        }

        internal static double GetConfiguredCropGrowthRateMultiplier(ICoreAPI api)
        {
            return Math.Max((double)api.World.Config.GetDecimal("cropGrowthRateMul", 1.0), 0.01);
        }

        internal static TemperatureSimulationReport SimulateTemperatureWindow(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland, double initialHourOffset, double simulatedGrowthHours)
        {
            TemperatureSimulationReport report = new TemperatureSimulationReport
            {
                Failure = TemperatureFailure.None,
                LowestTempExperienced = 999f,
                HighestTempExperienced = -999f
            };

            if (crop.CropProps == null)
            {
                return report;
            }

            float coldDamageAccum = 0f;
            float heatDamageAccum = 0f;
            double intervalHours = 3.5;
            double currentTotalHours = api.World.Calendar.TotalHours;
            double futureHourOffset = Math.Max(0, initialHourOffset);
            BlockPos cropPos = farmlandPos.UpCopy();

            while (futureHourOffset < simulatedGrowthHours)
            {
                futureHourOffset += intervalHours;
                double simulatedTotalDays = (currentTotalHours + futureHourOffset) / api.World.Calendar.HoursPerDay;

                ClimateCondition futureClimate = api.World.BlockAccessor.GetClimateAt(
                    cropPos,
                    EnumGetClimateMode.ForSuppliedDate_TemperatureRainfallOnly,
                    simulatedTotalDays
                );

                if (futureClimate == null) break;

                if (farmland.Roomness > 0) futureClimate.Temperature += 5f;

                if (futureClimate.Temperature < report.LowestTempExperienced) report.LowestTempExperienced = futureClimate.Temperature;
                if (futureClimate.Temperature > report.HighestTempExperienced) report.HighestTempExperienced = futureClimate.Temperature;

                if (futureClimate.Temperature < crop.CropProps.ColdDamageBelow)
                {
                    coldDamageAccum += (float)intervalHours;
                }
                else
                {
                    coldDamageAccum = Math.Max(0f, coldDamageAccum - (float)intervalHours / 10f);
                }

                if (futureClimate.Temperature > crop.CropProps.HeatDamageAbove)
                {
                    heatDamageAccum += (float)intervalHours;
                }
                else
                {
                    heatDamageAccum = Math.Max(0f, heatDamageAccum - (float)intervalHours / 10f);
                }

                if (coldDamageAccum > 48f)
                {
                    report.Failure = TemperatureFailure.Freeze;
                    report.ExpectedFailureHour = (float)futureHourOffset;
                    return report;
                }

                if (heatDamageAccum > 48f)
                {
                    report.Failure = TemperatureFailure.Burn;
                    report.ExpectedFailureHour = (float)futureHourOffset;
                    return report;
                }
            }

            return report;
        }
    }
}
