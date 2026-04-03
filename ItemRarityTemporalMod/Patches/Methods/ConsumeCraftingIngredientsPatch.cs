using HarmonyLib;
using ItemRarity.Rarities;
using Vintagestory.API.Common;

// ReSharper disable InconsistentNaming

namespace ItemRarity.Patches.Methods;

[HarmonyPatch]
public static class ConsumeCraftingIngredientsPatch
{
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.ConsumeCraftingIngredients)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
    public static void CollectibleObject_ConsumeCraftingIngredientsPatch(CollectibleObject __instance, ItemSlot[] __0, ItemSlot __1, IRecipeBase __2)
    {
        ConsumeCraftingIngredients(__0, __1, __2);
    }

    private static void ConsumeCraftingIngredients(ItemSlot[] inSlots, ItemSlot outputSlot, IRecipeBase recipe)
    {
        if (outputSlot is not { Itemstack: not null })
            return;

        if (!ModCore.Config.Rarity.ApplyRarityOnCraft)
            return;

        if (!Rarity.IsSuitableFor(outputSlot.Itemstack))
            return;

        Rarity.ApplyRarity(outputSlot.Itemstack);
    }
}
