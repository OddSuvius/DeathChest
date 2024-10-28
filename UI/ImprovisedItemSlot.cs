using DeathChest;
using System;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;
using Terraria;
using Terraria.ModLoader;

public class ImprovisedItemSlot : UIElement
{
    private readonly Item[] _itemArray;
    private readonly int _itemIndex;
    private readonly int _itemSlotContext; // Idk just keep it here
    public int _NPC_ID;

    public event Action<int> OnItemChanged;


    public ImprovisedItemSlot(Item[] items, int index, int context = ItemSlot.Context.ChestItem)
    {
        _itemArray = items;
        _itemIndex = index;
        _itemSlotContext = context;
    }

    public Item StoredItem
    {
        get => _itemArray[_itemIndex];
        set
        {
            _itemArray[_itemIndex] = value;
            HandleItemChange();
        }
    }

    private void HandleItemChange()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            PacketHandler.SendUpdateChestItem(_NPC_ID, _itemIndex, StoredItem);

        OnItemChanged?.Invoke(_NPC_ID);
    }

    public int SlotId => _itemIndex;

    private bool _isRightMouseHeld;
    private int _rightClickTimer;
    private const int RIGHT_CLICK_DELAY = 7;

    public float Scale { get; set; } = 1f;
    public bool IgnoreClicks { get; set; }


    public void HandleItemSlotLogic()
    {
        if (IgnoreClicks || PlayerInput.IgnoreMouseInterface)
            return;

        Player player = Main.LocalPlayer;
        player.mouseInterface = true;

        // Handle standard item pick up
        if (Main.mouseLeft && Main.mouseLeftRelease)
        {
            if (Main.mouseItem.IsAir && !StoredItem.IsAir)
            {
                // Pick up the entire stack
                Main.mouseItem = StoredItem.Clone();
                StoredItem = new Item(); // This will trigger HandleItemChange
                SoundEngine.PlaySound(SoundID.Grab);
            }
            else if (!Main.mouseItem.IsAir && (StoredItem.IsAir || Main.mouseItem.type == StoredItem.type))
            {
                // Place or stack items
                bool changed = false;
                if (StoredItem.IsAir)
                {
                    StoredItem = Main.mouseItem.Clone();
                    Main.mouseItem = new Item();
                    changed = true;
                }
                else if (StoredItem.stack < StoredItem.maxStack)
                {
                    int spaceLeft = StoredItem.maxStack - StoredItem.stack;
                    int transfer = Math.Min(spaceLeft, Main.mouseItem.stack);
                    StoredItem.stack += transfer;
                    Main.mouseItem.stack -= transfer;
                    if (Main.mouseItem.stack <= 0)
                        Main.mouseItem = new Item();
                    changed = true;
                }

                if (changed)
                    SoundEngine.PlaySound(SoundID.Grab);
            }
        }
        // Handle right-click quick transfer - NOT WORKING RIGHT NOW
        else if (Main.mouseRight && !StoredItem.IsAir && Main.mouseItem.IsAir)
        {
            if (Main.mouseRightRelease)
            {
                _isRightMouseHeld = true;
                HandleRightClick();
            }
            else if (_isRightMouseHeld)
            {
                _rightClickTimer++;
                if (_rightClickTimer >= RIGHT_CLICK_DELAY)
                {
                    _rightClickTimer = 0;
                    HandleRightClick();
                }
            }
        }
        else
        {
            _isRightMouseHeld = false;
            _rightClickTimer = 0;
        }
    }


    private void HandleRightClick()
    {
        if (StoredItem.stack > 1)
        {
            Main.mouseItem = StoredItem.Clone();
            Main.mouseItem.stack = 1;
            StoredItem.stack--;

            if (Main.mouseRightRelease) // Only play sound on initial click
                SoundEngine.PlaySound(SoundID.Grab);
        }
        else
        {
            Main.mouseItem = StoredItem.Clone();
            StoredItem = new Item();

            if (Main.mouseRightRelease) // Only play sound on initial click
                SoundEngine.PlaySound(SoundID.Grab);
        }
    }
}