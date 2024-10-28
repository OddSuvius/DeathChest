using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Microsoft.Xna.Framework;
using DeathChest.NPCs;
using DeathChest.UI;
using Terraria.ID;
using Terraria.Audio;
using DeathChest.Data;

namespace DeathChest
{
    public class DeathChestPlayer : ModPlayer
    {
        private const float MAX_INTERACTION_RANGE = 200f;

        private bool _chestOpen;
        private int _activeChestId = -1;

        public Guid PlayerGUID { get; private set; }
        public static DeathChestPlayer LocalPlayer => Main.LocalPlayer.GetModPlayer<DeathChestPlayer>();
        public bool IsChestOpen() => _chestOpen;
        public int GetActiveChest() => _activeChestId;

        public override void Initialize()
        {
            if (PlayerGUID == Guid.Empty)
                PlayerGUID = Guid.NewGuid();
            CloseChest();
        }

        public override void PreSavePlayer() => CloseChest();
        public override void PlayerDisconnect() => CloseChest();
        public override void UpdateDead() => CloseChest();

        public override void SaveData(TagCompound tag)
        {
            tag["playerGUID"] = PlayerGUID.ToString();
        }

        public override void LoadData(TagCompound tag)
        {
            if (tag.TryGet("playerGUID", out string guidString) &&
                Guid.TryParse(guidString, out Guid guid))
            {
                PlayerGUID = guid;
            }
            else if (PlayerGUID == Guid.Empty)
            {
                PlayerGUID = Guid.NewGuid();
            }
        }

        public override void ResetEffects()
        {
            if (Player.dead)
            {
                CloseChest();
                return;
            }

            if (Player.CCed || Main.gamePaused)
            {
                if (_chestOpen)
                {
                    CloseChest();
                    return;
                }
            }


/*            // Range check if an active chest
            if (_activeChestId >= 0 && _activeChestId < Main.maxNPCs)
            {
                var npc = Main.npc[_activeChestId];
                if (!npc.active || !IsInRange(npc))
                {
                    CloseChest();
                    Main.NewText("Death Chest is too far away!", Color.Red);
                }
            }*/
        }

        public void OpenChest(int npcId)
        {
            if (npcId < 0 || npcId >= Main.npc.Length || Main.npc[npcId] == null || Main.npc[npcId]?.active == false)
            {
                Logging.PublicLogger.Error($"[Death Chest] Invalid npcId: {npcId}");
                return;
            }
            var npc = Main.npc[npcId].ModNPC as DeathChestNPC;
            if (npc == null)
            {
                Logging.PublicLogger.Error($"[Death Chest] NPC {npcId} is not a Death Chest");
                return;
            }

                if (!npc.CanPlayerAccess(Player))
            {
                    Main.NewText("You cannot access this Death Chest!", Color.Red);
                    return;
                }

                var data = DeathChestManager.GetChestItems(npcId);
                if (data == null)
                {
                    Logging.PublicLogger.Debug($"[Death Chest] No data found for NPC {npcId}");
                    return;
                }

                _activeChestId = npcId;
                _chestOpen = true;

                // Set proper NPC talk state
                Player.SetTalkNPC(npcId);
                Main.playerInventory = true;
                SoundEngine.PlaySound(SoundID.MenuOpen);

                if (Main.myPlayer == Player.whoAmI)
                {
                    var ui = ModContent.GetInstance<UISystem>();
                    ui.ShowUI();
                    ui.DeathChestUIInstance?.AddRemoveItems(true, data.Items, npcId);
                }

                Logging.PublicLogger.Debug($"[Death Chest] Successfully opened chest {npcId}");
            }


        public void CloseChest()
        {
            // Only play sound if chest was actually open
            bool wasOpen = _chestOpen;
            _chestOpen = false;
            Player.SetTalkNPC(-1);

            if (Main.netMode == NetmodeID.MultiplayerClient && _activeChestId != -1)
            {
                PacketHandler.SendCloseChest(_activeChestId);
            }

            var ui = ModContent.GetInstance<UISystem>();
            ui?.HideUI();

            // Only play sound if chest was actually open
            if (wasOpen)
            {
                SoundEngine.PlaySound(SoundID.MenuClose);
            }

            _activeChestId = -1;
        }



        public bool IsInRangeGeneral(int npcId)
        {
            if (!ValidateNPC(npcId))
                return false;

            return IsInRange(Main.npc[npcId]);
        }

        private static bool ValidateNPC(int npcId)
        {
            return npcId >= 0 && npcId < Main.maxNPCs &&
                   Main.npc[npcId].active &&
                   Main.npc[npcId].type == ModContent.NPCType<DeathChestNPC>();
        }

        private bool IsInRange(NPC npc)
        {
            float distanceSquared = Vector2.DistanceSquared(Player.Center, npc.Center);
            return distanceSquared <= MAX_INTERACTION_RANGE * MAX_INTERACTION_RANGE;
        }
    }
}