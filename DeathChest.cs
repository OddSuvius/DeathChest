using System;
using System.IO;
using Terraria.ID;
using Terraria.Localization;
using Terraria;
using Terraria.ModLoader;


namespace DeathChest
{
    public class DeathChest : Mod
    {
        public override void Load()
        {
            ILEdits.ApplyILEdits();
        }

        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            try
            {
                PacketHandler.HandlePacket(reader, whoAmI);
            }
            catch (Exception e)
            {
                Logging.PublicLogger.Error($"Error handling packet: {e}");
                if (Main.netMode == NetmodeID.Server)
                {
                    NetMessage.TrySendData(MessageID.ChatText, whoAmI, -1,
                        NetworkText.FromLiteral("Error processing Death Chest action"));
                }
            }
        }
    }
}