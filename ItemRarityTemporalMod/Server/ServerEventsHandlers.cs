using ItemRarity.Packets;
using ItemRarity.Rarities;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace ItemRarity.Server;

internal static class ServerEventsHandlers
{
    public static void OnPlayerJoin(IServerPlayer byplayer)
    {
        var api = ModCore.ServerApi;
        if (api == null)
            return;

        try
        {
            api.Network.GetChannel(ModCore.ConfigSyncNetChannel).SendPacket(new ServerConfigMessage
                { SerializedConfig = JsonConvert.SerializeObject(ModCore.Config) }, byplayer);
        }
        catch
        {
            // Ignore join-time sync failures if mod startup did not finish cleanly.
        }
    }

    public static void OnEntitySpawn(Entity entity)
    {
        if (!ModCore.Config.Rarity.ApplyRarityOnItemDrop || entity is not EntityItem { Itemstack: not null, Attributes: not null } item)
            return;

        if (!Rarity.IsSuitableFor(item.Itemstack))
            return;

        Rarity.ApplyRarity(item.Itemstack);
    }
}
