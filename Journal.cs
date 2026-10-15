using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DeathChest
{
	// Server side of the multiplayer bookkeeping, saved with the world (together with the chests), per character:
	// - LastTxn: the last death applied, so a resent one is skipped.
	// - Grants: item deliveries sent to the character and not yet confirmed. Resent on their next join.
	public class Journal : ModSystem
	{
		public class Grant
		{
			public long Txn;
			public bool ToMouse;
			public string EquipTo; // equipment slot to put it back into (Try to Equip), or null
			public List<Item> Items;
		}

		public class Record
		{
			public long LastTxn;
			public long NextGrant = 1;
			public List<Grant> Grants = new();
		}

		private static readonly Dictionary<Guid, Record> _records = new();

		public static Record For(Guid characterId)
		{
			if (!_records.TryGetValue(characterId, out var r))
				_records[characterId] = r = new Record();
			return r;
		}

		public override void OnWorldUnload() => _records.Clear();

		public override void SaveWorldData(TagCompound tag)
		{
			tag["records"] = _records.Select(kv => new TagCompound {
				["id"] = kv.Key.ToString(),
				["lastTxn"] = kv.Value.LastTxn,
				["nextGrant"] = kv.Value.NextGrant,
				["grants"] = kv.Value.Grants.Select(g => new TagCompound { ["txn"] = g.Txn, ["toMouse"] = g.ToMouse, ["equipTo"] = g.EquipTo ?? "", ["items"] = g.Items }).ToList(),
			}).ToList();
		}

		public override void LoadWorldData(TagCompound tag)
		{
			_records.Clear();
			foreach (var t in tag.GetList<TagCompound>("records")) {
				_records[Guid.Parse(t.GetString("id"))] = new Record {
					LastTxn = t.GetLong("lastTxn"),
					NextGrant = Math.Max(1, t.GetLong("nextGrant")),
					Grants = t.GetList<TagCompound>("grants").Select(g => new Grant { Txn = g.GetLong("txn"), ToMouse = g.GetBool("toMouse"), EquipTo = g.GetString("equipTo") is { Length: > 0 } e ? e : null, Items = g.GetList<Item>("items").ToList() }).ToList(),
				};
			}
		}
	}
}
