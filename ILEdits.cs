using MonoMod.Cil;
using Mono.Cecil.Cil;
using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using DeathChest.NPCs;
using DeathChest.UI;
using Microsoft.Xna.Framework;
using System.Linq;
using DeathChest.Data;
using Terraria.DataStructures;
using System.Collections.Generic;
using Humanizer;
using Microsoft.CodeAnalysis.Simplification;
using System.Drawing;
using System.Numerics;
using System.Threading.Channels;
using Vector2 = Microsoft.Xna.Framework.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace DeathChest
{
    public static class ILEdits
    {
        private static readonly int[] StarterItems =
[
            ItemID.CopperShortsword,
            ItemID.CopperPickaxe,
            ItemID.CopperAxe,
];
        private static bool IsStarterItem(Item item)
        {
            return Array.IndexOf(StarterItems, item.type) != -1 && item.stack == 1;
        }
        public static void ApplyILEdits()
        {
            IL_Main.HoverOverNPCs += HookHoverOverNPCs;
            IL_Player.KillMe += ModifyItemDrop;
        }

        private static void HookHoverOverNPCs(ILContext il)
        {
            try
            {
                var cursor = new ILCursor(il);

                if (!cursor.TryGotoNext(MoveType.Before,
                    i => i.MatchLdloc(2),
                    i => i.MatchCallvirt("Terraria.NPC", "GetChat"),
                    i => i.MatchStsfld<Main>("npcChatText")))
                    return;

                var normalNpcLabel = cursor.DefineLabel();

                cursor.Emit(OpCodes.Ldloc_2);  // Load NPC instance
                cursor.EmitDelegate<Func<NPC, bool>>(npc =>
                {
                    if (npc.type != ModContent.NPCType<DeathChestNPC>())
                        return false;

                    var player = Main.LocalPlayer;
                    var modPlayer = player.GetModPlayer<DeathChestPlayer>();

                    if (Main.mouseRight)
                    {
                        if (npc.ModNPC is DeathChestNPC deathChest)
                        {
                            // Request access
                            if (Main.netMode == NetmodeID.MultiplayerClient)
                            {
                                PacketHandler.SendOpenRequest(npc.whoAmI);
                            }
                            else
                            {
                                modPlayer.OpenChest(npc.whoAmI);
                            }

                            Main.mouseRight = false;
                            Main.mouseRightRelease = false;
                        }
                    }

                    return true;
                });

                cursor.Emit(OpCodes.Brfalse, normalNpcLabel);
                cursor.Emit(OpCodes.Ret);
                cursor.MarkLabel(normalNpcLabel);
            }
            catch (Exception e)
            {
                Logging.PublicLogger.Error($"[Death Chest] Error in IL edit: {e}");
                MonoModHooks.DumpIL(ModContent.GetInstance<DeathChest>(), il);
            }
        }


        private static void ModifyItemDrop(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);

                // Find the myPlayer check followed by the inventory check sequence
                if (!c.TryGotoNext(MoveType.Before,
                    i => i.MatchLdsfld<Main>("myPlayer"),
                    i => i.MatchLdarg(0),
                    i => i.MatchLdfld<Entity>("whoAmI"),
                    i => i.MatchBneUn(out _)))
                {
                    Logging.PublicLogger.Error("[Death Chest] Could not find first myPlayer check - patch failed");
                    return;
                }

                // Move to the start of the item dropping code block
                if (!c.TryGotoNext(MoveType.Before,
                    i => i.MatchLdarg(0),
                    i => i.MatchLdfld<Player>("trashItem")))
                {
                    Logging.PublicLogger.Error("[Death Chest] Could not find trashItem access - patch failed");
                    return;
                }

                var jumpStart = c.Index;

                if (!c.TryGotoNext(MoveType.Before,
                    i => i.MatchLdloc(0),
                    i => i.MatchBrfalse(out _)))
                {
                    Logging.PublicLogger.Error("[Death Chest] Could not find end of item drop code - patch failed");
                    return;
                }

                var afterItemDropping = c.MarkLabel();
                c.Index = jumpStart;

                c.Emit(OpCodes.Ldarg_0); // Load Player instance
                c.EmitDelegate<Action<Player>>(player =>
                {
                    if (Main.myPlayer != player.whoAmI)
                        return;

                    try
                    {
                        var items = new List<Item>();

                        // Handle all inventory arrays
                        for (int i = 0; i < player.inventory.Length; i++)
                        {
                            if (player.inventory[i].stack > 0 && !IsStarterItem(player.inventory[i]))
                            {
                                items.Add(player.inventory[i].Clone());
                            }
                        }

                        // Handle armor
                        for (int i = 0; i < player.armor.Length; i++)
                        {
                            if (!player.armor[i].IsAir)
                            {
                                items.Add(player.armor[i].Clone());
                            }
                        }

                        // Handle dyes
                        for (int i = 0; i < player.dye.Length; i++)
                        {
                            if (!player.dye[i].IsAir)
                            {
                                items.Add(player.dye[i].Clone());
                            }
                        }

                        // Handle misc equipment
                        for (int i = 0; i < player.miscEquips.Length; i++)
                        {
                            if (!player.miscEquips[i].IsAir)
                            {
                                items.Add(player.miscEquips[i].Clone());
                            }
                        }

                        // Handle misc dyes
                        for (int i = 0; i < player.miscDyes.Length; i++)
                        {
                            if (!player.miscDyes[i].IsAir)
                            {
                                items.Add(player.miscDyes[i].Clone());
                            }
                        }

                        // Only spawn chest if there are items to store
                        if (items.Count > 0)
                        {
                            Vector2 spawnPos = player.position + new Vector2(32f, -32f);

                            if (Main.netMode == NetmodeID.MultiplayerClient)
                            {
                                PacketHandler.SendNewDeathChest(spawnPos, items.ToArray(),
                                    player.GetModPlayer<DeathChestPlayer>().PlayerGUID);
                                Main.NewText("Death Chest spawn requested...", Color.Yellow);
                                ClearAllItems(player);
                            }
                            else
                            {
                                int npcId = NPC.NewNPC(new EntitySource_SpawnNPC(), (int)spawnPos.X, (int)spawnPos.Y,
                                    ModContent.NPCType<DeathChestNPC>());

                                if (npcId != -1)
                                {
                                    DeathChestManager.RegisterChest(npcId,
                                        new DeathChestData(items.ToArray(), player.GetModPlayer<DeathChestPlayer>().PlayerGUID));
                                    ClearAllItems(player);
                                    Main.NewText("Death Chest spawned with your items", Color.Yellow);
                                }
                                else
                                {
                                    Main.NewText("Failed to spawn Death Chest", Color.Red);
                                    Logging.PublicLogger.Error("[Death Chest] Failed to spawn NPC");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.PublicLogger.Error($"[Death Chest] Error spawning Death Chest: {ex}");
                    }
                });

                c.Emit(OpCodes.Br, afterItemDropping);
                Logging.PublicLogger.Info("[Death Chest] Successfully patched KillMe to use Death Chests");
            }
            catch (Exception ex)
            {
                Logging.PublicLogger.Error($"[Death Chest] Error patching KillMe: {ex}");
                MonoModHooks.DumpIL(ModContent.GetInstance<DeathChest>(), il);
            }
        }

        private static void ClearAllItems(Player player)
        {
            // Clear main inventory
            for (int i = 0; i < player.inventory.Length; i++)
            {
                if (!IsStarterItem(player.inventory[i]))
                    player.inventory[i].TurnToAir(false);
            }

            // Clear armor
            for (int i = 0; i < player.armor.Length; i++)
            {
                    player.armor[i].TurnToAir(false);
            }

            // Clear dyes
            for (int i = 0; i < player.dye.Length; i++)
            {
                    player.dye[i].TurnToAir(false);
            }

            // Clear misc equipment
            for (int i = 0; i < player.miscEquips.Length; i++)
            {
                    player.miscEquips[i].TurnToAir(false);
            }

            // Clear misc dyes
            for (int i = 0; i < player.miscDyes.Length; i++)
            {
                    player.miscDyes[i].TurnToAir(false);
            }
        }
    }
}