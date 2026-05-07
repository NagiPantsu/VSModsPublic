using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace IceCellarMod
{
    /// <summary>
    /// When attached to a block, this makes that block count as a "cooling wall"
    /// in the game's cellar system, the same way stone or soil does.
    ///
    /// This behavior is assigned through JSON patches to the block families
    /// this mod wants to contribute to cellar cooling.
    /// </summary>
    public class BlockBehaviorIceCooling : BlockBehavior
    {
        // Returning -1 makes patched blocks behave like vanilla cooling materials
        // for cellar insulation scoring.
        const int CoolingRetentionValue = -1;

        public BlockBehaviorIceCooling(Block block) : base(block) { }

        /// <summary>
        /// Called by the room system when it checks how much this block face
        /// contributes to insulation. Negative = cooling. Positive = warming.
        /// Zero = no contribution (open face / non-solid).
        /// </summary>
        public override int GetRetention(
            BlockPos pos,
            BlockFacing facing,
            EnumRetentionType type,
            ref EnumHandling handling)
        {
            if (type != EnumRetentionType.Heat || !block.SideSolid[facing.Index]) return 0;

            // PreventDefault means: use our return value, ignore other behaviours.
            handling = EnumHandling.PreventDefault;
            return CoolingRetentionValue;
        }
    }
}
