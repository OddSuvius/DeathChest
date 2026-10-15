using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using DeathChest.NPCs;
using Terraria.ID;
using DeathChest.Data;

namespace DeathChest
{
    public class DeathChestWorldSystem : ModSystem
    {
        public override void OnWorldLoad()
        {
            DeathChestManager.Clear();
        }

        public override void OnWorldUnload()
        {
            DeathChestManager.Clear();
        }

        public override void SaveWorldData(TagCompound tag)
        {
            var chestsData = new List<TagCompound>();

            foreach (var kvp in DeathChestManager.GetAllChests())
            {
                // Skip invalid entries
                if (kvp.Value?.Items == null)
                    continue;

                // Filter out empty items
                var nonEmptyItems = kvp.Value.Items.Where(i => i != null && !i.IsAir).ToList();
                if (!nonEmptyItems.Any())
                    continue;

                var chestTag = new TagCompound
                {
                    ["items"] = nonEmptyItems,
                    ["playerGuid"] = kvp.Value.PlayerGuid.ToString(),
                    ["npcId"] = kvp.Key
                };

                chestsData.Add(chestTag);
            }

            tag["deathChests"] = chestsData;
            tag["version"] = 1;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            if (!tag.ContainsKey("deathChests"))
                return;

            var chestsData = tag.GetList<TagCompound>("deathChests");
            int version = tag.GetInt("version");

            foreach (var chestTag in chestsData)
            {
                try
                {
                    if (chestTag.TryGet("items", out List<Item> items) &&
                        chestTag.TryGet("playerGuid", out string guidString) &&
                        chestTag.TryGet("npcId", out int npcId) &&
                        Guid.TryParse(guidString, out Guid playerGuid))
                    {
                        // Only load data for existing active death chest NPCs
                        if (npcId >= 0 && npcId < Main.maxNPCs &&
                            Main.npc[npcId].active &&
                            Main.npc[npcId].type == ModContent.NPCType<DeathChestNPC>())
                        {
                            var itemArray = items.Where(i => i != null && !i.IsAir).ToArray();
                            if (itemArray.Length > 0)
                            {
                                DeathChestManager.RegisterChest(npcId, new DeathChestData(itemArray, playerGuid));
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logging.PublicLogger.Error($"Error loading death chest data: {e}");
                }
            }
        }

        public override void PreUpdateWorld()
        {
            // Clean up invalid entries periodically
            if (Main.GameUpdateCount % 600 == 0) // Every 10 seconds
            {
                var invalidChests = DeathChestManager.GetAllChests()
                    .Where(kvp => kvp.Value.IsEmpty)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var npcId in invalidChests)
                {
                    DeathChestNPC.ManuallyDespawn(npcId);

                    if (Main.netMode == NetmodeID.Server)
                    {
                        PacketHandler.SendRemoveChest(npcId);
                    }
                }
            }
        }
    }
}