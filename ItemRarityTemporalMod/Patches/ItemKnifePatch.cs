using HarmonyLib;
using ItemRarity.Attributes;
using ItemRarity.Rarities;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

// ReSharper disable InconsistentNaming

namespace ItemRarity.Patches;

[HarmonyPatch(typeof(ItemKnife))]
public static class ItemKnifePatch
{
    [HarmonyPostfix, HarmonyPatch(nameof(ItemKnife.OnHeldInteractStep)), HarmonyPriority(Priority.Last)]
    public static void OnHeldInteractStepPatch(ItemKnife __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel, ref bool __result)
    {
        if (entitySel == null || slot is not { Itemstack: not null })
            return;

        if (!Rarity.TryGetRarity(slot.Itemstack, out _))
            return;

        var entityBehaviour = entitySel.Entity.GetBehavior<EntityBehaviorHarvestable>();
        if (entityBehaviour == null)
            return;

        var miningSpeedMul = Attribute.MiningSpeedMultiplier.GetFloat(slot.Itemstack, 1f);
        var harvestDuration = entityBehaviour.GetHarvestDuration(slot, byEntity) / miningSpeedMul + 0.15000000596046448f;
        __result = secondsUsed < harvestDuration;
    }

    [HarmonyPostfix, HarmonyPatch(nameof(ItemKnife.OnHeldInteractStop)), HarmonyPriority(Priority.Last)]
    public static void OnHeldInteractStopPatch(ItemKnife __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel)
    {
        if (entitySel == null || slot is not { Itemstack: not null })
            return;

        if (!Rarity.TryGetRarity(slot.Itemstack, out _))
            return;

        var entityBehaviour = entitySel.Entity.GetBehavior<EntityBehaviorHarvestable>();
        if (entityBehaviour == null)
            return;

        var miningSpeedMul = Attribute.MiningSpeedMultiplier.GetFloat(slot.Itemstack, 1f);
        var harvestDuration = entityBehaviour.GetHarvestDuration(slot, byEntity) / miningSpeedMul - 0.10000000149011612f;

        if (secondsUsed < harvestDuration)
            return;

        entityBehaviour.SetHarvested(byEntity is EntityPlayer entityPlayer ? entityPlayer.Player : null);
        slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot, 3);
    }
}
