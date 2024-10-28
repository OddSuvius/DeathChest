using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;
using DeathChest.UI.Components;
using DeathChest.NPCs;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;
using DeathChest.Data;
using System;
using Terraria.Audio;
using static DeathChest.PacketHandler;
using Terraria.ModLoader.IO;

namespace DeathChest.UI
{
    public class DeathChestUI : UIState
    {
        private const float INVENTORY_SCALE = 1f;
        private const int SLOTS_X = 10;
        private const int SLOTS_Y = 6;
        private const float BASE_X = 73f;
        private const float BASE_Y_OFFSET = 40f;

        private readonly List<ImprovisedItemSlot> _slots = new();
        private int _npcId = -1;

        private UITitle _title;
        private UISlotGrid _slotGrid;
        private UILootAllButton _lootAllButton;

        public override void OnInitialize()
        {
            Main.inventoryScale = INVENTORY_SCALE;

            InitializeTitle();
            InitializeSlotGrid();
            InitializeLootAllButton();
        }

        private void InitializeTitle()
        {
            _title = new UITitle();
            _title.Left.Set(BASE_X, 0f);
            _title.Top.Set(Main.instance.invBottom + BASE_Y_OFFSET - 25f, 0f);
            _title.Width.Set(0, 0f);
            _title.Height.Set(25f, 0f);
            Append(_title);
        }

        private void InitializeSlotGrid()
        {
            _slotGrid = new UISlotGrid(SLOTS_X, SLOTS_Y, _slots);
            _slotGrid.Left.Set(BASE_X, 0f);
            _slotGrid.Top.Set(Main.instance.invBottom + BASE_Y_OFFSET, 0f);
            _slotGrid.Width.Set(520f * INVENTORY_SCALE, 0f);
            _slotGrid.Height.Set(208f * INVENTORY_SCALE, 0f);
            Append(_slotGrid);
        }

        private void InitializeLootAllButton()
        {
            _lootAllButton = new UILootAllButton();
            _lootAllButton.Left.Set(BASE_X + 520f * INVENTORY_SCALE + 10f, 0f);
            _lootAllButton.Top.Set(Main.instance.invBottom + BASE_Y_OFFSET + (208f * INVENTORY_SCALE / 2f), 0f);
            _lootAllButton.Width.Set(100f, 0f);
            _lootAllButton.Height.Set(24f, 0f);
            _lootAllButton.OnClicked += LootAll;
            Append(_lootAllButton);
        }

        public void AddRemoveItems(bool add, Item[] items, int npcId = 0)
        {
            try
            {
                _npcId = npcId;
                _slots.Clear();

                if (add && items != null)
                {
                    if (!Main.npc[npcId].active || Main.npc[npcId].type != ModContent.NPCType<DeathChestNPC>())
                    {
                        Logging.PublicLogger.Error($"[Death Chest] Invalid NPC state during UI update");
                        RemoveDeathChest();
                        return;
                    }

                    for (int i = 0; i < items.Length && i < SLOTS_X * SLOTS_Y; i++)
                    {
                        var slot = new ImprovisedItemSlot(items, i, ItemSlot.Context.ChestItem)
                        {
                            Scale = INVENTORY_SCALE,
                            _NPC_ID = npcId
                        };
                        // Subscribe to item changes
                        slot.OnItemChanged += CheckForEmptyChest;
                        _slots.Add(slot);
                    }
                }

                UpdateVisibility();
            }
            catch (Exception ex)
            {
                Logging.PublicLogger.Error($"[Death Chest] Error in AddRemoveItems: {ex}");
                RemoveDeathChest();
            }
        }

        private void CheckForEmptyChest(int npcId)
        {
            if (!_slots.Any(slot => !slot.StoredItem.IsAir))
            {
                RemoveDeathChest();
            }
        }

        private void UpdateVisibility()
        {
            bool hasItems = _slots.Any(slot => !slot.StoredItem.IsAir);
            _title.Visible = hasItems;
            _slotGrid.Visible = hasItems;
            _lootAllButton.Visible = hasItems;

            if (!hasItems)
            {
                RemoveDeathChest();
            }
        }


        public void LootAll()
        {
            if (_npcId == -1)
                return;

            var player = Main.LocalPlayer;
            bool anyItemPickedUp = false;
            Dictionary<int, Item> updates = new();

            foreach (var slot in _slots.ToList())
            {
                if (!slot.StoredItem.IsAir)
                {
                    Item item = slot.StoredItem.Clone();
                    item.position = player.Center;

                    var result = player.GetItem(Main.myPlayer, item, GetItemSettings.LootAllSettings);

                    if (result != item) // Item was picked up at least partially
                    {
                        anyItemPickedUp = true;
                        slot.StoredItem = result;  // Now this will work
                        updates[slot.SlotId] = result;
                    }
                }
            }

            if (anyItemPickedUp)
            {
                // Play pickup sound
                SoundEngine.PlaySound(SoundID.Grab);

                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    // Send updated item data to server
                    foreach (var kvp in updates)
                    {
                        PacketHandler.SendUpdateChestItem(_npcId, kvp.Key, kvp.Value);
                    }
                }

                // Check if chest is now empty
                if (!_slots.Any(slot => !slot.StoredItem.IsAir))
                {
                    RemoveDeathChest();
                }
            }
        }



        public void RemoveDeathChest()
        {
            if (_npcId == -1)
                return;

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                PacketHandler.SendRemoveChest(_npcId);
            }

            if (Main.npc[_npcId].active &&
                Main.npc[_npcId].type == ModContent.NPCType<DeathChestNPC>())
            {
                DeathChestManager.RemoveChest(_npcId);
                Main.npc[_npcId].active = false;
            }

            var player = Main.LocalPlayer.GetModPlayer<DeathChestPlayer>();
            player.CloseChest();

            var ui = ModContent.GetInstance<UISystem>();
            ui.HideUI();

            Main.playerInventory = false;
        }

        public bool IsDeathChestEmpty()
        {
            if (_npcId == -1 || !Main.npc[_npcId].active)
                return true;

            var data = DeathChestManager.GetChestItems(_npcId);
            return data == null || data.IsEmpty;
        }
    }
}