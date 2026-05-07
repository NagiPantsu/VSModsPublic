using HarmonyLib;
using ItemRarity.Rarities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

// ReSharper disable InconsistentNaming
// Patching this is making me insane so I'm deleting most harmony and api uses and only preserve
//  the bare minimum so we can have some shield functionality.

namespace ItemRarity.Patches.Methods;

[HarmonyPatch]
public static class GetHeldItemNamePatch
{
    // Handles all normal items.
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemName)), HarmonyPostfix,
     HarmonyPriority(Priority.Last)]
    public static void CollectibleObject_GetHeldItemNamePatch(CollectibleObject __instance, ItemStack itemStack,
        ref string __result)
    {
        // Skip shields — they override GetHeldItemName, so CollectibleObject's
        // version is never called for them. The dedicated patch below handles them.
        if (itemStack?.Collectible is ItemShieldFromAttributes)
            return;

        ApplyRarityName(itemStack, ref __result);
    }

    // ItemShieldFromAttributes overrides GetHeldItemName, so we need a separate
    // postfix targeting it directly to get the colored rarity prefix on shields.
    [HarmonyPatch(typeof(ItemShieldFromAttributes), nameof(ItemShieldFromAttributes.GetHeldItemName)),
     HarmonyPostfix, HarmonyPriority(Priority.Last)]
    public static void ItemShieldFromAttributes_GetHeldItemNamePatch(ItemShieldFromAttributes __instance,
        ItemStack itemStack, ref string __result)
    {
        ApplyRarityName(itemStack, ref __result);
    }

    private static void ApplyRarityName(ItemStack? itemStack, ref string result)
    {
        if (!Rarity.TryGetRarity(itemStack, out var rarity))
            return;

        var rarityName = rarity.IgnoreTranslation
            ? $"[{rarity.Name}]"
            : Lang.GetWithFallback($"itemrarity:{rarity.Key}", "itemrarity:unknown", rarity.Name);

        if (result.Contains(rarityName))
            return;

        result = $"<font color=\"{rarity.Color}\" weight=bold>{rarityName} {result}</font>";
    }
}
