using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DeathChest.NPCs
{
	// The server (or single player) holds a chest's items and saves them with the NPC. Clients get the items only
	// when they open the chest; the NPC sync carries just the id, owner and time of death. All changes to the items
	// happen on the server (ChestOps), so two machines never disagree about who has an item.
	[AutoloadHead]
	public class DeathChestNPC : ModNPC
	{
		public List<Item> Items = new();
		public Guid ChestId;          // stable across saves, unlike the NPC slot
		public string Owner = "";     // the owner's character name
		public Guid OwnerId;          // the owner's DeathChestPlayer.CharacterId
		public DateTime DeathTime;    // UTC

		public bool IsEmpty => Items.TrueForAll(i => i.IsAir);
		public string DeathTimeText => DeathTime.ToLocalTime().ToString("g");

		public override void SetStaticDefaults()
		{
			Main.npcFrameCount[Type] = 2;
			NPCID.Sets.NoTownNPCHappiness[Type] = true;
			NPCID.Sets.ActsLikeTownNPC[Type] = true;
			NPCID.Sets.DontDoHardmodeScaling[Type] = true;
			NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, new NPCID.Sets.NPCBestiaryDrawModifiers { Hide = true });
		}

		public override void SetDefaults()
		{
			Items = new(); // ModNPC instances are shallow copies of the template; never share its list
			ChestId = Guid.Empty;
			Owner = "";
			OwnerId = Guid.Empty;
			DeathTime = default;
			NPC.width = 40;
			NPC.height = 56;
			NPC.lifeMax = 1;
			NPC.immortal = true;
			NPC.dontTakeDamage = true;
			NPC.friendly = true;
			NPC.noGravity = true;
			NPC.noTileCollide = true;
			NPC.dontCountMe = true;
			NPC.npcSlots = 0f;
			NPC.aiStyle = -1;
			NPC.townNPC = true; // shows its head on the map
			TownNPCStayingHomeless = true;
			NPC.behindTiles = true;
			NPC.netAlways = true;
		}

		// The name on cursor hover and map hover: "<owner>'s Death Chest (died <time>)". No given name, so this is all that shows.
		public override List<string> SetNPCNameList() => new();

		public override void ModifyTypeName(ref string typeName)
		{
			if (Owner != "")
				typeName = Language.GetTextValue("Mods.DeathChest.NPCs.DeathChestNPC.Hover", Owner, DeathTimeText);
		}

		public override void AI()
		{
			if (!Main.dedServ && CanAccess(Main.LocalPlayer))
				Lighting.AddLight(NPC.Center, Color.Pink.ToVector3());
		}

		// A private chest is invisible to everyone but its owner: not drawn, no name on hover, no right-click, no map head.
		public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) => CanAccess(Main.LocalPlayer);
		public override bool PreHoverInteract(bool mouseIntersects) => CanAccess(Main.LocalPlayer);
		public override ITownNPCProfile TownNPCProfile() => new HeadOnlyForOwner(new Profiles.DefaultNPCProfile(Texture, NPCHeadLoader.GetHeadSlot(HeadTexture)));

		private class HeadOnlyForOwner(ITownNPCProfile inner) : ITownNPCProfile
		{
			public int RollVariation() => inner.RollVariation();
			public string GetNameForVariant(NPC npc) => inner.GetNameForVariant(npc);
			public Asset<Texture2D> GetTextureNPCShouldUse(NPC npc) => inner.GetTextureNPCShouldUse(npc);
			public int GetHeadTextureIndex(NPC npc) =>
				npc.ModNPC is DeathChestNPC chest && !chest.CanAccess(Main.LocalPlayer) ? -1 : inner.GetHeadTextureIndex(npc);
		}

		public override bool? CanBeHitByProjectile(Projectile projectile) => false;
		public override bool? CanBeHitByItem(Player player, Item item) => false;
		public override bool CheckDead() => false;
		public override bool CheckActive() => false;
		public override bool NeedSaving() => true;
		public override bool CanChat() => CanAccess(Main.LocalPlayer);

		// Right-click: vanilla calls GetChat when it opens the chat window. An empty text keeps the window closed.
		public override string GetChat()
		{
			var player = Main.LocalPlayer.GetModPlayer<DeathChestPlayer>();
			if (player.OpenChestId == NPC.whoAmI)
				return "";
			player.Close(); // release the chest that's open now before asking for another
			if (Main.netMode == NetmodeID.MultiplayerClient)
				PacketHandler.SendOpenRequest(NPC.whoAmI);
			else if (CanAccess(Main.LocalPlayer))
				Main.LocalPlayer.GetModPlayer<DeathChestPlayer>().Open(NPC.whoAmI);
			return "";
		}

		public bool CanAccess(Player player) => !Config.Instance.PrivateDeathChests || IsOwner(player);

		public bool IsOwner(Player player) => player.GetModPlayer<DeathChestPlayer>().CharacterId == OwnerId;

		public static DeathChestNPC Find(Guid chestId)
		{
			foreach (var npc in Main.npc)
				if (npc.active && npc.ModNPC is DeathChestNPC chest && chest.ChestId == chestId)
					return chest;
			return null;
		}

		// Single player or server.
		public void Remove()
		{
			NPC.active = false;
			for (int i = 0; i < Main.maxPlayers; i++) // the slot may hold a new chest later; don't leave it locked
				if (Main.player[i].active && Main.player[i].GetModPlayer<DeathChestPlayer>().OpenChestId == NPC.whoAmI)
					Main.player[i].GetModPlayer<DeathChestPlayer>().OpenChestId = -1;
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendData(MessageID.SyncNPC, number: NPC.whoAmI);
		}

		// Local player died: spawn here (single player) or hand the items to the server.
		public static void SpawnFor(Player player, List<Item> items)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient)
				player.GetModPlayer<DeathChestPlayer>().HandOver(new PendingOp { Bottom = player.Bottom, Items = items });
			else
				Spawn(player.Bottom, items, player);
		}

		// Single player or server.
		public static void Spawn(Vector2 bottom, List<Item> items, Player owner)
		{
			int id = NPC.NewNPC(NPC.GetSource_NaturalSpawn(), (int)bottom.X, (int)bottom.Y, ModContent.NPCType<DeathChestNPC>());
			if (id >= Main.maxNPCs) {
				// No free NPC slot: drop the items like vanilla would rather than lose them.
				foreach (var item in items) {
					int i = Item.NewItem(NPC.GetSource_NaturalSpawn(), bottom, item);
					if (Main.netMode == NetmodeID.Server)
						NetMessage.SendData(MessageID.SyncItem, number: i);
				}
				return;
			}
			var chest = (DeathChestNPC)Main.npc[id].ModNPC;
			chest.ChestId = Guid.NewGuid();
			chest.Items = items;
			chest.Owner = owner.name;
			chest.OwnerId = owner.GetModPlayer<DeathChestPlayer>().CharacterId;
			chest.DeathTime = DateTime.UtcNow;
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendData(MessageID.SyncNPC, number: id);
		}

		public override void SaveData(TagCompound tag)
		{
			tag["chestId"] = ChestId.ToString();
			tag["items"] = Items.Where(i => !i.IsAir).ToList();
			tag["owner"] = Owner;
			tag["ownerId"] = OwnerId.ToString();
			tag["deathTime"] = DeathTime.Ticks;
		}

		public override void LoadData(TagCompound tag)
		{
			Guid.TryParse(tag.GetString("chestId"), out ChestId);
			Items = tag.GetList<Item>("items").ToList();
			Owner = tag.GetString("owner");
			Guid.TryParse(tag.GetString("ownerId"), out OwnerId);
			DeathTime = new DateTime(tag.GetLong("deathTime"), DateTimeKind.Utc);
		}

		public override void SendExtraAI(BinaryWriter writer)
		{
			writer.Write(ChestId.ToByteArray());
			writer.Write(Owner);
			writer.Write(OwnerId.ToByteArray());
			writer.Write(DeathTime.Ticks);
		}

		public override void ReceiveExtraAI(BinaryReader reader)
		{
			ChestId = new Guid(reader.ReadBytes(16));
			Owner = reader.ReadString();
			OwnerId = new Guid(reader.ReadBytes(16));
			DeathTime = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
		}
	}
}
