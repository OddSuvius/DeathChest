using System;
using System.IO;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Microsoft.Xna.Framework;
using DeathChest.Data;
using DeathChest.UI;
using DeathChest.NPCs;
using Terraria.Audio;
using System.Linq;
using Terraria.DataStructures;

namespace DeathChest
{
    public static class PacketHandler
    {
        public enum MessageType : byte
        {
            RequestOpenChest,
            ChestOpenResponse,
            CloseChest,
            UpdateChestItem,
            RemoveChest,
            RequestNewDeathChest,  // Client -> Server request
            SyncDeathChestSpawn,   // Server -> Client complete data
            SyncDeathChestData     // Server -> Client data only
        }

        public static void HandlePacket(BinaryReader reader, int whoAmI)
        {
            try
            {
                MessageType msgType = (MessageType)reader.ReadByte();
                Logging.PublicLogger.Debug($"[Death Chest] Handling packet type: {msgType} from {whoAmI}");

                switch (msgType)
                {
                    case MessageType.RequestNewDeathChest:
                        HandleNewDeathChest(reader, whoAmI);
                        break;
                    case MessageType.SyncDeathChestSpawn:
                        HandleSyncDeathChestSpawn(reader);
                        break;
                    case MessageType.RequestOpenChest:
                        HandleOpenRequest(reader, whoAmI);
                        break;
                    case MessageType.ChestOpenResponse:
                        HandleOpenResponse(reader);
                        break;
                    case MessageType.CloseChest:
                        HandleCloseChest(reader);
                        break;
                    case MessageType.UpdateChestItem:
                        HandleUpdateChestItem(reader, whoAmI);
                        break;
                    case MessageType.RemoveChest:
                        HandleRemoveChest(reader);
                        break;
                    default:
                        Logging.PublicLogger.Error($"[Death Chest] Unknown packet type: {msgType}");
                        break;
                }
            }
            catch (Exception e)
            {
                Logging.PublicLogger.Error($"[Death Chest] Error handling packet: {e}");
            }
        }

        // Send Methods
        public static ModPacket PreparePacket(MessageType type)
        {
            var packet = ModContent.GetInstance<DeathChest>().GetPacket();
            packet.Write((byte)type);
            return packet;
        }

        public static void SendOpenRequest(int npcId)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = PreparePacket(MessageType.RequestOpenChest);
            packet.Write(npcId);
            packet.Send();

            Logging.PublicLogger.Debug($"[Death Chest] Sent open request for NPC {npcId}");
        }


        private static void HandleOpenRequest(BinaryReader reader, int whoAmI)
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            int npcId = reader.ReadInt32();
            Logging.PublicLogger.Debug($"[Death Chest] Received open request for NPC {npcId} from player {whoAmI}");

            // Verify NPC exists and is active
            if (npcId < 0 || npcId >= Main.maxNPCs || !Main.npc[npcId].active ||
                Main.npc[npcId].type != ModContent.NPCType<DeathChestNPC>())
            {
                Logging.PublicLogger.Error($"[Death Chest] Invalid NPC ID: {npcId}");
                return;
            }

            // Verify chest data exists
            var data = DeathChestManager.GetChestItems(npcId);
            if (data == null)
            {
                Logging.PublicLogger.Error($"[Death Chest] No data found for NPC {npcId}");
                // Optional: Resync chest data to client
                return;
            }

            var packet = PreparePacket(MessageType.ChestOpenResponse);
            packet.Write(npcId);
            packet.Send(whoAmI);

            Logging.PublicLogger.Debug($"[Death Chest] Sent open confirmation for NPC {npcId} to player {whoAmI}");
        }

        public static void SendNewDeathChest(Vector2 position, Item[] items, Guid playerGuid)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            // Filter out any null or air items first
            var validItems = items.Where(i => i != null && !i.IsAir).ToArray();

            ModPacket packet = PreparePacket(MessageType.RequestNewDeathChest);

            // Send spawn position
            packet.Write(position.X);
            packet.Write(position.Y);

            // Send player GUID
            packet.Write(playerGuid.ToString());

            // Send items count and data
            packet.Write((byte)validItems.Length);
            foreach (var item in validItems)
            {
                ItemIO.Send(item.Clone(), packet, true, true);
            }

            packet.Send();

            Logging.PublicLogger.Info($"[Death Chest] Client requested chest spawn at: {position} with {validItems.Length} items");
        }

        private static void HandleOpenResponse(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            int npcId = reader.ReadInt32();
            var player = Main.LocalPlayer.GetModPlayer<DeathChestPlayer>();
            player.OpenChest(npcId);
        }



        public static void SendCloseChest(int npcId)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = PreparePacket(MessageType.CloseChest);
            packet.Write(npcId);
            packet.Send();
        }

        private static void HandleCloseChest(BinaryReader reader)
        {
            int npcId = reader.ReadInt32();

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                var player = Main.LocalPlayer.GetModPlayer<DeathChestPlayer>();
                if (player.GetActiveChest() == npcId)
                    player.CloseChest();
            }
        }

        public static void SendUpdateChestItem(int npcId, int slot, Item item)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = PreparePacket(MessageType.UpdateChestItem);
            packet.Write(npcId);
            packet.Write(slot);
            ItemIO.Send(item ?? new Item(), packet, true, true);
            packet.Send();

            Logging.PublicLogger.Debug($"[Death Chest] Sent item update for NPC {npcId}, slot {slot}");
        }

        private static void HandleUpdateChestItem(BinaryReader reader, int whoAmI)
        {
            int npcId = reader.ReadInt32();
            int slot = reader.ReadInt32();
            Item item = ItemIO.Receive(reader, true, true);

            var data = DeathChestManager.GetChestItems(npcId);
            if (data == null)
                return;

            if (slot >= 0 && slot < data.Items.Length)
                data.Items[slot] = item;

            // If on server, relay to other clients
            if (Main.netMode == NetmodeID.Server)
            {
                var packet = PreparePacket(MessageType.UpdateChestItem);
                packet.Write(npcId);
                packet.Write(slot);
                ItemIO.Send(item, packet, true, true);
                packet.Send(-1, whoAmI);  // Send to all clients except sender

                // Check if chest is now empty
                if (data.IsEmpty)
                {
                    DeathChestNPC.ManuallyDespawn(npcId);
                    SendRemoveChest(npcId);
                }
            }
        }
        public static void HandleNewDeathChest(BinaryReader reader, int whoAmI)
        {
            try
            {
                if (Main.netMode == NetmodeID.Server)
                {
                    // Read spawn request from client
                    float posX = reader.ReadSingle();
                    float posY = reader.ReadSingle();
                    string guidString = reader.ReadString();
                    byte itemCount = reader.ReadByte();

                    if (!Guid.TryParse(guidString, out Guid playerGuid))
                    {
                        Logging.PublicLogger.Error("[Death Chest] Invalid GUID in spawn packet");
                        return;
                    }

                    var items = new Item[itemCount];
                    for (int i = 0; i < itemCount; i++)
                        items[i] = ItemIO.Receive(reader, true, true);

                    // Spawn NPC
                    var position = new Vector2(posX, posY);
                    int npcId = NPC.NewNPC(new EntitySource_SpawnNPC(), (int)position.X, (int)position.Y,
                        ModContent.NPCType<DeathChestNPC>());

                    if (npcId == -1)
                    {
                        Logging.PublicLogger.Error("[Death Chest] Failed to spawn NPC on server");
                        return;
                    }

                    // Register data on server
                    DeathChestManager.RegisterChest(npcId, new DeathChestData(items, playerGuid));

                    // First sync NPC
                    NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, npcId);

                    // Then sync chest data to all clients
                    var syncPacket = PreparePacket(MessageType.SyncDeathChestSpawn);
                    syncPacket.Write(npcId);
                    syncPacket.Write(guidString);
                    syncPacket.Write(itemCount);
                    foreach (var item in items)
                        ItemIO.Send(item, syncPacket, true, true);
                    syncPacket.Send(-1); // Send to all clients

                    Logging.PublicLogger.Info($"[Death Chest] Server spawned and synced chest ID: {npcId}");
                }
            }
            catch (Exception e)
            {
                Logging.PublicLogger.Error($"[Death Chest] Error handling new chest spawn: {e}");
            }
        }

        // Add this new handler
        private static void HandleSyncDeathChestSpawn(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            try
            {
                int npcId = reader.ReadInt32();
                string guidString = reader.ReadString();
                byte itemCount = reader.ReadByte();

                if (!Guid.TryParse(guidString, out Guid playerGuid))
                {
                    Logging.PublicLogger.Error("[Death Chest] Invalid GUID in sync packet");
                    return;
                }

                var items = new Item[itemCount];
                for (int i = 0; i < itemCount; i++)
                    items[i] = ItemIO.Receive(reader, true, true);

                // Register on client
                DeathChestManager.RegisterChest(npcId, new DeathChestData(items, playerGuid));
                Logging.PublicLogger.Info($"[Death Chest] Client received and registered chest data for NPC {npcId}");
            }
            catch (Exception e)
            {
                Logging.PublicLogger.Error($"[Death Chest] Error handling chest sync: {e}");
            }
        }

        public static void SendRemoveChest(int npcId)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = PreparePacket(MessageType.RemoveChest);
            packet.Write(npcId);
            packet.Send();
        }

        private static void HandleRemoveChest(BinaryReader reader)
        {
            int npcId = reader.ReadInt32();
            DeathChestNPC.ManuallyDespawn(npcId);
        }
    }
}