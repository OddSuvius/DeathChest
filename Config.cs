using System.ComponentModel;
using Terraria.ModLoader.Config;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using Microsoft.Xna.Framework;

namespace DeathChest
{
    public class Config : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ServerSide;

        [Header("Options")]
        [DefaultValue(false)]
        [Label("Public Death Chests")]
        [Tooltip("When enabled, all players can access each other's Death Chests")]
        public bool PublicDeathChests = true;

        public static LocalizedText RejectClientChangesMessage { get; private set; }

        // Static access to current config
        public static Config Instance => ModContent.GetInstance<Config>();

        public override void OnLoaded()
        {
            RejectClientChangesMessage = this.GetLocalization(nameof(RejectClientChangesMessage));
        }

        public override void OnChanged()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            if (Main.netMode == NetmodeID.Server)
            {
                string message = PublicDeathChests ?
                    "Death Chests are now public - all players can access them" :
                    "Death Chests are now private - only owners can access them";

                Logging.PublicLogger.Info($"[Death Chest - Server] {message}");

                // Notify all clients
                if (Mod.IsNetSynced)
                {
/*                    ModPacket packet = Mod.GetPacket();
                    packet.Write((byte)PacketHandler.MessageType.SyncConfig);
                    packet.Write(PublicDeathChests);
                    packet.Send(-1, -1);*/
                }
            }
            else
            {
                Color messageColor = PublicDeathChests ? new Color(150, 255, 150) : new Color(255, 150, 150);
                string message = PublicDeathChests ?
                    "Death Chests are now public" :
                    "Death Chests are now private";
                Main.NewText(message, messageColor);
            }
        }

        public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)

        {

            if (!(Main.countsAsHostForGameplay[whoAmI] && whoAmI == 0))

            {

                message = RejectClientChangesMessage.ToNetworkText();

                return false;

            }

            return true;
        }
    }
}