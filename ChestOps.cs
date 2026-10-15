using System;
using System.Collections.Generic;
using DeathChest.NPCs;
using Terraria;
using Terraria.ModLoader;

namespace DeathChest
{
	// The only way items leave a chest. It runs on the server (or in single player); a client only asks.
	// Whatever leaves the chest is returned to the caller to deliver, so nothing is created or lost.
	public static class ChestOps
	{
		// Takes up to count from slot. Returns what was taken (air if the slot was empty).
		public static Item Take(List<Item> items, int slot, int count)
		{
			if ((uint)slot >= (uint)items.Count || items[slot].IsAir || count <= 0)
				return new Item();
			Item there = items[slot];
			int n = Math.Min(count, there.stack);
			if (n == there.stack) {
				items[slot] = new Item();
				return there;
			}
			Item taken = there.Clone();
			taken.stack = n;
			there.stack -= n;
			return taken;
		}
	}
}
