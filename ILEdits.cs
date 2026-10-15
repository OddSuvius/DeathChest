using System.Collections.Generic;
using MonoMod.Cil;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace DeathChest
{
	// Every death drops like mediumcore, and everything the death drops goes into a Death Chest instead of onto the ground.
	// What vanilla mediumcore doesn't drop stays vanilla: the starter kit (copper tools) is kept and handed out as usual,
	// and the trash slot is cleared.
	public static class ILEdits
	{
		// Set only while Player.KillMe runs for the local player.
		private static List<Item> _collected;
		private static List<string> _origins; // where each collected item was equipped (Equipment), or null
		private static Player _dying;

		public static void ApplyILEdits()
		{
			IL_Player.KillMe += ClassicDropsLikeMediumcore;
			On_Player.KillMe += CollectDeathDrops;
			On_Player.TryDroppingSingleItem += StoreInsteadOfDropping;
			On_Item.NewItem_Inner += CatchOtherDeathDrops;
		}

		// Player.KillMe, inside `if (Main.myPlayer == whoAmI)`:
		//   trashItem.SetDefaults();                    .ilmap C# 35185, IL_012c ldfld trashItem .. IL_0133 callvirt Item::SetDefaults  <- insert after
		//   if (difficulty == 0 || difficulty == 3) { only large gems drop }
		//   else if (difficulty == 1) DropItems(); else if (difficulty == 2) { DropItems(); KillMeForGood(); }
		// Classic and Journey run DropItems here too; the vanilla classic branch then finds nothing left to drop,
		// and the later DropCoins finds no coins.
		private static void ClassicDropsLikeMediumcore(ILContext il)
		{
			var c = new ILCursor(il);
			if (!c.TryGotoNext(i => i.MatchLdfld<Player>("trashItem")) ||
				!c.TryGotoNext(MoveType.After, i => i.MatchCallOrCallvirt<Item>("SetDefaults")))
				throw new ILPatchFailureException(ModContent.GetInstance<DeathChest>(), il, null);

			c.EmitLdarg0();
			c.EmitDelegate(static (Player player) => {
				if (player.difficulty is PlayerDifficultyID.SoftCore or PlayerDifficultyID.Creative)
					player.DropItems();
			});
		}

		// Collect for the whole death, so items other mods drop in their own death code (ModPlayer.Kill, their own
		// DropItems detours, extra inventories) land in the chest as well.
		private static void CollectDeathDrops(On_Player.orig_KillMe orig, Player self, PlayerDeathReason damageSource, double dmg, int hitDirection, bool pvp)
		{
			if (self.whoAmI != Main.myPlayer || _collected != null) {
				orig(self, damageSource, dmg, hitDirection, pvp);
				return;
			}
			var items = _collected = new List<Item>();
			var origins = _origins = new List<string>();
			_dying = self;
			bool wasDead = self.dead;
			// The cursor item reaches DropItems only through inventory[58], which vanilla refreshes from Main.mouseItem once
			// per frame (Player.dropItemCheck). Dying before that refresh would delete the cursor item (DropItems_End clears
			// the cursor). Refresh it now, as vanilla would have.
			if (!wasDead)
				self.inventory[58] = Main.mouseItem.Clone();
			try {
				orig(self, damageSource, dmg, hitDirection, pvp);
			}
			finally {
				_collected = null;
				_origins = null;
				_dying = null;
			}
			if (items.Count > 0)
				ChestWorld.SpawnFor(self, items, origins);
		}

		// Vanilla's drop path (inventory, armor, dyes, equipment, loadouts, ModAccessorySlot slots): never spawn the item.
		private static void StoreInsteadOfDropping(On_Player.orig_TryDroppingSingleItem orig, Player self, IEntitySource source, Item theItem)
		{
			if (_collected == null || self != _dying) {
				orig(self, source, theItem);
				return;
			}
			if (theItem.stack > 0) {
				_collected.Add(theItem.Clone());
				_origins.Add(Equipment.FindOrigin(self, theItem));
			}
			theItem.TurnToAir(fullReset: true);
		}

		// Any other item spawned from the dying player's death. On a client, a new item is local until its caller
		// sends it, so emptying it here means the server never creates it.
		private static int CatchOtherDeathDrops(On_Item.orig_NewItem_Inner orig, IEntitySource source, int X, int Y, int Width, int Height,
			Item itemToClone, int Type, int Stack, bool noBroadcast, int pfix, bool noGrabDelay, bool reverseLookup)
		{
			int i = orig(source, X, Y, Width, Height, itemToClone, Type, Stack, noBroadcast, pfix, noGrabDelay, reverseLookup);
			if (_collected != null && source is EntitySource_Death death && death.Entity == _dying && i >= 0 && i < Main.item.Length && !Main.item[i].IsAir) {
				_collected.Add(Main.item[i].Clone());
				_origins.Add(null);
				Main.item[i].TurnToAir(fullReset: true);
				Main.item[i].active = false;
			}
			return i;
		}
	}
}
