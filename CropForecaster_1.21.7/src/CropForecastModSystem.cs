using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CropForecaster
{
    public class CropForecastModSystem : ModSystem
    {
        private const string CycleForecastModeHotkeyCode = "cropforecaster-cyclemode";
        private ICoreClientAPI? clientApi;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            
            // Register the behavior with the engine
            api.RegisterBlockBehaviorClass("CropForecast", typeof(BlockBehaviorCropForecast));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            clientApi = api;
            CropForecastClientState.Initialize(api);

            api.Input.RegisterHotKey(
                CycleForecastModeHotkeyCode,
                "Cycle CropForecaster mode",
                GlKeys.C,
                HotkeyType.HelpAndOverlays,
                altPressed: true
            );
            api.Input.SetHotKeyHandler(CycleForecastModeHotkeyCode, OnCycleForecastModeHotkey);
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            foreach (Block block in api.World.Blocks)
            {
                if (block is BlockFarmland)
                {
                    if (block.BlockBehaviors?.Any(existing => existing is BlockBehaviorCropForecast) == true)
                    {
                        continue;
                    }

                    var behavior = new BlockBehaviorCropForecast(block);
                    if (block.BlockBehaviors == null)
                    {
                        block.BlockBehaviors = new BlockBehavior[] { behavior };
                    }
                    else
                    {
                        block.BlockBehaviors = block.BlockBehaviors.Append<BlockBehavior>(behavior).ToArray();
                    }
                }
            }
        }

        private bool OnCycleForecastModeHotkey(KeyCombination keyCombination)
        {
            CropForecastMode mode = CropForecastClientState.CycleMode();
            clientApi?.ShowChatMessage($"CropForecaster mode: {DescribeMode(mode)}");
            return true;
        }

        private static string DescribeMode(CropForecastMode mode)
        {
            return mode switch
            {
                CropForecastMode.Off => "Off",
                CropForecastMode.Basic => "Basic",
                _ => "Full"
            };
        }
    }
}
