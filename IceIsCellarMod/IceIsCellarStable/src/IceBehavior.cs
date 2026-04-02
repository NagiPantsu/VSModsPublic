using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace IceIsCellar
{
    public class BlockBehaviorCellarBlock : BlockBehavior
    {
        public BlockBehaviorCellarBlock(Block block) : base(block) { }

        public override int GetRetention(BlockPos pos, BlockFacing facing,
            EnumRetentionType type, ref EnumHandling handled)
        {
            if (type == EnumRetentionType.Heat)
            {
                handled = EnumHandling.PreventSubsequent;
                return -1;
            }
            return base.GetRetention(pos, facing, type, ref handled);
        }
    }
}