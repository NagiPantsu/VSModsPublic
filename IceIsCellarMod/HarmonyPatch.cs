using HarmonyLib;
using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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

    [HarmonyPatch]
    public static class TransitionRatePatch
    {
        public static IceCellarModSystem? ModSystem { get; set; }

        [HarmonyTargetMethod]
        static MethodBase? TargetMethod()
        {
            var type = AccessTools.TypeByName("Vintagestory.API.Common.CollectibleObject");
            if (type == null)
            {
                Console.Error.WriteLine("[IceCellar] Could not find CollectibleObject type.");
                return null;
            }

            var method = AccessTools.Method(type, "GetTransitionRateMul",
                new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(EnumTransitionType) });

            if (method == null)
                Console.Error.WriteLine("[IceCellar] Could not find GetTransitionRateMul method.");

            return method;
        }

        [HarmonyPostfix]
        static void Postfix(ref float __result, IWorldAccessor world, ItemSlot inSlot,
                            EnumTransitionType transType)
        {
            if (transType != EnumTransitionType.Perish) return;
            if (ModSystem == null || world == null || inSlot?.Inventory == null) return;

            BlockPos? invPos = inSlot.Inventory.Pos;
            if (invPos == null) return;

            BlockPos pos = invPos.Copy();

            float? newRate = ModSystem.GetIceCellarPerishRateOverride(pos, world);

            if (newRate.HasValue)
            {
                // Never override vanilla with a worse value
                __result = Math.Min(__result, newRate.Value);
            }
        }
    }
}
