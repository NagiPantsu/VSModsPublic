using Vintagestory.API.Common;

namespace IceIsCellar       
{
    public class IceBehavior : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterBlockBehaviorClass("CellarBlock", 
                typeof(BlockBehaviorCellarBlock));
        }
    }
    }