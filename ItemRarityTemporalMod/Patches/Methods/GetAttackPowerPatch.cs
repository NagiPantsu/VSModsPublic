using HarmonyLib;
using ItemRarity.Attributes;
using ItemRarity.Rarities;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

// ReSharper disable InconsistentNaming

namespace ItemRarity.Patches.Methods;

[HarmonyPatch]
public static class GetAttackPowerPatch
{
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetAttackPower)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
    public static void CollectibleObject_GetAttackPowerPatch(CollectibleObject __instance, ItemStack itemStack, ref float __result)
    {
        var wearableStats = __instance.GetCollectibleInterface<IWearableStatsSupplier>();
        if (wearableStats?.IsArmorType(new DummySlot(itemStack)) == true || !Rarity.TryGetRarity(itemStack, out _))
            return;

        __result *= Attribute.AttackPowerMultiplier.GetFloat(itemStack, 1f);
    }
}
