using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace IceCellarMod
{
    /// <summary>
    /// When attached to a block, this makes that block count as a "cooling wall"
    /// in the game's cellar system, the same way stone or soil does.
    ///
    /// This behavior is assigned directly by block JSON or from mod config
    /// during asset finalization.
    /// </summary>
    public class BlockBehaviorIceCooling : BlockBehavior
    {
        // Returning -1 makes configured blocks behave like vanilla cooling materials
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
            // IceCooling is only meant to affect cellar heat retention. Sound
            // and water checks should keep the block's normal vanilla behavior.
            if (type != EnumRetentionType.Heat || !block.SideSolid[facing.Index]) return 0;

            // PreventDefault means: use our return value, ignore other behaviours.
            handling = EnumHandling.PreventDefault;
            return CoolingRetentionValue;
        }
    }
}
