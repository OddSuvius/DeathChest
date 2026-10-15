using Terraria;
using Terraria.ModLoader;

namespace DeathChest.UI
{
	// How much of an item fits into the player's inventory, counting what earlier calls already reserved.
	// Used so shift-click and Loot All only take out what fits, and the rest stays in the chest.
	// Main inventory takes anything; coin slots take coins, ammo slots take ammo.
	public class InventorySpace
	{
		private readonly Item[] _slots = new Item[58];

		public InventorySpace(Player player)
		{
			for (int i = 0; i < _slots.Length; i++)
				_slots[i] = player.inventory[i].Clone();
		}

		public int Reserve(Item item)
		{
			if (item.IsAir)
				return 0;
			Item left = item.Clone();
			for (int i = 0; i < _slots.Length && left.stack > 0; i++)
				if (Allowed(i, item) && !_slots[i].IsAir && _slots[i].type == item.type && _slots[i].stack < _slots[i].maxStack)
					ItemLoader.TryStackItems(_slots[i], left, out _);
			for (int i = 0; i < _slots.Length && left.stack > 0; i++) {
				if (Allowed(i, item) && _slots[i].IsAir) {
					_slots[i] = left.Clone();
					_slots[i].stack = System.Math.Min(left.stack, left.maxStack);
					left.stack -= _slots[i].stack;
				}
			}
			return item.stack - left.stack;
		}

		private static bool Allowed(int slot, Item item) =>
			slot < 50 || (slot < 54 ? item.IsACoin : item.FitsAmmoSlot());
	}
}
