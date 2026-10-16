using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;

namespace DeathChest
{
	// Pointing at a chest in the world: its name and time of death on hover, and right-click to open it in range.
	// With smart cursor or a gamepad, the nearest chest in range is targeted instead, like vanilla smart interact,
	// and the interact button opens it. A selected chest (under the mouse and in range, or targeted) gets vanilla's
	// yellow selection outline. Only the mouse directly on a chest shows the icon and tooltip, as for vanilla chests.
	public class ChestInteraction : ModSystem
	{
		public const float OpenRange = 160f;

		// The chest smart cursor or the gamepad would open now, or null.
		public static DeathChestData SmartTarget;

		// The chest under the mouse and in range (set while drawing the mouse-over; used for the outline and the cursor
		// icon), or null.
		public static DeathChestData Hovered;

		private static Asset<Texture2D> _icon;

		public override void Load()
		{
			On_Main.DrawMouseOver += AfterVanillaMouseOver;
			if (!Main.dedServ)
				_icon = ModContent.Request<Texture2D>("DeathChest/Assets/DeathChest_Icon");
		}

		// The Death Chest icon at the cursor, where and how vanilla draws the chest icon when you point at a chest
		// (Main.DrawInterface_40_InteractItemIcon: top left at the mouse + 10, at cursor scale). Drawn by a UI layer
		// right after vanilla's (UISystem).
		public static void DrawCursorIcon()
		{
			if (Hovered != null && _icon != null && !Main.LocalPlayer.mouseInterface)
				Main.spriteBatch.Draw(_icon.Value, new Vector2(Main.mouseX + 10, Main.mouseY + 10), null, Color.White, 0f, Vector2.Zero, Main.cursorScale, SpriteEffects.None, 0f);
		}

		// The name and time of death in the box vanilla draws behind item tooltips (the way tModLoader's
		// UICommon.TooltipMouseText does it: a name-only item tooltip, or plain text if the player turned the box off),
		// moved so it starts right of the icon and is centred on it.
		private static void Tooltip(string text, bool besideIcon)
		{
			int x = -1, y = -1;
			if (besideIcon && _icon != null) {
				// fixed at the icon's largest size: the icon pulses with Main.cursorScale (0.8 to 1.1), the tooltip stays still
				Vector2 icon = _icon.Size() * 1.1f;
				Vector2 textSize = FontAssets.MouseText.Value.MeasureString(text);
				x = Main.mouseX + 10 + (int)icon.X;                                  // vanilla adds 10 to these
				y = Main.mouseY + 10 + (int)(icon.Y / 2f - textSize.Y / 2f) - 10 - 8;
			}
			if (Main.SettingsEnabled_OpaqueBoxBehindTooltips) {
				var tip = new Item();
				tip.SetDefaults(0, noMatCheck: true);
				tip.SetNameOverride(text);
				tip.type = 1;
				tip.scale = 0f;
				tip.rare = 0;
				tip.value = -1;
				Main.HoverItem = tip;
				Main.instance.MouseText("", 0, 0, x, y);
			}
			else {
				Main.instance.MouseText(text, 0, 0, x, y);
			}
			Main.mouseText = true;
		}

		// Called from DeathChestPlayer.SetControls, before vanilla handles the interact button. Vanilla's own smart
		// target (a chest tile, an NPC, ...) wins; otherwise the nearest Death Chest the player may open.
		public static void UpdateSmartTarget(Player player)
		{
			SmartTarget = null;
			if (!(Main.SmartCursorIsUsed || PlayerInput.UsingGamepad) || Main.SmartInteractShowingGenuine || player.dead || player.ghost)
				return;
			float best = OpenRange;
			foreach (var chest in ChestWorld.Chests) {
				float d = player.Distance(chest.Center);
				if (d <= best && chest.CanAccess(player)) {
					best = d;
					SmartTarget = chest;
				}
			}
		}

		// Main.DrawMouseOver is where vanilla shows NPC names and handles right-clicking NPCs (HoverOverNPCs);
		// chests are handled right after it, the same way.
		private static void AfterVanillaMouseOver(On_Main.orig_DrawMouseOver orig, Main self)
		{
			orig(self);
			Hovered = null;
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

			// A smart-cursor / gamepad target gets only its outline; the icon and tooltip are for the mouse on a chest.
			var chest = ChestWorld.Chests.Find(c => c.Hitbox.Contains(mouse) && c.CanAccess(player));
			if (chest == null)
				return;
			bool inRange = player.Distance(chest.Center) <= OpenRange;
			if (inRange) {
				Hovered = chest;
				Main.HoveringOverAnNPC = true; // vanilla's "pointing at something to interact with": right-click won't use the held item
				player.noThrow = 2;
				if (Main.mouseRight && Main.mouseRightRelease) {
					Main.mouseRightRelease = false;
					player.GetModPlayer<DeathChestPlayer>().RequestOpen(chest);
				}
			}
			Tooltip(chest.HoverText, besideIcon: inRange);
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
