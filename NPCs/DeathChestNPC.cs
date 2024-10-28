using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using DeathChest.Data;
using System.Linq;
using System;
using static DeathChest.PacketHandler;
using Terraria.ModLoader.IO;

namespace DeathChest.NPCs
{
    [AutoloadHead]
    public class DeathChestNPC : ModNPC
    {
        private const float LIGHT_INTENSITY = 1f;
        private static readonly Color LIGHT_COLOR = Color.Pink;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 2;
            NPCID.Sets.NoTownNPCHappiness[Type] = true;
            NPCID.Sets.ActsLikeTownNPC[Type] = true;
            NPCID.Sets.DontDoHardmodeScaling[Type] = true;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
            NPCID.Sets.TownCritter[Type] = false;
        }

        public override void SetDefaults()
        {
            NPC.width = 40;
            NPC.height = 56;
            NPC.lifeMax = 1;
            NPC.life = 1;
            NPC.immortal = true;
            NPC.dontTakeDamage = true;
            NPC.friendly = true;
            NPC.noGravity = true;
            NPC.noTileCollide = true;
            NPC.dontCountMe = true;
            NPC.npcSlots = 0f;
            NPC.aiStyle = -1;
            NPC.townNPC = true;
            TownNPCStayingHomeless = true;

            // Extra settings to prevent despawn
            NPC.behindTiles = true;
            NPC.netAlways = true;
            NPC.boss = false;

#pragma warning disable CS0618 // Type or member is obsolete
            NPCID.Sets.NPCBestiaryDrawModifiers value = new(0)
            {
                Hide = true
            };
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public override void AI()
        {
            Lighting.AddLight(NPC.Center, LIGHT_COLOR.ToVector3() * LIGHT_INTENSITY);
        }

        public override bool? CanBeHitByProjectile(Projectile projectile) => false;
        public override bool? CanBeHitByItem(Player player, Item item) => false;
        public override bool CheckDead() => false;
        public override bool NeedSaving() => true;

        public bool CanPlayerAccess(Player player)
        {
            if (player == null || !player.active)
                { return false; }

            if (Main.netMode == NetmodeID.SinglePlayer)
                { return true; }

            var modPlayer = player.GetModPlayer<DeathChestPlayer>();

            if (!modPlayer.IsInRangeGeneral(NPC.whoAmI))
                { return false; }

            var data = DeathChestManager.GetChestItems(NPC.whoAmI);

            if (Config.Instance.PublicDeathChests || data?.PlayerGuid == modPlayer.PlayerGUID){
                return true;
            }
            return true;
        }

        public override void SaveData(TagCompound tag)
        {
            // Save chest data if it exists
            var data = DeathChestManager.GetChestItems(NPC.whoAmI);
            if (data != null)
            {
                tag["chestData"] = data.Save();
            }
        }

        public override void LoadData(TagCompound tag)
        {
            if (tag.TryGet("chestData", out TagCompound chestData))
            {
                var data = DeathChestData.Load(chestData);
                if (data != null)
                {
                    DeathChestManager.RegisterChest(NPC.whoAmI, data);
                }
            }
        }


        public override void OnSpawn(IEntitySource source)
        {
            if (Main.netMode == NetmodeID.Server)
            {
                NPC.netUpdate = true;
                Logging.PublicLogger.Debug($"[Death Chest] NPC spawned on server with ID: {NPC.whoAmI}");
            }
        }


        public static void ManuallyDespawn(int npcId)
        {
            DeathChestManager.RemoveChest(npcId);

            if (npcId >= 0 && npcId < Main.maxNPCs &&
                Main.npc[npcId].active &&
                Main.npc[npcId].type == ModContent.NPCType<DeathChestNPC>())
            {
                Main.npc[npcId].active = false;

                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, npcId);
            }
        }
    }
}