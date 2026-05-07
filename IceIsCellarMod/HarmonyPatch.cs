using HarmonyLib;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;

namespace IceCellarMod
{
    public class HarmonyPatchLoader : ModSystem
    {
        static bool patchesApplied;
        Harmony? harmony;

        public override double ExecuteOrder() => 0.04;

        public override void StartServerSide(ICoreServerAPI sapi)
            => ApplyPatches(sapi);

        public override void StartClientSide(ICoreClientAPI capi)
            => ApplyPatches(capi);

        void ApplyPatches(ICoreAPI api)
        {
            if (patchesApplied)
            {
                api.Logger.Notification("[IceCellar] Harmony patches already applied, skipping duplicate patch pass.");
                return;
            }

            TransitionRatePatch.ModSystem = api.ModLoader.GetModSystem<IceCellarModSystem>();

            harmony = new Harmony("vs.icecellar");

            try
            {
                harmony.PatchAll(typeof(HarmonyPatchLoader).Assembly);
                patchesApplied = true;
                api.Logger.Notification("[IceCellar] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                api.Logger.Error($"[IceCellar] Failed to apply Harmony patches: {ex}");
            }
        }

        public override void Dispose()
        {
            if (harmony == null) return;

            harmony.UnpatchAll("vs.icecellar");
            patchesApplied = false;
        }
    }

    [HarmonyPatch(
        typeof(CollectibleObject),
        nameof(CollectibleObject.GetTransitionRateMul),
        new Type[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(EnumTransitionType) })]
    public static class TransitionRatePatch
    {
        public static IceCellarModSystem? ModSystem { get; set; }

        [HarmonyPostfix]
        static void Postfix(ref float __result, IWorldAccessor world, ItemSlot inSlot,
                            EnumTransitionType transType)
        {
            if (transType != EnumTransitionType.Perish) return;
            if (ModSystem == null || world == null || inSlot?.Inventory == null) return;

            var invPos = inSlot.Inventory.Pos;
            if (invPos == null) return;

            if (ModSystem.GetIceCellarPerishRateOverride(invPos, world) is float newRate)
            {
                // Never override vanilla with a worse value
                __result = Math.Min(__result, newRate);
            }
        }
    }
}
