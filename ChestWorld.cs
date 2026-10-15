using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DeathChest
{
	// One Death Chest in the world. The server (or single player) holds its items; a client knows only the header
	// (id, position, owner, time of death) until it opens the chest.
	public class DeathChestData
	{
		public const int Width = 40, Height = 56;

		public Guid Id;
		public Vector2 Bottom;           // world coordinates of the bottom center
		public string Owner = "";        // the owner's character name
		public Guid OwnerId;             // the owner's DeathChestPlayer.CharacterId
		public DateTime DeathTime;       // UTC
		public List<Item> Items = new();
		public List<string> Origins = new(); // per item: the slot it was equipped in at death (Equipment), or null

		public Rectangle Hitbox => new((int)Bottom.X - Width / 2, (int)Bottom.Y - Height, Width, Height);
		public Vector2 Center => Hitbox.Center.ToVector2();
		public bool IsEmpty => Items.TrueForAll(i => i.IsAir);
		public string OriginAt(int slot) => slot < Origins.Count ? Origins[slot] : null;
		public string DeathTimeText => DeathTime.ToLocalTime().ToString("g");
		public string HoverText => Language.GetTextValue("Mods.DeathChest.UI.Hover", Owner, DeathTimeText);

		// Private chests exist only for their owner: not drawn, no light, no hover, no map icon, can't be opened.
		public bool CanAccess(Player player) => !Config.Instance.PrivateDeathChests || IsOwner(player);
		public bool IsOwner(Player player) => player.GetModPlayer<DeathChestPlayer>().CharacterId == OwnerId;

		public TagCompound Save()
		{
			var kept = Enumerable.Range(0, Items.Count).Where(i => !Items[i].IsAir).ToList();
			return new TagCompound {
				["id"] = Id.ToString(), ["x"] = Bottom.X, ["y"] = Bottom.Y, ["owner"] = Owner, ["ownerId"] = OwnerId.ToString(),
				["deathTime"] = DeathTime.Ticks, ["items"] = kept.Select(i => Items[i]).ToList(), ["origins"] = kept.Select(i => OriginAt(i) ?? "").ToList(),
			};
		}

		public static DeathChestData Load(TagCompound t)
		{
			var c = new DeathChestData {
				Id = Guid.Parse(t.GetString("id")), Bottom = new Vector2(t.GetFloat("x"), t.GetFloat("y")), Owner = t.GetString("owner"),
				DeathTime = new DateTime(t.GetLong("deathTime"), DateTimeKind.Utc), Items = t.GetList<Item>("items").ToList(),
				Origins = t.GetList<string>("origins").Select(o => o == "" ? null : o).ToList(),
			};
			Guid.TryParse(t.GetString("ownerId"), out c.OwnerId);
			while (c.Origins.Count < c.Items.Count)
				c.Origins.Add(null);
			return c;
		}

		public void WriteHeader(BinaryWriter w)
		{
			w.Write(Id.ToByteArray());
			w.WriteVector2(Bottom);
			w.Write(Owner);
			w.Write(OwnerId.ToByteArray());
			w.Write(DeathTime.Ticks);
		}

		public static DeathChestData ReadHeader(BinaryReader r) => new() {
			Id = new Guid(r.ReadBytes(16)), Bottom = r.ReadVector2(), Owner = r.ReadString(), OwnerId = new Guid(r.ReadBytes(16)),
			DeathTime = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
		};
	}

	// The world's Death Chests: a plain list saved with the world, drawn and lit here. Nothing else in the game or in
	// other mods sees them, and there's no limit on how many there are.
	public class ChestWorld : ModSystem
	{
		public static readonly List<DeathChestData> Chests = new();

		private static Asset<Texture2D> _texture;

		public static DeathChestData Find(Guid id) => id == Guid.Empty ? null : Chests.Find(c => c.Id == id);

		public override void Load()
		{
			if (!Main.dedServ)
				_texture = ModContent.Request<Texture2D>("DeathChest/Assets/DeathChest");
		}

		public override void OnWorldUnload() => Chests.Clear();

		public override void SaveWorldData(TagCompound tag) => tag["chests"] = Chests.Where(c => !c.IsEmpty).Select(c => c.Save()).ToList();

		public override void LoadWorldData(TagCompound tag)
		{
			Chests.Clear();
			Chests.AddRange(tag.GetList<TagCompound>("chests").Select(DeathChestData.Load));
		}

		// Local player died: spawn here (single player) or hand the items to the server.
		public static void SpawnFor(Player player, List<Item> items, List<string> origins)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
				player.GetModPlayer<DeathChestPlayer>().HandOver(new PendingOp { Bottom = player.Bottom, Items = items, Origins = origins });
			else
				Spawn(player.Bottom, items, origins, player);
		}

		// Single player or server.
		public static void Spawn(Vector2 bottom, List<Item> items, List<string> origins, Player owner)
		{
			var chest = new DeathChestData {
				Id = Guid.NewGuid(), Bottom = bottom, Owner = owner.name, OwnerId = owner.GetModPlayer<DeathChestPlayer>().CharacterId,
				DeathTime = DateTime.UtcNow, Items = items, Origins = origins,
			};
			while (chest.Origins.Count < items.Count)
				chest.Origins.Add(null);
			Chests.Add(chest);
			if (Main.netMode == NetmodeID.Server)
				PacketHandler.BroadcastChestAdded(chest);
		}

		// Single player or server: an emptied chest goes away.
		public static void Remove(DeathChestData chest)
		{
			Chests.Remove(chest);
			for (int i = 0; i < Main.maxPlayers; i++) // nobody keeps a lock on a chest that's gone
				if (Main.player[i].active && Main.player[i].GetModPlayer<DeathChestPlayer>().OpenChestId == chest.Id)
					Main.player[i].GetModPlayer<DeathChestPlayer>().OpenChestId = Guid.Empty;
			if (Main.netMode == NetmodeID.Server)
				PacketHandler.BroadcastChestRemoved(chest.Id);
		}

		// Visible chests give off a soft pink light.
		public override void PostUpdateEverything()
		{
			if (Main.dedServ)
				return;
			foreach (var chest in Chests)
				if (chest.CanAccess(Main.LocalPlayer) && Main.LocalPlayer.Distance(chest.Center) < 2000f)
					Lighting.AddLight(chest.Center, Color.Pink.ToVector3());
		}

		public override void PostDrawTiles()
		{
			if (Chests.Count == 0 || _texture == null)
				return;
			var screen = new Rectangle((int)Main.screenPosition.X - 100, (int)Main.screenPosition.Y - 100, Main.screenWidth + 200, Main.screenHeight + 200);
			var frame = new Rectangle(0, 0, DeathChestData.Width, DeathChestData.Height);
			bool begun = false;
			foreach (var chest in Chests) {
				if (!chest.Hitbox.Intersects(screen) || !chest.CanAccess(Main.LocalPlayer))
					continue;
				if (!begun) {
					Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
					begun = true;
				}
				var box = chest.Hitbox;
				Color light = Lighting.GetColor((int)(chest.Center.X / 16f), (int)(chest.Center.Y / 16f));
				Main.spriteBatch.Draw(_texture.Value, new Vector2(box.X, box.Y) - Main.screenPosition, frame, light);
			}
			if (begun)
				Main.spriteBatch.End();
		}
	}
}
