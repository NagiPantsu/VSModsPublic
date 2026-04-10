using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CropForecaster
{
    public class CropForecastModSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            
            // Register the behavior with the engine
            api.RegisterBlockBehaviorClass("CropForecast", typeof(BlockBehaviorCropForecast));
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
    }
}
