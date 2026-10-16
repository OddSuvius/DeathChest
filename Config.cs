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

		// Only the host (the Host & Play client) may change these settings in multiplayer. On a dedicated server
		// no client counts as the host, so the settings are changed in the server's config file instead.
		public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
		{
			if (NetMessage.DoesPlayerSlotCountAsAHost(whoAmI))
				return true;
			message = NetworkText.FromKey("tModLoader.ModConfigRejectChangesNotHost");
			return false;
		}

		[Header("$Mods.DeathChest.Configs.Config.Headers.DeathChests")]
		[LabelKey("$Mods.DeathChest.Configs.Config.PrivateDeathChests.Label"), TooltipKey("$Mods.DeathChest.Configs.Config.PrivateDeathChests.Tooltip")]
		[DefaultValue(true)]
		[BackgroundColor(45, 110, 115)]
		public bool PrivateDeathChests { get; set; } = true;
	}
}
