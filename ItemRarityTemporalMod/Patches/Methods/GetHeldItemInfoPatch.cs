using System;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using ItemRarity.Rarities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Attribute = ItemRarity.Attributes.Attribute;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming

namespace ItemRarity.Patches.Methods;

[HarmonyPatch]
public static class GetHeldItemInfoPatch
{
    // -------------------------------------------------------------------------
    // Reverse patch: lets us call CollectibleObject.GetHeldItemInfo directly.
    // -------------------------------------------------------------------------
    [HarmonyReversePatch, HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo))]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CollectibleObject_GetHeldItemInfoReversePatch(CollectibleObject __instance, ItemSlot inSlot,
        StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
    }

    // -------------------------------------------------------------------------
    // Postfix on CollectibleObject.GetHeldItemInfo — handles tools/weapons.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo)), HarmonyPostfix,
     HarmonyPriority(Priority.Last)]
    public static void CollectibleObject_GetHeldItemInfoPatch(CollectibleObject __instance, ItemSlot inSlot,
        StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (inSlot is not { Itemstack: not null })
            return;

        if (inSlot.Itemstack.Collectible is ItemShield)
            return;

        if (!Rarity.TryGetRarity(inSlot.Itemstack, out _))
            return;

        RewriteToolInfo(inSlot.Itemstack, __instance, dsc);
    }

    // -------------------------------------------------------------------------
    // Prefix on ItemShield.GetHeldItemInfo — handles base shields (e.g., Blackguard).
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(ItemShield), nameof(ItemShield.GetHeldItemInfo)),
     HarmonyPrefix, HarmonyPriority(Priority.Last)]
    public static bool ItemShield_GetHeldItemInfoPatch(ItemShield __instance,
        ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (inSlot is not { Itemstack: not null })
            return true;

        if (!Rarity.TryGetRarity(inSlot.Itemstack, out _))
            return true;

        dsc.Clear();
        CollectibleObject_GetHeldItemInfoReversePatch(__instance, inSlot, dsc, world, withDebugInfo);
        AppendShieldInfo(inSlot.Itemstack, dsc);

        return false;
    }

    // -------------------------------------------------------------------------
    // Prefix on ItemShieldFromAttributes.GetHeldItemInfo — handles shields.
    //
    // I'll try to bypass the shield override entirely for rarity items, call the base
    // CollectibleObject tooltip implementation, then append one adjusted shield
    // stat block. I'm trying to avoid unreliable line-matching against the localized
    // vanilla shield section and prevent duplicated stats showing in the tooltip. -nagi.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(ItemShieldFromAttributes), nameof(ItemShieldFromAttributes.GetHeldItemInfo)),
     HarmonyPrefix, HarmonyPriority(Priority.Last)]
    public static bool ItemShieldFromAttributes_GetHeldItemInfoPatch(ItemShieldFromAttributes __instance,
        ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (inSlot is not { Itemstack: not null })
            return true;

        if (!Rarity.TryGetRarity(inSlot.Itemstack, out _))
            return true;

        dsc.Clear();
        CollectibleObject_GetHeldItemInfoReversePatch(__instance, inSlot, dsc, world, withDebugInfo);
        AppendShieldInfo(inSlot.Itemstack, dsc);

        return false;
    }

    // -------------------------------------------------------------------------
    // Prefix on ItemSpear.GetHeldItemInfo — handles spears.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(ItemSpear), nameof(ItemSpear.GetHeldItemInfo)), HarmonyPrefix,
     HarmonyPriority(Priority.Last)]
    public static bool ItemSpear_GetHeldItemInfoPatch(ItemSpear __instance, ItemSlot inSlot, StringBuilder dsc,
        IWorldAccessor world, bool withDebugInfo)
    {
        if (inSlot.Itemstack == null || !Rarity.TryGetRarity(inSlot.Itemstack, out _))
            return true;

        CollectibleObject_GetHeldItemInfoReversePatch(__instance, inSlot, dsc, world, withDebugInfo);

        var piercingDamage = 1.5f;
        if (inSlot.Itemstack.Collectible.Attributes != null)
            piercingDamage = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat();

        piercingDamage *= Attribute.PiercingPowerMultiplier.GetFloat(inSlot.Itemstack, 1f);

        dsc.AppendLine($"{FormatStat(piercingDamage)}{Lang.Get("piercing-damage-thrown")}");

        return false;
    }

    // -------------------------------------------------------------------------
    // Rewrites mining speed and attack power lines for tools.
    // -------------------------------------------------------------------------
    private static void RewriteToolInfo(ItemStack itemStack, CollectibleObject collectible, StringBuilder sb)
    {
        if (itemStack.Collectible.MiningSpeed is not { Count: > 0 })
            return;

        var lines = sb.ToString().Trim().Split(Environment.NewLine);

        sb.Clear();

        var miningSpeedLine = Lang.Get("item-tooltip-miningspeed") ?? string.Empty;
        var foundLine = Array.FindIndex(lines, line => line.StartsWith(miningSpeedLine, StringComparison.Ordinal));
        var miningSpeedMul = Attribute.MiningSpeedMultiplier.GetFloat(itemStack, 1f);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i != foundLine)
            {
                sb.AppendLine(lines[i]);
                continue;
            }

            sb.Append(miningSpeedLine);

            var num = 0;
            foreach (var miningSpeed in collectible.MiningSpeed)
            {
                if (miningSpeed.Value <= 1.0)
                    continue;

                if (num++ > 0)
                    sb.Append(", ");

                sb.Append(Lang.Get(miningSpeed.Key.ToString()))
                    .Append(' ')
                    .Append((miningSpeed.Value * miningSpeedMul).ToString("#.#"))
                    .Append('x');
            }

            sb.AppendLine();

            if (collectible.GetAttackPower(itemStack) > 0.5f)
                sb.AppendLine(Lang.Get("Attack power: {0} damage",
                    FormatStat(collectible.GetAttackPower(itemStack))));
        }
    }

    // -------------------------------------------------------------------------
    // Appends one rarity-adjusted shield tooltip block after the generic base
    // collectible info. The shield override is skipped entirely by the prefix.
    // -------------------------------------------------------------------------
    private static void AppendShieldInfo(ItemStack itemStack, StringBuilder sb)
    {
        var shieldJson = itemStack.Collectible.Attributes?["shield"];
        ITreeAttribute shieldTree = itemStack.Attributes?.GetTreeAttribute("shield");

        float activeMeleeChance = 0f;
        float passiveMeleeChance = 0f;
        float baseMeleeAbsorption = 2f;
        float activeProjectileChance = 0f;
        float passiveProjectileChance = 0f;
        float baseProjectileAbsorption = 2f;
        bool hasStats = false;

        // 1. Base JSON parse
        if (shieldJson != null && shieldJson.Exists)
        {
            activeMeleeChance = shieldJson["protectionChance"]?["active"]?.AsFloat() * 100f ?? 0f;
            passiveMeleeChance = shieldJson["protectionChance"]?["passive"]?.AsFloat() * 100f ?? 0f;
            baseMeleeAbsorption = shieldJson["damageAbsorption"]?.AsFloat(2f) ?? 2f;
            
            activeProjectileChance = shieldJson["protectionChance"]?["active-projectile"]?.Exists == true
                ? shieldJson["protectionChance"]["active-projectile"].AsFloat() * 100f
                : activeMeleeChance;
            passiveProjectileChance = shieldJson["protectionChance"]?["passive-projectile"]?.Exists == true
                ? shieldJson["protectionChance"]["passive-projectile"].AsFloat() * 100f
                : passiveMeleeChance;
            baseProjectileAbsorption = shieldJson["projectileDamageAbsorption"]?.AsFloat(baseMeleeAbsorption) ?? baseMeleeAbsorption;
            hasStats = true;
        }
        else if (itemStack.Collectible is ItemShield shield)
        {
            // 2. Reflection extraction for ItemShield properties
            try 
            {
                var type = shield.GetType();
                var dmgAbs = type.GetProperty("DamageAbsorption")?.GetValue(shield);
                if (dmgAbs != null) baseMeleeAbsorption = (float)dmgAbs;
                
                var actChance = type.GetProperty("ProtectionChanceActive")?.GetValue(shield);
                if (actChance != null) activeMeleeChance = (float)actChance * 100f;
                
                var passChance = type.GetProperty("ProtectionChancePassive")?.GetValue(shield);
                if (passChance != null) passiveMeleeChance = (float)passChance * 100f;

                activeProjectileChance = activeMeleeChance;
                passiveProjectileChance = passiveMeleeChance;
                baseProjectileAbsorption = baseMeleeAbsorption;
                hasStats = true; 
            }
            catch {}
        }

        if (!hasStats) return;

        // Multiply base stats by the rarity multiplier!
        var meleeAbsorption = baseMeleeAbsorption * Attribute.ShieldDamageAbsorptionMultiplier.GetFloat(itemStack, 1f);
        var projectileAbsorption = baseProjectileAbsorption * Attribute.ShieldProjectileDamageAbsorptionMultiplier.GetFloat(itemStack, 1f);

        AppendShieldStats(sb, activeMeleeChance, passiveMeleeChance, meleeAbsorption,
            activeProjectileChance, passiveProjectileChance, projectileAbsorption);
        AppendShieldMaterials(itemStack, sb);
    }

    private static void AppendShieldStats(StringBuilder sb,
        float activeMeleeChance, float passiveMeleeChance, float meleeAbsorption,
        float activeProjectileChance, float passiveProjectileChance, float projectileAbsorption)
    {
        var hasDistinctProjectileStats =
            Math.Abs(activeMeleeChance - activeProjectileChance) > 0.05f ||
            Math.Abs(passiveMeleeChance - passiveProjectileChance) > 0.05f ||
            Math.Abs(meleeAbsorption - projectileAbsorption) > 0.05f;

        if (!hasDistinctProjectileStats)
        {
            sb.AppendLine(Lang.Get("shield-stats",
                FormatStat(activeMeleeChance), FormatStat(passiveMeleeChance), FormatStat(meleeAbsorption)));
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"<strong>{Lang.Get("shield-projectile-protection")}</strong>");
        sb.AppendLine(Lang.Get("shield-stats",
            FormatStat(activeProjectileChance), FormatStat(passiveProjectileChance), FormatStat(projectileAbsorption)));
        sb.AppendLine();

        sb.AppendLine($"<strong>{Lang.Get("shield-melee-protection")}</strong>");
        sb.AppendLine(Lang.Get("shield-stats",
            FormatStat(activeMeleeChance), FormatStat(passiveMeleeChance), FormatStat(meleeAbsorption)));
        sb.AppendLine();
    }

    private static void AppendShieldMaterials(ItemStack itemStack, StringBuilder sb)
    {
        var wood = itemStack.Attributes?.GetString("wood");
        var metal = itemStack.Attributes?.GetString("metal");

        if (!string.IsNullOrEmpty(wood))
            sb.AppendLine(Lang.Get("shield-woodtype", Lang.Get("material-" + wood)));

        if (!string.IsNullOrEmpty(metal))
            sb.AppendLine(Lang.Get("shield-metaltype", Lang.Get("material-" + metal)));
    }

    private static string FormatStat(float value) => value.ToString("0.#");
}
