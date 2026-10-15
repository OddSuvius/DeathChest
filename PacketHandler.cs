using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeathChest.NPCs;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DeathChest
{
	// Multiplayer. The server owns every chest's items; clients only ask, so the two can't disagree about an item.
	// Items crossing the network are always written down on the side that has them until the other side confirms:
	// - client -> server (a death): the client keeps the items in DeathChestPlayer.Pending, saved with the
	//   character, and resends them on its next join; the server skips any it already applied (Journal.LastTxn).
	// - server -> client (anything taken out): the server keeps them in Journal grants, saved with the world, and
	//   resends them on the character's next join; the client skips any it already received (LastGrant).
	// A dropped connection therefore leaves every item in exactly one place, and it reaches its owner on rejoin.
	public static class PacketHandler
	{
		private enum MessageType : byte
		{
			CharacterId,  // client -> server: my DeathChestPlayer.CharacterId
			Hello,        // client -> server, on entering the world: the last delivery I received here
			Death,        // client -> server, split: I died, here are my items (txn)
			Take,         // client -> server: give me up to N of that chest slot
			GrantAck,     // client -> server: I received delivery txn
			RequestOpen,  // client -> server
			Close,        // client -> server: I closed my chest
			OpenItems,    // server -> client, split: you may open this chest, here are its items
			ChestUpdate,  // server -> client, split: these slots of your open chest changed
			Grant,        // server -> client: items for you (txn)
			Ack,          // server -> client: death txn applied
			OpDone,       // server -> client: your Take has been handled
		}

		// A ModPacket holds at most 65535 bytes, so item lists are split into packets of at most this many bytes of items.
		// Every part says where its entries start and whether it is the last one; a receiver that sees a gap throws the
		// partial list away rather than shift slot numbers.
		private const int PartBudget = 60000;

		// Server-side range check is looser than the client's, so lag never refuses a valid open.
		private const float ServerRange = DeathChestPlayer.Range * 2;

		// Parts received so far: server, per sending player; client, the chest being opened.
		private static readonly Dictionary<int, (long txn, Vector2 bottom, List<Item> items)> _deathParts = new();
		private static readonly List<Item> _openParts = new();

		// Client: one open request at a time. The server ignores refused requests (like vanilla chests), so a request
		// with no answer expires after two seconds.
		private static uint _openRequestedAt;
		private static bool _openPending;

		private static Mod Mod => ModContent.GetInstance<DeathChest>();

		private static ModPacket Packet(MessageType type)
		{
			var packet = Mod.GetPacket();
			packet.Write((byte)type);
			return packet;
		}

		// Items travel in tModLoader's save format (ModItem.SaveData, global item data, prefix, stack, favorite):
		// exactly what a world or player file keeps. A mod's NetSend is meant for display sync and often leaves data out.
		private static void WriteItem(BinaryWriter w, Item item) => TagIO.Write(ItemIO.Save(item), w);
		private static Item ReadItem(BinaryReader reader) => ItemIO.Load(TagIO.Read(reader));

		private static byte[] ItemBytes(Item item, int slot = -1)
		{
			var ms = new MemoryStream();
			var w = new BinaryWriter(ms);
			if (slot >= 0)
				w.Write((ushort)slot);
			WriteItem(w, item);
			return ms.ToArray();
		}

		// Sends entries in as many packets as needed: header, start index, last-part flag, entry count, entries.
		private static void SendSplit(MessageType type, Action<ModPacket> header, List<byte[]> entries, int toClient = -1)
		{
			int i = 0;
			do {
				int n = 0, size = 0;
				while (i + n < entries.Count && (n == 0 || size + entries[i + n].Length <= PartBudget))
					size += entries[i + n++].Length;
				var p = Packet(type);
				header(p);
				p.Write(i);
				p.Write(i + n >= entries.Count);
				p.Write((ushort)n);
				for (int k = 0; k < n; k++)
					p.Write(entries[i + k]);
				p.Send(toClient);
				i += n;
			} while (i < entries.Count);
		}

		private static List<Item> ReadItems(BinaryReader reader, out int start, out bool last)
		{
			start = reader.ReadInt32();
			last = reader.ReadBoolean();
			int n = reader.ReadUInt16();
			var items = new List<Item>(n);
			for (int i = 0; i < n; i++)
				items.Add(ReadItem(reader));
			return items;
		}

		// Adds a part to what was received so far. False if a part is missing.
		private static bool Append(List<Item> sofar, List<Item> part, int start)
		{
			if (start == 0)
				sofar.Clear();
			if (start != sofar.Count)
				return false;
			sofar.AddRange(part);
			return true;
		}

		// ---- client -> server ----

		public static void SendCharacterId(Guid id)
		{
			var p = Packet(MessageType.CharacterId);
			p.Write(id.ToByteArray());
			p.Send();
		}

		public static void SendHello(long lastGrant)
		{
			var p = Packet(MessageType.Hello);
			p.Write(lastGrant);
			p.Send();
		}

		public static void SendOp(PendingOp op) =>
			SendSplit(MessageType.Death, p => { p.Write(op.Txn); p.WriteVector2(op.Bottom); }, op.Items.ConvertAll(i => ItemBytes(i)));

		public static void SendTake(Guid chest, int slot, int count, bool toMouse)
		{
			var p = Packet(MessageType.Take);
			p.Write(chest.ToByteArray());
			p.Write(slot);
			p.Write(count);
			p.Write(toMouse);
			p.Send();
		}

		public static void SendOpenRequest(int npcId)
		{
			if (_openPending && Main.GameUpdateCount - _openRequestedAt < 120)
				return;
			_openPending = true;
			_openRequestedAt = Main.GameUpdateCount;
			var p = Packet(MessageType.RequestOpen);
			p.Write((short)npcId);
			p.Send();
		}

		public static void SendClose() => Packet(MessageType.Close).Send();

		// ---- server ----

		// A player left in the middle of sending a death: drop the partial list. Their client still has the death in
		// Pending and sends all of it again on the next join.
		public static void PlayerLeft(int whoAmI) => _deathParts.Remove(whoAmI);

		private static Journal.Record RecordOf(int whoAmI) => Journal.For(Main.player[whoAmI].GetModPlayer<DeathChestPlayer>().CharacterId);

		// Items leaving a chest for a player: written to the journal first, then sent.
		private static void Give(int whoAmI, Item item, bool toMouse)
		{
			if (item.IsAir)
				return;
			var rec = RecordOf(whoAmI);
			var grant = new Journal.Grant { Txn = rec.NextGrant++, ToMouse = toMouse, Items = new List<Item> { item } };
			rec.Grants.Add(grant);
			SendGrant(whoAmI, grant);
		}

		private static void SendGrant(int toClient, Journal.Grant grant)
		{
			var p = Packet(MessageType.Grant);
			p.Write(grant.Txn);
			p.Write(grant.ToMouse);
			p.Write((ushort)grant.Items.Count);
			foreach (var item in grant.Items)
				WriteItem(p, item);
			p.Send(toClient);
		}

		private static void SendChestUpdate(int toClient, DeathChestNPC chest, IEnumerable<int> slots)
		{
			var list = slots.Where(s => s >= 0 && s < chest.Items.Count).Distinct().ToList();
			if (list.Count > 0)
				SendSplit(MessageType.ChestUpdate, p => p.Write(chest.ChestId.ToByteArray()), list.ConvertAll(s => ItemBytes(chest.Items[s], s)), toClient);
		}

		private static void Reply(MessageType type, int toClient, long txn = 0)
		{
			var p = Packet(type);
			p.Write(txn);
			p.Send(toClient);
		}

		private static void HandleTake(int whoAmI, Guid chestId, int slot, int count, bool toMouse)
		{
			var chest = DeathChestNPC.Find(chestId);
			// Only the player the server let open this chest takes from it.
			if (chest != null && Main.player[whoAmI].GetModPlayer<DeathChestPlayer>().OpenChestId == chest.NPC.whoAmI) {
				Item taken = ChestOps.Take(chest.Items, slot, count);
				Give(whoAmI, taken, toMouse);
				SendChestUpdate(whoAmI, chest, new[] { slot });
				if (chest.IsEmpty)
					chest.Remove();
			}
		}

		private static DeathChestNPC ChestAt(int npcId) =>
			npcId >= 0 && npcId < Main.maxNPCs && Main.npc[npcId].active ? Main.npc[npcId].ModNPC as DeathChestNPC : null;

		public static void HandlePacket(BinaryReader reader, int whoAmI)
		{
			bool server = Main.netMode == NetmodeID.Server;
			switch ((MessageType)reader.ReadByte()) {
				case MessageType.CharacterId: {
					var id = new Guid(reader.ReadBytes(16));
					if (server)
						Main.player[whoAmI].GetModPlayer<DeathChestPlayer>().CharacterId = id;
					break;
				}
				case MessageType.Hello: {
					long lastReceived = reader.ReadInt64();
					if (!server)
						break;
					var rec = RecordOf(whoAmI);
					rec.Grants.RemoveAll(g => g.Txn <= lastReceived);
					foreach (var g in rec.Grants)
						SendGrant(whoAmI, g);
					break;
				}
				case MessageType.Death: {
					long txn = reader.ReadInt64();
					var bottom = reader.ReadVector2();
					var part = ReadItems(reader, out int start, out bool last);
					if (!server)
						break;
					var sofar = _deathParts.TryGetValue(whoAmI, out var s) && s.txn == txn ? s.items : new List<Item>();
					if (!Append(sofar, part, start)) {
						// Can't happen over one ordered connection. Drop it: the client keeps the death and resends it whole.
						Mod.Logger.Error($"Death items from player {whoAmI} arrived out of order (part at {start}, have {sofar.Count}); waiting for a resend");
						_deathParts.Remove(whoAmI);
						break;
					}
					if (!last) {
						_deathParts[whoAmI] = (txn, bottom, sofar);
						break;
					}
					_deathParts.Remove(whoAmI);
					var rec = RecordOf(whoAmI);
					if (txn > rec.LastTxn) {
						rec.LastTxn = txn;
						DeathChestNPC.Spawn(bottom, sofar, Main.player[whoAmI]); // owner is whoever sent it, not what the packet claims
					}
					Reply(MessageType.Ack, whoAmI, txn);
					break;
				}
				case MessageType.Take: {
					var chestId = new Guid(reader.ReadBytes(16));
					int slot = reader.ReadInt32();
					int count = reader.ReadInt32();
					bool toMouse = reader.ReadBoolean();
					if (server) {
						HandleTake(whoAmI, chestId, slot, count, toMouse);
						Reply(MessageType.OpDone, whoAmI);
					}
					break;
				}
				case MessageType.GrantAck: {
					long txn = reader.ReadInt64();
					if (server)
						RecordOf(whoAmI).Grants.RemoveAll(g => g.Txn <= txn);
					break;
				}
				case MessageType.RequestOpen: {
					int npcId = reader.ReadInt16();
					var chest = ChestAt(npcId);
					var player = Main.player[whoAmI];
					if (!server || chest == null || player.Distance(chest.NPC.Center) > ServerRange || !chest.CanAccess(player))
						break;
					// Like vanilla chests: one player at a time, and a request for a chest in use is ignored.
					int user = DeathChestPlayer.UsingChest(npcId);
					if (user != -1 && user != whoAmI)
						break;
					player.GetModPlayer<DeathChestPlayer>().OpenChestId = npcId;
					SendSplit(MessageType.OpenItems, p => p.Write((short)npcId), chest.Items.ConvertAll(i => ItemBytes(i)), whoAmI);
					break;
				}
				case MessageType.Close:
					if (server)
						Main.player[whoAmI].GetModPlayer<DeathChestPlayer>().OpenChestId = -1;
					break;

				// ---- client ----
				case MessageType.OpenItems: {
					int npcId = reader.ReadInt16();
					var part = ReadItems(reader, out int start, out bool last);
					bool complete = Append(_openParts, part, start);
					if (complete && !last)
						break;
					_openPending = false;
					var chest = ChestAt(npcId);
					if (complete && chest != null) {
						chest.Items = new List<Item>(_openParts);
						Main.LocalPlayer.GetModPlayer<DeathChestPlayer>().Open(npcId);
					}
					else {
						// Incomplete list or no such chest here: don't show it, and release the server's lock.
						Mod.Logger.Warn($"Not opening chest {npcId}: complete={complete} present={chest != null}");
						SendClose();
					}
					_openParts.Clear();
					break;
				}
				case MessageType.ChestUpdate: {
					var chest = DeathChestNPC.Find(new Guid(reader.ReadBytes(16)));
					reader.ReadInt32();
					reader.ReadBoolean();
					int n = reader.ReadUInt16();
					for (int i = 0; i < n; i++) {
						int slot = reader.ReadUInt16();
						var item = ReadItem(reader);
						if (chest == null)
							continue;
						while (chest.Items.Count <= slot)
							chest.Items.Add(new Item());
						chest.Items[slot] = item;
					}
					break;
				}
				case MessageType.Grant: {
					long txn = reader.ReadInt64();
					bool toMouse = reader.ReadBoolean();
					int n = reader.ReadUInt16();
					var items = new List<Item>(n);
					for (int i = 0; i < n; i++)
						items.Add(ReadItem(reader));
					var me = Main.LocalPlayer.GetModPlayer<DeathChestPlayer>();
					var world = DeathChestPlayer.WorldId;
					if (txn > me.LastGrant.GetValueOrDefault(world)) {
						me.Receive(items, toMouse);
						me.LastGrant[world] = txn;
					}
					var p = Packet(MessageType.GrantAck);
					p.Write(txn);
					p.Send();
					break;
				}
				case MessageType.Ack: {
					long txn = reader.ReadInt64();
					var me = Main.LocalPlayer.GetModPlayer<DeathChestPlayer>();
					me.Pending.RemoveAll(op => op.World == DeathChestPlayer.WorldId && op.Txn <= txn);
					break;
				}
				case MessageType.OpDone:
					reader.ReadInt64();
					Main.LocalPlayer.GetModPlayer<DeathChestPlayer>().Outstanding--;
					break;
			}
		}
	}
}
