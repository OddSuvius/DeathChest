using Terraria.ID;
using Terraria;
using Terraria.ModLoader;
using DeathChest.NPCs;
using Microsoft.Xna.Framework;
using DeathChest.Data;
using Terraria.DataStructures;

namespace DeathChest
{
    public class CopperSword : GlobalItem
    {
/*        public override bool? UseItem(Item item, Player player)
        {
            if (item.type == ItemID.CopperShortsword)
            {
                if (Main.myPlayer != player.whoAmI)
                    return true;

                // Create test items
                var items = new Item[5];
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = new Item();
                    switch (i)
                    {
                        case 0:
                            items[i].SetDefaults(ItemID.GoldCoin);
                            items[i].stack = 50;
                            break;
                        case 1:
                            items[i].SetDefaults(ItemID.HealingPotion);
                            items[i].stack = 10;
                            break;
                        case 2:
                            items[i].SetDefaults(ItemID.WoodenSword);
                            items[i].stack = 1;
                            break;
                        case 3:
                            items[i].SetDefaults(ItemID.Torch);
                            items[i].stack = 99;
                            break;
                        case 4:
                            items[i].SetDefaults(ItemID.MagicMirror);
                            items[i].stack = 1;
                            break;
                    }
                }

                // Spawn position slightly offset from player
                Vector2 spawnPos = player.position + new Vector2(32f, -32f);

                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    // Send spawn request to server
                    PacketHandler.SendNewDeathChest(spawnPos, items, player.GetModPlayer<DeathChestPlayer>().PlayerGUID);
                    Main.NewText("Sent spawn request to server", Color.Yellow);
                }
                else
                {
                    // Direct spawn in singleplayer
                    int npcId = NPC.NewNPC(new EntitySource_SpawnNPC(), (int)spawnPos.X, (int)spawnPos.Y,
                        ModContent.NPCType<DeathChestNPC>());

                    if (npcId != -1)
                    {
                        var guid = player.GetModPlayer<DeathChestPlayer>().PlayerGUID;
                        DeathChestManager.RegisterChest(npcId, new DeathChestData(items, guid));
                        Main.NewText($"Spawned Death Chest (ID: {npcId})", Color.Yellow);
                    }
                    else
                        Main.NewText("Failed to spawn Death Chest", Color.Red);
                }

                return true;
            }
            return base.UseItem(item, player);
        }*/
    }
}