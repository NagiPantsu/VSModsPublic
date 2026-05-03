using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CropForecaster
{
    public class CropForecastSystem
    {
        private const int DefaultDelayGrowthBelowSunLight = 19;
        private const float DefaultLightLossPerLevel = 0.1f;
        private const float GreenhouseTemperatureBonus = 5f;
        private const int DefaultProbabilisticSimulationCount = 48;

        private static Type? cachedFarmlandType;
        private static PropertyInfo? cachedRoomnessProperty;
        private static FieldInfo? cachedRoomnessField;

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

        public struct ProbabilisticForecastReport
        {
            public ForecastResult MostLikelyResult;
            public int SimulationCount;
            public double SurvivalProbability;
            public double FreezeProbability;
            public double BurnProbability;
            public double MedianGrowthHours;
            public double P10GrowthHours;
            public double P90GrowthHours;
            public double FastestGrowthHours;
            public double SlowestGrowthHours;
            public float LowestTempExperienced;
            public float HighestTempExperienced;
        }

        internal struct ProbabilisticSimulationOutcome
        {
            public ForecastResult Result;
            public double GrowthHours;
            public float LowestTempExperienced;
            public float HighestTempExperienced;
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

            double futureHourOffset = Math.Max(0, hoursUntilNextStage);
            double simulatedGrowthHours = futureHourOffset + Math.Max(0, remainingStageTransitions - 1) * defaultStageDuration;
            report.EstimatedGrowthHours = simulatedGrowthHours;

            bool hasGreenhouseBonus = HasGreenhouseBonus(farmland);
            TemperatureSimulationReport temperatureReport = SimulateTemperatureWindow(api, farmlandPos, crop, hasGreenhouseBonus, futureHourOffset, simulatedGrowthHours);
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

        public static ProbabilisticForecastReport PredictCurrentCropViabilityProbabilistic(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland, int simulationCount = DefaultProbabilisticSimulationCount)
        {
            double currentTotalHours = api.World.Calendar.TotalHours;
            double hoursUntilNextStage = Math.Max(0, farmland.TotalHoursForNextStage - currentTotalHours);
            return PredictCropViabilityProbabilistic(api, farmlandPos, crop, farmland, crop.CurrentStage(), hoursUntilNextStage, false, simulationCount);
        }

        public static ProbabilisticForecastReport PredictNewPlantingViabilityProbabilistic(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland, int simulationCount = DefaultProbabilisticSimulationCount)
        {
            double firstStageHours = EstimateStageDurationHours(api, farmlandPos, crop, farmland);
            return PredictCropViabilityProbabilistic(api, farmlandPos, crop, farmland, 1, firstStageHours, true, simulationCount);
        }

        private static ProbabilisticForecastReport PredictCropViabilityProbabilistic(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, BlockEntityFarmland farmland, int currentStage, double hoursUntilNextStage, bool randomizeInitialStage, int simulationCount)
        {
            // This is intentionally layered on top of the same live crop/farmland data as the
            // deterministic forecast. The only extra uncertainty we model here is vanilla's
            // per-stage random growth roll, so modded crop stats still flow through naturally.
            ProbabilisticForecastReport report = new ProbabilisticForecastReport
            {
                SimulationCount = Math.Max(1, simulationCount),
                LowestTempExperienced = 999f,
                HighestTempExperienced = -999f,
                MedianGrowthHours = double.NaN,
                P10GrowthHours = double.NaN,
                P90GrowthHours = double.NaN,
                FastestGrowthHours = double.NaN,
                SlowestGrowthHours = double.NaN
            };

            if (crop.CropProps == null)
            {
                report.MostLikelyResult = ForecastResult.UnsupportedCrop;
                return report;
            }

            if (IsPumpkinCrop(crop))
            {
                report.MostLikelyResult = ForecastResult.SprawlingCropNotSupported;
                return report;
            }

            int remainingStageTransitions = Math.Max(0, crop.CropProps.GrowthStages - currentStage);
            if (remainingStageTransitions == 0)
            {
                report.MostLikelyResult = ForecastResult.ReadyToHarvest;
                report.SurvivalProbability = 1;
                report.MedianGrowthHours = 0;
                report.P10GrowthHours = 0;
                report.P90GrowthHours = 0;
                report.FastestGrowthHours = 0;
                report.SlowestGrowthHours = 0;
                return report;
            }

            double baseStageDuration = GetLightAdjustedStageDurationHours(api, farmlandPos, crop, farmland);
            if (double.IsPositiveInfinity(baseStageDuration))
            {
                report.MostLikelyResult = ForecastResult.InsufficientLight;
                return report;
            }

            int effectiveSimulationCount = Math.Max(32, simulationCount);
            report.SimulationCount = effectiveSimulationCount;

            Random random = new Random(CreateProbabilisticSeed(api, farmlandPos, crop, currentStage, hoursUntilNextStage, randomizeInitialStage));
            List<double> survivingGrowthHours = new List<double>(effectiveSimulationCount);
            bool hasGreenhouseBonus = HasGreenhouseBonus(farmland);

            int surviveCount = 0;
            int freezeCount = 0;
            int burnCount = 0;

            for (int i = 0; i < effectiveSimulationCount; i++)
            {
                ProbabilisticSimulationOutcome outcome = SimulateProbabilisticOutcome(
                    api,
                    farmlandPos,
                    crop,
                    hasGreenhouseBonus,
                    baseStageDuration,
                    Math.Max(0, hoursUntilNextStage),
                    remainingStageTransitions,
                    randomizeInitialStage,
                    random
                );

                if (outcome.LowestTempExperienced < report.LowestTempExperienced) report.LowestTempExperienced = outcome.LowestTempExperienced;
                if (outcome.HighestTempExperienced > report.HighestTempExperienced) report.HighestTempExperienced = outcome.HighestTempExperienced;

                switch (outcome.Result)
                {
                    case ForecastResult.WillSurvive:
                        surviveCount++;
                        survivingGrowthHours.Add(outcome.GrowthHours);
                        break;

                    case ForecastResult.WillFreezeToDeath:
                        freezeCount++;
                        break;

                    case ForecastResult.WillBurnToDeath:
                        burnCount++;
                        break;
                }
            }

            double simulationCountAsDouble = effectiveSimulationCount;
            report.SurvivalProbability = surviveCount / simulationCountAsDouble;
            report.FreezeProbability = freezeCount / simulationCountAsDouble;
            report.BurnProbability = burnCount / simulationCountAsDouble;
            report.MostLikelyResult = GetMostLikelyResult(surviveCount, freezeCount, burnCount);

            if (survivingGrowthHours.Count > 0)
            {
                survivingGrowthHours.Sort();
                report.MedianGrowthHours = GetPercentile(survivingGrowthHours, 0.5);
                report.P10GrowthHours = GetPercentile(survivingGrowthHours, 0.1);
                report.P90GrowthHours = GetPercentile(survivingGrowthHours, 0.9);
                report.FastestGrowthHours = survivingGrowthHours[0];
                report.SlowestGrowthHours = survivingGrowthHours[survivingGrowthHours.Count - 1];
            }

            if (report.LowestTempExperienced == 999f) report.LowestTempExperienced = 0f;
            if (report.HighestTempExperienced == -999f) report.HighestTempExperienced = 0f;

            return report;
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
            bool allowUndergroundFarming = api.World.Config.GetBool("allowUndergroundFarming", false);
            int lightPenalty = allowUndergroundFarming ? 0 : Math.Max(0, api.World.SeaLevel - farmlandPos.Y);
            EnumLightLevelType lightType = allowUndergroundFarming ? EnumLightLevelType.MaxLight : EnumLightLevelType.OnlySunLight;
            int sunlight = api.World.BlockAccessor.GetLightLevel(farmlandPos.UpCopy(), lightType);
            int delayGrowthBelowSunLight = DefaultDelayGrowthBelowSunLight;
            float lossPerLevel = DefaultLightLossPerLevel;
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

        internal static TemperatureSimulationReport SimulateTemperatureWindow(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, bool hasGreenhouseBonus, double initialHourOffset, double simulatedGrowthHours)
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

                if (hasGreenhouseBonus) futureClimate.Temperature += GreenhouseTemperatureBonus;

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

        private static ProbabilisticSimulationOutcome SimulateProbabilisticOutcome(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, bool hasGreenhouseBonus, double baseStageDuration, double hoursUntilNextStage, int remainingStageTransitions, bool randomizeInitialStage, Random random)
        {
            // For already planted crops the first transition is whatever the live farmland says is left.
            // For pre-sow preview there is no live timer yet, so we sample that first stage too.
            double totalGrowthHours = randomizeInitialStage
                ? SampleStageDuration(baseStageDuration, random)
                : hoursUntilNextStage;

            for (int transitionIndex = 1; transitionIndex < remainingStageTransitions; transitionIndex++)
            {
                totalGrowthHours += SampleStageDuration(baseStageDuration, random);
            }

            TemperatureSimulationReport temperatureReport = SimulateTemperatureWindow(api, farmlandPos, crop, hasGreenhouseBonus, 0, totalGrowthHours);
            ForecastResult result = ForecastResult.WillSurvive;

            if (temperatureReport.Failure == TemperatureFailure.Freeze)
            {
                result = ForecastResult.WillFreezeToDeath;
            }
            else if (temperatureReport.Failure == TemperatureFailure.Burn)
            {
                result = ForecastResult.WillBurnToDeath;
            }

            return new ProbabilisticSimulationOutcome
            {
                Result = result,
                GrowthHours = totalGrowthHours,
                LowestTempExperienced = temperatureReport.LowestTempExperienced,
                HighestTempExperienced = temperatureReport.HighestTempExperienced
            };
        }

        private static double SampleStageDuration(double baseStageDuration, Random random)
        {
            return baseStageDuration * (0.9 + 0.2 * random.NextDouble());
        }

        private static int CreateProbabilisticSeed(ICoreAPI api, BlockPos farmlandPos, BlockCrop crop, int currentStage, double hoursUntilNextStage, bool randomizeInitialStage)
        {
            // We want stable tooltip output while the observed state is unchanged, without pretending
            // to replicate the game's internal RNG stream exactly.
            HashCode hash = new HashCode();
            hash.Add(farmlandPos.X);
            hash.Add(farmlandPos.Y);
            hash.Add(farmlandPos.Z);
            hash.Add(crop.Id);
            hash.Add(currentStage);
            hash.Add((int)Math.Round(hoursUntilNextStage * 10));
            hash.Add(randomizeInitialStage);
            hash.Add((int)Math.Floor(api.World.Calendar.TotalHours));
            return hash.ToHashCode();
        }

        private static ForecastResult GetMostLikelyResult(int surviveCount, int freezeCount, int burnCount)
        {
            if (surviveCount >= freezeCount && surviveCount >= burnCount) return ForecastResult.WillSurvive;
            if (freezeCount >= burnCount) return ForecastResult.WillFreezeToDeath;
            return ForecastResult.WillBurnToDeath;
        }

        private static double GetPercentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0) return double.NaN;
            if (sortedValues.Count == 1) return sortedValues[0];

            double clampedPercentile = GameMath.Clamp(percentile, 0, 1);
            double scaledIndex = clampedPercentile * (sortedValues.Count - 1);
            int lowerIndex = (int)Math.Floor(scaledIndex);
            int upperIndex = (int)Math.Ceiling(scaledIndex);

            if (lowerIndex == upperIndex) return sortedValues[lowerIndex];

            double fraction = scaledIndex - lowerIndex;
            return sortedValues[lowerIndex] + (sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction;
        }

        internal static bool HasGreenhouseBonus(BlockEntityFarmland farmland)
        {
            Type farmlandType = farmland.GetType();

            if (cachedFarmlandType != farmlandType)
            {
                cachedFarmlandType = farmlandType;
                cachedRoomnessProperty = farmlandType.GetProperty("Roomness", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                cachedRoomnessField = farmlandType.GetField("roomness", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            if (cachedRoomnessProperty?.GetValue(farmland) is int roomnessFromProperty)
            {
                return roomnessFromProperty > 0;
            }

            if (cachedRoomnessField?.GetValue(farmland) is int roomnessFromField)
            {
                return roomnessFromField > 0;
            }

            return false;
        }
    }
}
