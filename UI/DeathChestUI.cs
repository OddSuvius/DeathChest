using System;
using System.Collections.Generic;
using DeathChest.NPCs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;

namespace DeathChest.UI
{
	// Drawn where a vanilla chest is drawn: a 10x4 grid under the inventory, the name and buttons to its right.
	// Any number of items fits: more than 40 scroll with the mouse wheel or the scrollbar beside the grid.
	// Items can only be taken out.
	public class DeathChestUI : UIState
	{
		private const int Cols = 10, Rows = 4;
		private const float Scale = 0.755f; // vanilla chest slot scale
		private const float GridX = 73f;

		private const float TextX = 516f; // right of the scrollbar

		private int _scrollRow;
		private bool _draggingScrollbar;
		private float _lootAllScale = 0.75f;
		private bool _lootAllHovered;

		public void Reset() => _scrollRow = 0;

		public override void Draw(SpriteBatch spriteBatch)
		{
			var player = Main.LocalPlayer;
			var chest = player.GetModPlayer<DeathChestPlayer>().OpenChest;
			if (chest == null)
				return;

			int top = Main.instance.invBottom;
			float step = 56f * Scale;
			int totalRows = Math.Max(Rows, (chest.Items.Count + Cols - 1) / Cols);
			Main.inventoryScale = Scale;

			if (!PlayerInput.IgnoreMouseInterface && Utils.FloatIntersect(Main.mouseX, Main.mouseY, 0f, 0f, GridX, top, Cols * step, Rows * step)) {
				player.mouseInterface = true;
				PlayerInput.LockVanillaMouseScroll("DeathChest/Grid");
				_scrollRow -= Math.Sign(PlayerInput.ScrollWheelDeltaForUI);
			}
			if (totalRows > Rows)
				Scrollbar(spriteBatch, GridX + Cols * step + 4f, top, Rows * step, totalRows);
			_scrollRow = Math.Clamp(_scrollRow, 0, totalRows - Rows);

			float slotSize = TextureAssets.InventoryBack.Width() * Scale;
			for (int y = 0; y < Rows; y++) {
				for (int x = 0; x < Cols; x++) {
					int slot = (y + _scrollRow) * Cols + x;
					var pos = new Vector2((int)(GridX + x * step), (int)(top + y * step));
					bool exists = slot < chest.Items.Count;
					if (exists && !PlayerInput.IgnoreMouseInterface && Utils.FloatIntersect(Main.mouseX, Main.mouseY, 0f, 0f, pos.X, pos.Y, slotSize, slotSize)) {
						player.mouseInterface = true;
						bool left = Main.mouseLeft && Main.mouseLeftRelease, right = Main.mouseRight && Main.mouseRightRelease;
						if (left)
							Main.mouseLeftRelease = false;
						if (right)
							Main.mouseRightRelease = false;
						if (HandleSlot(chest, slot, left, right, ItemSlot.ShiftInUse))
							SoundEngine.PlaySound(SoundID.Grab);
						Item hovered = chest.Items[slot];
						ItemSlot.MouseHover(ref hovered, ItemSlot.Context.ChestItem);
					}
					Item item = exists ? chest.Items[slot] : new Item();
					ItemSlot.Draw(spriteBatch, ref item, ItemSlot.Context.ChestItem, pos);
				}
			}

			DrawText(spriteBatch, Language.GetTextValue("Mods.DeathChest.UI.Title", chest.Owner), new Vector2(TextX, top), 1f, Color.White);
			DrawText(spriteBatch, Language.GetTextValue("Mods.DeathChest.UI.Died", chest.DeathTimeText), new Vector2(TextX, top + 24), 0.8f, Color.LightGray);
			if (Button(spriteBatch, Language.GetTextValue("Mods.DeathChest.UI.LootAll"), (int)TextX, top + 60, ref _lootAllScale, ref _lootAllHovered))
				LootAll(chest);
		}

		// Drag or click the track to scroll; the thumb's size shows how much of the chest is visible.
		private void Scrollbar(SpriteBatch spriteBatch, float x, float y, float height, int totalRows)
		{
			const float Width = 10f;
			float thumbHeight = Math.Max(12f, height * Rows / totalRows);
			bool hover = !PlayerInput.IgnoreMouseInterface && Utils.FloatIntersect(Main.mouseX, Main.mouseY, 0f, 0f, x, y, Width, height);
			if (hover) {
				Main.LocalPlayer.mouseInterface = true;
				if (Main.mouseLeft && Main.mouseLeftRelease) {
					_draggingScrollbar = true;
					Main.mouseLeftRelease = false;
				}
			}
			if (!Main.mouseLeft)
				_draggingScrollbar = false;
			if (_draggingScrollbar) {
				float t = (Main.mouseY - y - thumbHeight / 2f) / (height - thumbHeight);
				_scrollRow = (int)Math.Round(Math.Clamp(t, 0f, 1f) * (totalRows - Rows));
			}
			float thumbY = y + (height - thumbHeight) * Math.Clamp(_scrollRow, 0, totalRows - Rows) / (totalRows - Rows);
			var pixel = TextureAssets.MagicPixel.Value;
			spriteBatch.Draw(pixel, new Rectangle((int)x, (int)y, (int)Width, (int)height), new Color(30, 30, 60, 180));
			spriteBatch.Draw(pixel, new Rectangle((int)x + 1, (int)thumbY, (int)Width - 2, (int)thumbHeight),
				hover || _draggingScrollbar ? new Color(200, 200, 255) : new Color(140, 140, 200));
		}

		// Moves as much as fits into the inventory; the rest stays in the chest.
		public static void LootAll(DeathChestNPC chest)
		{
			var space = new InventorySpace(Main.LocalPlayer);
			bool any = false;
			for (int i = 0; i < chest.Items.Count; i++) {
				int n = space.Reserve(chest.Items[i]);
				if (n > 0) {
					RequestTake(chest, i, n, toMouse: false);
					any = true;
				}
			}
			if (any)
				SoundEngine.PlaySound(SoundID.Grab);
		}

		// Single player: move the items right away. Multiplayer: ask the server, which sends them (PacketHandler).
		private static void RequestTake(DeathChestNPC chest, int slot, int count, bool toMouse)
		{
			var me = Main.LocalPlayer.GetModPlayer<DeathChestPlayer>();
			if (Main.netMode == NetmodeID.MultiplayerClient) {
				me.Outstanding++;
				PacketHandler.SendTake(chest.ChestId, slot, count, toMouse);
				return;
			}
			me.Receive(new[] { ChestOps.Take(chest.Items, slot, count) }, toMouse);
			if (chest.IsEmpty)
				chest.Remove();
		}

		// Take-only: items come out of a Death Chest, nothing goes in.
		// Left click: take the stack onto the empty cursor; shift-click: move to inventory; right click: take one.
		// Returns true if a take was made (or requested).
		internal static bool HandleSlot(DeathChestNPC chest, int slot, bool left, bool right, bool shift)
		{
			Item item = chest.Items[slot];
			if (item.IsAir)
				return false;
			if (left && shift) {
				int n = new InventorySpace(Main.LocalPlayer).Reserve(item);
				if (n <= 0)
					return false;
				RequestTake(chest, slot, n, toMouse: false);
				return true;
			}
			if (left) {
				if (!Main.mouseItem.IsAir)
					return false; // holding something: nothing can be put in
				RequestTake(chest, slot, item.stack, toMouse: true);
				return true;
			}
			if (right) {
				var mouse = Main.mouseItem;
				if (!mouse.IsAir && (mouse.type != item.type || mouse.stack >= mouse.maxStack || !ItemLoader.CanStack(mouse, item)))
					return false; // different item, or the held stack is full or refuses it
				RequestTake(chest, slot, 1, toMouse: true);
				return true;
			}
			return false;
		}

		private static void DrawText(SpriteBatch spriteBatch, string text, Vector2 pos, float scale, Color color) =>
			ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, text, pos, color, 0f, Vector2.Zero, new Vector2(scale));

		// A vanilla-style chest button (grows on hover). Returns true on click.
		private static bool Button(SpriteBatch spriteBatch, string text, int x, int y, ref float scale, ref bool hovered)
		{
			Vector2 size = FontAssets.MouseText.Value.MeasureString(text);
			bool hover = !PlayerInput.IgnoreMouseInterface && Utils.FloatIntersect(Main.mouseX, Main.mouseY, 0f, 0f, x, y - 12, size.X, 24f);
			if (hover && !hovered)
				SoundEngine.PlaySound(SoundID.MenuTick);
			hovered = hover;
			scale = hover ? Math.Min(scale + 0.05f, 1f) : Math.Max(scale - 0.05f, 0.75f);

			Color color = hover ? Main.OurFavoriteColor : Color.White * (0.97f * (1f - (255f - Main.mouseTextColor) / 255f * 0.5f));
			color.A = 255;
			ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, text, new Vector2(x, y), color, 0f,
				new Vector2(0f, size.Y / 2f), new Vector2(scale), -1f, 1.5f);

			if (!hover)
				return false;
			Main.LocalPlayer.mouseInterface = true;
			if (!Main.mouseLeft || !Main.mouseLeftRelease)
				return false;
			Main.mouseLeftRelease = false;
			return true;
		}
	}
}
