using System;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Default;
using Terraria.UI;

namespace DeathChest
{
	// Where an equipped item came from at death, so "Try to Equip" can put it back. Origins are short strings:
	//   armor/L/k   Player.armor (armor 0-2, accessories 3-9, vanity 10-19) of loadout L
	//   dye/L/k     Player.dye of loadout L
	//   misc/k      Player.miscEquips (pet, light pet, minecart, mount, hook)
	//   miscdye/k   Player.miscDyes
	//   modacc/L/k, modvan/L/k, moddye/L/k   modded accessory slot k (functional, vanity, dye) of loadout L
	// Inventory items have no origin; they stay for Loot All.
	public static class Equipment
	{
		// tModLoader keeps the modded accessory slots of the loadouts not in use in a private field,
		// ModAccessorySlotPlayer.exLoadouts: one entry per loadout with public arrays ExAccessorySlot (functional slots,
		// then vanity slots) and ExDyesAccessory. Looked up once here. If an update renames it, the lookup finds nothing,
		// one warning is logged, and those slots are simply not restored: their items stay in the chest.
		private static readonly FieldInfo ExLoadouts = typeof(ModAccessorySlotPlayer).GetField("exLoadouts", BindingFlags.NonPublic | BindingFlags.Instance);
		private static readonly Type ExLoadoutType = ExLoadouts?.FieldType.GetElementType();
		private static readonly PropertyInfo ExIndex = ExLoadoutType?.GetProperty("LoadoutIndex");
		private static readonly PropertyInfo ExAcc = ExLoadoutType?.GetProperty("ExAccessorySlot");
		private static readonly PropertyInfo ExDyes = ExLoadoutType?.GetProperty("ExDyesAccessory");
		private static bool _warned;

		// The modded slot arrays of a loadout that isn't in use, or false if tModLoader no longer has them where expected.
		private static bool OtherLoadoutModSlots(Player p, int loadout, out Item[] acc, out Item[] dyes)
		{
			acc = dyes = null;
			try {
				if (ExLoadouts?.GetValue(p.GetModPlayer<ModAccessorySlotPlayer>()) is Array all && ExIndex != null && ExAcc != null && ExDyes != null) {
					foreach (var entry in all) {
						if (entry != null && (int)ExIndex.GetValue(entry) == loadout) {
							acc = ExAcc.GetValue(entry) as Item[];
							dyes = ExDyes.GetValue(entry) as Item[];
							return acc != null && dyes != null;
						}
					}
				}
			}
			catch (Exception) {
			}
			if (!_warned) {
				_warned = true;
				ModContent.GetInstance<DeathChest>().Logger.Warn("Modded accessory slots of other loadouts aren't where this tModLoader version was expected to keep them; Try to Equip leaves those items in the chest.");
			}
			return false;
		}

		// Called while the dying player's items are still in place: finds the slot holding exactly this item.
		public static string FindOrigin(Player p, Item item)
		{
			int cur = p.CurrentLoadoutIndex;
			for (int k = 0; k < p.armor.Length; k++)
				if (ReferenceEquals(p.armor[k], item)) return $"armor/{cur}/{k}";
			for (int k = 0; k < p.dye.Length; k++)
				if (ReferenceEquals(p.dye[k], item)) return $"dye/{cur}/{k}";
			for (int k = 0; k < p.miscEquips.Length; k++)
				if (ReferenceEquals(p.miscEquips[k], item)) return $"misc/{k}";
			for (int k = 0; k < p.miscDyes.Length; k++)
				if (ReferenceEquals(p.miscDyes[k], item)) return $"miscdye/{k}";
			for (int l = 0; l < p.Loadouts.Length; l++) {
				if (l == cur)
					continue; // the loadout in use lives in armor/dye above
				for (int k = 0; k < p.Loadouts[l].Armor.Length; k++)
					if (ReferenceEquals(p.Loadouts[l].Armor[k], item)) return $"armor/{l}/{k}";
				for (int k = 0; k < p.Loadouts[l].Dye.Length; k++)
					if (ReferenceEquals(p.Loadouts[l].Dye[k], item)) return $"dye/{l}/{k}";
			}
			var loader = LoaderManager.Get<AccessorySlotLoader>();
			int count = p.GetModPlayer<ModAccessorySlotPlayer>().SlotCount;
			for (int k = 0; k < count; k++) {
				var slot = loader.Get(k, p);
				if (ReferenceEquals(slot.FunctionalItem, item)) return $"modacc/{cur}/{k}";
				if (ReferenceEquals(slot.VanityItem, item)) return $"modvan/{cur}/{k}";
				if (ReferenceEquals(slot.DyeItem, item)) return $"moddye/{cur}/{k}";
			}
			for (int l = 0; l < p.Loadouts.Length; l++) {
				if (l == cur || !OtherLoadoutModSlots(p, l, out var acc, out var dyes))
					continue;
				for (int k = 0; k < acc.Length; k++)
					if (ReferenceEquals(acc[k], item)) return k < count ? $"modacc/{l}/{k}" : $"modvan/{l}/{k - count}";
				for (int k = 0; k < dyes.Length; k++)
					if (ReferenceEquals(dyes[k], item)) return $"moddye/{l}/{k}";
			}
			return null;
		}

		// True if the origin slot is empty and would accept this item now, by the same rules as equipping by hand.
		public static bool CanPlace(Player p, string origin, Item item) => Place(p, origin, item, dryRun: true);

		// Puts the item into its origin slot if CanPlace; false if it has to go elsewhere.
		public static bool TryPlace(Player p, string origin, Item item) => Place(p, origin, item, dryRun: false);

		private static bool Place(Player p, string origin, Item item, bool dryRun)
		{
			if (string.IsNullOrEmpty(origin) || item.IsAir)
				return false;
			var parts = origin.Split('/');
			try {
				switch (parts[0]) {
					case "armor": {
						int l = int.Parse(parts[1]), k = int.Parse(parts[2]);
						Item[] arr = l == p.CurrentLoadoutIndex ? p.armor : p.Loadouts[l].Armor;
						if (!arr[k].IsAir || (l == p.CurrentLoadoutIndex && !p.IsItemSlotUnlockedAndUsable(k)))
							return false;
						if ((k is >= 3 and <= 9 or >= 13) && ItemSlot.AccCheck(arr, item, k)) // accessory already worn, or a second pair of wings
							return false;
						if (!dryRun) arr[k] = item;
						return true;
					}
					case "dye": {
						int l = int.Parse(parts[1]), k = int.Parse(parts[2]);
						Item[] arr = l == p.CurrentLoadoutIndex ? p.dye : p.Loadouts[l].Dye;
						if (!arr[k].IsAir || (l == p.CurrentLoadoutIndex && !p.IsItemSlotUnlockedAndUsable(k)))
							return false;
						if (!dryRun) arr[k] = item;
						return true;
					}
					case "misc":
					case "miscdye": {
						Item[] arr = parts[0] == "misc" ? p.miscEquips : p.miscDyes;
						int k = int.Parse(parts[1]);
						if (!arr[k].IsAir)
							return false;
						if (!dryRun) arr[k] = item;
						return true;
					}
					case "modacc":
					case "modvan":
					case "moddye": {
						int l = int.Parse(parts[1]), k = int.Parse(parts[2]);
						int count = p.GetModPlayer<ModAccessorySlotPlayer>().SlotCount;
						if (k >= count)
							return false;
						var slot = LoaderManager.Get<AccessorySlotLoader>().Get(k, p);
						if (!slot.IsEnabled())
							return false;
						var context = parts[0] == "modacc" ? AccessorySlotType.FunctionalSlot : parts[0] == "modvan" ? AccessorySlotType.VanitySlot : AccessorySlotType.DyeSlot;
						// By hand, a dye goes into a modded dye slot through vanilla's dye handling (ItemSlot.PickItemMovementAction,
						// context -12), which only asks for a dye; the slot's CanAcceptItem is consulted for the other slots.
						if (context == AccessorySlotType.DyeSlot ? item.dye <= 0 : !slot.CanAcceptItem(item, context))
							return false;
						// The loadout in use, or a slot shared by all loadouts: through the slot itself.
						if (l == p.CurrentLoadoutIndex || !slot.HasEquipmentLoadoutSupport) {
							Item there = context == AccessorySlotType.FunctionalSlot ? slot.FunctionalItem : context == AccessorySlotType.VanitySlot ? slot.VanityItem : slot.DyeItem;
							if (!there.IsAir || (context != AccessorySlotType.DyeSlot && ItemSlot.AccCheck(p.armor, item, -1)))
								return false;
							if (!dryRun) {
								if (context == AccessorySlotType.FunctionalSlot) slot.FunctionalItem = item;
								else if (context == AccessorySlotType.VanitySlot) slot.VanityItem = item;
								else slot.DyeItem = item;
							}
							return true;
						}
						// Another loadout: its stored arrays.
						if (!OtherLoadoutModSlots(p, l, out var acc, out var dyes))
							return false;
						Item[] arr = context == AccessorySlotType.DyeSlot ? dyes : acc;
						int index = context == AccessorySlotType.VanitySlot ? k + count : k;
						if (index >= arr.Length || !arr[index].IsAir)
							return false;
						if (context != AccessorySlotType.DyeSlot && ItemSlot.AccCheck(p.Loadouts[l].Armor, item, -1))
							return false;
						if (!dryRun) arr[index] = item;
						return true;
					}
				}
			}
			catch (System.Exception) {
				// a malformed origin or a slot that no longer exists: the item goes to the inventory instead
			}
			return false;
		}
	}
}
