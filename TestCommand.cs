#if DEBUG
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace DeathChest
{
	// Testing helpers, only in Debug builds (tml-run, tml-check). Workshop builds are Release, so this never ships.
	public class TestCommand : ModCommand
	{
		public override string Command => "dctest";
		public override CommandType Type => CommandType.Chat;
		public override string Usage => "/dctest gear | die | chests [count]";
		public override string Description => "Death Chest testing (debug builds only)";

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			var p = Main.LocalPlayer;
			switch (args.Length > 0 ? args[0].ToLowerInvariant() : "") {
				case "gear":
					int put = GiveGear(p);
					caller.Reply($"Filled {put} empty slots with test gear (armor, accessories, vanity, dyes, pet, light pet, hook, mount, the next loadout, inventory items). Nothing you had was replaced.", Color.LightGreen);
					break;
				case "die":
					p.KillMe(PlayerDeathReason.ByCustomReason(NetworkText.FromLiteral($"{p.name} tested a Death Chest.")), 9999, 0);
					break;
				case "chests": {
					int count = args.Length > 1 && int.TryParse(args[1], out int n) ? Math.Clamp(n, 1, 200) : 5;
					var rng = new Random();
					int[] pool = { ItemID.DirtBlock, ItemID.Torch, ItemID.Wood, ItemID.HealingPotion, ItemID.Gel, ItemID.IronBar, ItemID.GoldCoin, ItemID.WoodenArrow,
						ItemID.CopperShortsword, ItemID.IronPickaxe, ItemID.Shuriken, ItemID.LesserManaPotion, ItemID.Mushroom, ItemID.Daybloom };
					for (int c = 0; c < count; c++) {
						var items = new List<Item>();
						int stacks = rng.Next(5, 90); // some chests scroll (more than 40)
						for (int i = 0; i < stacks; i++) {
							var item = new Item(pool[rng.Next(pool.Length)]);
							item.stack = Math.Min(item.maxStack, rng.Next(1, 120));
							if (item.damage > 0)
								item.Prefix(-1); // a random prefix
							items.Add(item);
						}
						var at = p.Bottom + new Vector2((c - (count - 1) / 2f) * 56f, 0f); // a row centred on you
						ChestWorld.SpawnFor(p, items, new List<string>(), at);
					}
					caller.Reply($"Spawned {count} test chests in a row around you, owned by you. Hover or right-click them; with smart cursor (Ctrl by default) or a gamepad, the nearest one gets a gold outline and right-click / interact opens it.", Color.LightGreen);
					break;
				}
				default:
					caller.Reply("Usage: " + Usage + "\n  gear: fill your empty slots with a full test setup\n  die: die right here (makes a Death Chest)\n  chests [count]: spawn test chests around you\nPrivate Death Chests is in the mod's config (host only).", Color.Yellow);
					break;
			}
		}

		// Only fills empty slots, so nothing the player has is lost.
		private static int GiveGear(Player p)
		{
			int put = 0;
			void Set(Item[] arr, int k, int type, int prefix = 0)
			{
				if (k < arr.Length && arr[k].IsAir) {
					arr[k] = new Item(type);
					if (prefix != 0)
						arr[k].Prefix(prefix);
					put++;
				}
			}
			Set(p.armor, 0, ItemID.MoltenHelmet); Set(p.armor, 1, ItemID.MoltenBreastplate); Set(p.armor, 2, ItemID.MoltenGreaves);
			Set(p.armor, 3, ItemID.HermesBoots); Set(p.armor, 4, ItemID.CloudinaBottle, PrefixID.Warding); Set(p.armor, 5, ItemID.BandofRegeneration);
			Set(p.armor, 6, ItemID.AngelWings); Set(p.armor, 7, ItemID.Shackle);
			Set(p.armor, 10, ItemID.TopHat); Set(p.armor, 11, ItemID.TuxedoShirt); Set(p.armor, 12, ItemID.TuxedoPants); Set(p.armor, 13, ItemID.ShinyRedBalloon);
			Set(p.dye, 0, ItemID.RedDye); Set(p.dye, 1, ItemID.BlueDye); Set(p.dye, 3, ItemID.GreenDye); Set(p.dye, 6, ItemID.YellowDye);
			Set(p.miscEquips, 0, ItemID.Fish); Set(p.miscEquips, 1, ItemID.ShadowOrb); Set(p.miscEquips, 3, ItemID.SlimySaddle); Set(p.miscEquips, 4, ItemID.GrapplingHook);
			Set(p.miscDyes, 0, ItemID.PurpleDye); Set(p.miscDyes, 4, ItemID.OrangeDye);
			int other = (p.CurrentLoadoutIndex + 1) % p.Loadouts.Length;
			Set(p.Loadouts[other].Armor, 0, ItemID.MiningHelmet); Set(p.Loadouts[other].Armor, 3, ItemID.Aglet); Set(p.Loadouts[other].Dye, 0, ItemID.PinkDye);
			int[] stuff = { ItemID.DirtBlock, ItemID.Torch, ItemID.HealingPotion, ItemID.Wood, ItemID.Gel, ItemID.IronBar };
			for (int i = 10, s = 0; i < 30; i++, s++) {
				if (p.inventory[i].IsAir) {
					p.inventory[i] = new Item(stuff[s % stuff.Length]);
					p.inventory[i].stack = Math.Min(p.inventory[i].maxStack, 25 + s * 7);
					put++;
				}
			}
			return put;
		}
	}
}
#endif
