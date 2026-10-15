using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;

namespace DeathChest
{
	// Pointing at a chest in the world: its name and time of death on hover, and right-click to open it in range.
	public class ChestInteraction : ModSystem
	{
		public const float OpenRange = 160f;

		public override void Load() => On_Main.DrawMouseOver += AfterVanillaMouseOver;

		// Main.DrawMouseOver is where vanilla shows NPC names and handles right-clicking NPCs (HoverOverNPCs);
		// chests are handled right after it, the same way.
		private static void AfterVanillaMouseOver(On_Main.orig_DrawMouseOver orig, Main self)
		{
			orig(self);
			var player = Main.LocalPlayer;
			if (ChestWorld.Chests.Count == 0 || Main.mouseText || player.mouseInterface || player.dead || player.ghost)
				return;

			// the mouse in world coordinates, with game zoom and reversed gravity, as vanilla's mouse-over does it
			PlayerInput.SetZoom_Unscaled();
			PlayerInput.SetZoom_MouseInWorld();
			var mouse = new Point((int)(Main.mouseX + Main.screenPosition.X), (int)(Main.mouseY + Main.screenPosition.Y));
			if (player.gravDir == -1f)
				mouse.Y = (int)Main.screenPosition.Y + Main.screenHeight - Main.mouseY;
			PlayerInput.SetZoom_UI();

			var chest = ChestWorld.Chests.Find(c => c.Hitbox.Contains(mouse) && c.CanAccess(player));
			if (chest == null)
				return;
			if (player.Distance(chest.Center) <= OpenRange) {
				Main.HoveringOverAnNPC = true; // vanilla's "pointing at something to interact with": right-click won't use the held item
				player.noThrow = 2;
				player.cursorItemIconEnabled = true;
				player.cursorItemIconID = ItemID.Chest;
				if (Main.mouseRight && Main.mouseRightRelease) {
					Main.mouseRightRelease = false;
					player.GetModPlayer<DeathChestPlayer>().RequestOpen(chest);
				}
			}
			Main.instance.MouseTextHackZoom(chest.HoverText);
			Main.mouseText = true;
		}
	}

	// Map and minimap icons, with the same text on hover.
	public class ChestMapLayer : ModMapLayer
	{
		private Asset<Texture2D> _icon;

		public override void Draw(ref MapOverlayDrawContext context, ref string text)
		{
			_icon ??= ModContent.Request<Texture2D>("DeathChest/Assets/DeathChestMap");
			foreach (var chest in ChestWorld.Chests) {
				if (!chest.CanAccess(Main.LocalPlayer))
					continue;
				if (context.Draw(_icon.Value, chest.Center / 16f, Color.White, new SpriteFrame(1, 1, 0, 0), 1f, 1.5f, Alignment.Center).IsMouseOver)
					text = chest.HoverText;
			}
		}
	}
}
