using System;
using System.Collections.Generic;
using System.Linq;
using DeathChest.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DeathChest
{
	public class DeathChestPlayer : ModPlayer
	{
		public const float Range = 200f;

		// Identifies this character. Saved in the player file and sent to the server on join.
		// Death Chests remember it, so only this character can see and open its private chests.
		public Guid CharacterId;

		// Multiplayer bookkeeping, saved with the character together with its inventory, so a dropped connection can
		// neither lose nor duplicate items:
		// - Pending: deaths this character sent to a server that the server hasn't confirmed.
		//   They're already out of the inventory. Resent on the next join to that world; the server skips any it applied.
		// - LastGrant: per world, the last item delivery from the server that this character received. The server resends
		//   deliveries until confirmed; this keeps a resend from being received twice.
		public long NextTxn = 1;
		public List<PendingOp> Pending = new();
		public Dictionary<Guid, long> LastGrant = new();

		// Client: requests sent and not yet answered (tests wait for 0).
		public int Outstanding;

		// On a client: the chest this player has open. On the server: the chest the server let this player open.
		public Guid OpenChestId;

		public static Guid WorldId => Main.ActiveWorldFileData.UniqueId;

		public override void Initialize()
		{
			CharacterId = Guid.NewGuid(); // a new character; LoadData overwrites it for saved ones
			NextTxn = 1;
			Pending = new();
			LastGrant = new();
		}

		public override void SaveData(TagCompound tag)
		{
			tag["characterId"] = CharacterId.ToString();
			tag["nextTxn"] = NextTxn;
			tag["pending"] = Pending.Select(p => p.Save()).ToList();
			tag["lastGrant"] = LastGrant.Select(kv => new TagCompound { ["world"] = kv.Key.ToString(), ["txn"] = kv.Value }).ToList();
		}

		public override void LoadData(TagCompound tag)
		{
			if (Guid.TryParse(tag.GetString("characterId"), out var id))
				CharacterId = id;
			NextTxn = Math.Max(1, tag.GetLong("nextTxn"));
			Pending = tag.GetList<TagCompound>("pending").Select(PendingOp.Load).ToList();
			LastGrant = tag.GetList<TagCompound>("lastGrant").ToDictionary(t => Guid.Parse(t.GetString("world")), t => t.GetLong("txn"));
		}

		public override void SyncPlayer(int toWho, int fromWho, bool newPlayer)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
				PacketHandler.SendCharacterId(CharacterId);
		}

		// Joining a world: catch up with the server. Unconfirmed deliveries come back; unconfirmed deaths go out again.
		public override void OnEnterWorld()
		{
			if (Main.netMode != NetmodeID.MultiplayerClient)
				return;
			PacketHandler.SendCharacterId(CharacterId);
			PacketHandler.SendHello(LastGrant.GetValueOrDefault(WorldId));
			foreach (var op in Pending.Where(p => p.World == WorldId).OrderBy(p => p.Txn))
				PacketHandler.SendOp(op);
		}

		// Client: hand a death's items to the server. They left the inventory already and stay in Pending until the server confirms.
		public void HandOver(PendingOp op)
		{
			op.World = WorldId;
			op.Txn = NextTxn++;
			Pending.Add(op);
			PacketHandler.SendOp(op);
		}

		// Items arriving from a chest. Their equipment slot first if one is given (Try to Equip), else the mouse if asked,
		// then the inventory; what doesn't fit drops at the player like any vanilla overflow, never vanishes.
		public void Receive(IEnumerable<Item> items, bool toMouse, string equipTo = null)
		{
			foreach (var item in items) {
				if (item.IsAir)
					continue;
				if (equipTo != null && Equipment.TryPlace(Player, equipTo, item))
					continue;
				if (toMouse) {
					if (Main.mouseItem.IsAir) {
						Main.mouseItem = item;
						continue;
					}
					if (Main.mouseItem.type == item.type && ItemLoader.TryStackItems(Main.mouseItem, item, out _) && item.stack <= 0)
						continue;
				}
				Item rest = Player.GetItem(Main.myPlayer, item, GetItemSettings.LootAllSettings);
				if (rest.IsAir)
					continue;
				int i = Item.NewItem(new EntitySource_OverfullInventory(Player), Player.getRect(), rest);
				Main.item[i].newAndShiny = false;
				if (Main.netMode == NetmodeID.MultiplayerClient)
					NetMessage.SendData(MessageID.SyncItem, number: i, number2: 1f);
			}
		}

		// Like vanilla Chest.UsingChest: the player who has this chest open, or -1.
		public static int UsingChest(Guid chestId)
		{
			for (int i = 0; i < Main.maxPlayers; i++)
				if (Main.player[i].active && Main.player[i].GetModPlayer<DeathChestPlayer>().OpenChestId == chestId)
					return i;
			return -1;
		}

		// The chest this player has open, or null once it is gone.
		public DeathChestData OpenChest => ChestWorld.Find(OpenChestId);

		// Right-clicked a chest: release the one that's open now, then open this one (multiplayer: ask the server).
		public void RequestOpen(DeathChestData chest)
		{
			if (OpenChestId == chest.Id)
				return;
			Close();
			if (Main.netMode == NetmodeID.MultiplayerClient)
				PacketHandler.SendOpenRequest(chest.Id);
			else if (chest.CanAccess(Player))
				Open(chest.Id);
		}

		public void Open(Guid chestId)
		{
			OpenChestId = chestId;
			Player.SetTalkNPC(-1);
			Player.chest = -1;
			Main.npcChatText = "";
			Main.playerInventory = true;
			SoundEngine.PlaySound(SoundID.MenuOpen);
			ModContent.GetInstance<UISystem>().ShowUI();
		}

		public void Close()
		{
			if (OpenChestId == Guid.Empty)
				return;
			OpenChestId = Guid.Empty;
			if (Main.netMode == NetmodeID.MultiplayerClient)
				PacketHandler.SendClose();
			ModContent.GetInstance<UISystem>().HideUI();
			SoundEngine.PlaySound(SoundID.MenuClose);
		}

		// Closes the chest when the player dies, walks away, closes the inventory, opens something else, loses access,
		// or the chest is emptied.
		public override void PostUpdate()
		{
			if (Player.whoAmI != Main.myPlayer || OpenChestId == Guid.Empty)
				return;
			var chest = OpenChest;
			if (Player.dead || chest == null || chest.IsEmpty || !chest.CanAccess(Player) || !Main.playerInventory || Player.chest != -1 || Player.talkNPC != -1 ||
				Player.Distance(chest.Center) > Range)
				Close();
		}

		// Server: a player who leaves no longer holds a chest, and whoever joins in their slot doesn't inherit it.
		public override void PlayerDisconnect()
		{
			OpenChestId = Guid.Empty;
			if (Main.netMode == NetmodeID.Server)
				PacketHandler.PlayerLeft(Player.whoAmI);
		}

		public override void UpdateDead()
		{
			if (Player.whoAmI == Main.myPlayer)
				Close();
		}
	}

	// A death a client sent to the server and is waiting to have confirmed: its items and where the chest goes.
	public class PendingOp
	{
		public Guid World;
		public long Txn;
		public Vector2 Bottom;
		public List<Item> Items = new();
		public List<string> Origins = new();

		public TagCompound Save() => new() {
			["world"] = World.ToString(), ["txn"] = Txn, ["x"] = Bottom.X, ["y"] = Bottom.Y, ["items"] = Items,
			["origins"] = Origins.Select(o => o ?? "").ToList(),
		};

		public static PendingOp Load(TagCompound t) => new() {
			World = Guid.Parse(t.GetString("world")), Txn = t.GetLong("txn"),
			Bottom = new Vector2(t.GetFloat("x"), t.GetFloat("y")), Items = t.GetList<Item>("items").ToList(),
			Origins = t.GetList<string>("origins").Select(o => o == "" ? null : o).ToList(),
		};
	}
}
