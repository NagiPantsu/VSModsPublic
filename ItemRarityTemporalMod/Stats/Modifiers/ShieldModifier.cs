using ItemRarity.Attributes;
using ItemRarity.Rarities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ItemRarity.Stats.Modifiers;

public sealed class ShieldModifier : IStatsModifier
{
    public bool IsSuitable(ItemStack itemStack)
    {
        return itemStack.Collectible is ItemShield shield;
    }

    public void Apply(RarityModel rarityModel, ItemStack itemStack, ITreeAttribute modAttributes)
    {
        var damageAbsorptionMul = rarityModel.ShieldProtectionMultiplier.Random;
        var projectileDamageAbsorptionMul = rarityModel.ShieldProtectionMultiplier.Random;

        Attribute.ShieldDamageAbsorptionMultiplier.SetFloat(modAttributes, damageAbsorptionMul);
        Attribute.ShieldProjectileDamageAbsorptionMultiplier.SetFloat(modAttributes, projectileDamageAbsorptionMul);

        // Attempted fix for Blackguard and crude shields not being shieldy enough?
        if (!itemStack.Attributes.HasAttribute("shield"))
        {
            var baseShieldJson = itemStack.Collectible.Attributes?["shield"];
            if (baseShieldJson != null && baseShieldJson.Exists)
            {
                // Convert the JSON definition from the collectible into a live TreeAttribute
                // No more CS0266 hehehe.
                itemStack.Attributes["shield"] = baseShieldJson.ToAttribute() as ITreeAttribute;
            }
            else if (itemStack.Collectible is ItemShield shield)
            {
                ITreeAttribute defaultShieldTree = new TreeAttribute();
                float dmgAbs = 2f;
                try 
                {
                    var type = shield.GetType();
                    var prop = type.GetProperty("DamageAbsorption");
                    var damageAbsorptionValue = prop?.GetValue(shield);
                    if (damageAbsorptionValue is float damageAbsorptionFloat)
                    {
                        dmgAbs = damageAbsorptionFloat;
                    }
                    else if (damageAbsorptionValue is double damageAbsorptionDouble)
                    {
                        dmgAbs = (float)damageAbsorptionDouble;
                    }
                } 
                catch {}
                defaultShieldTree.SetFloat("damageAbsorption", dmgAbs);
                defaultShieldTree.SetFloat("projectileDamageAbsorption", dmgAbs);
                itemStack.Attributes["shield"] = defaultShieldTree;
            }
        }

        var shieldTree = itemStack.Attributes.GetTreeAttribute("shield");
        if (shieldTree != null)
        {
            // Use a default of 2f if the attribute is missing, similar to vanilla ItemShield logic
            var currentAbsorb = shieldTree.GetFloat("damageAbsorption", 2f);
            var currentProjAbsorb = shieldTree.GetFloat("projectileDamageAbsorption", currentAbsorb);

            shieldTree.SetFloat("damageAbsorption", currentAbsorb * damageAbsorptionMul);
            shieldTree.SetFloat("projectileDamageAbsorption", currentProjAbsorb * projectileDamageAbsorptionMul);
        }
    }
}
