using System.ComponentModel;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace DeathChest
{
	// Server-side: the server's value applies to everyone, and tModLoader sends every change to all clients at once,
	// so chests appear or hide without a reload.
	public class Config : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ServerSide;

		public static Config Instance => ModContent.GetInstance<Config>();

		[Header("Options")]
		[DefaultValue(true)]
		public bool PrivateDeathChests = true;

		// Only the host may change settings in multiplayer.
		public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
		{
			if (NetMessage.DoesPlayerSlotCountAsAHost(whoAmI))
				return true;
			message = NetworkText.FromKey("tModLoader.ModConfigRejectChangesNotHost");
			return false;
		}
	}
}
