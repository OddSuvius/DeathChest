using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI.Chat;
using Terraria.UI;
using Terraria.Localization;
using Terraria.ID;
using Terraria.Audio;
using Terraria.GameInput;
using System.Collections.Generic;

namespace DeathChest.UI.Components
{
    public class UITitle : UIElement
    {
        private readonly string _titleKey;
        private bool _visible = true;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public UITitle(string localizationKey = "Mods.DeathChest.UI.Title")
        {
            _titleKey = localizationKey;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!_visible) return;

            string title = Language.GetTextValue(_titleKey);
            Vector2 titleSize = FontAssets.MouseText.Value.MeasureString(title);
            Vector2 titlePos = GetDimensions().Center();

            Color color = GetTitleColor();

            ChatManager.DrawColorCodedStringWithShadow(
                spriteBatch,
                FontAssets.MouseText.Value,
                title,
                titlePos,
                color,
                0f,
                titleSize / 2f,
                Vector2.One,
                -1f,
                1.5f
            );
        }

        private static Color GetTitleColor()
        {
            float colorMultiplier = 0.97f * (1f - (255f - Main.mouseTextColor) / 255f * 0.5f);
            return new Color(255, 255, 255, 255) * colorMultiplier;
        }
    }

    public class UILootAllButton : UIElement
    {
        private const float SCALE_MIN = 0.75f;
        private const float SCALE_MAX = 1f;
        private const float SCALE_SPEED = 0.05f;

        private float _scale = SCALE_MIN;
        private bool _hovered;
        private bool _visible = true;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public bool IsHovered => _hovered;

        public event System.Action OnClicked;

        public override void Update(GameTime gameTime)
        {
            if (!_visible) return;

            if (_hovered)
            {
                _scale = MathHelper.Min(_scale + SCALE_SPEED, SCALE_MAX);
            }
            else
            {
                _scale = MathHelper.Max(_scale - SCALE_SPEED, SCALE_MIN);
            }

            base.Update(gameTime);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!_visible) return;

            CalculateAndUpdateButtonState(out Rectangle hitbox, out Rectangle expandedHitbox, out Vector2 position, out Vector2 textSize);
            DrawButton(spriteBatch, position, textSize);
        }

        private void CalculateAndUpdateButtonState(out Rectangle hitbox, out Rectangle expandedHitbox, out Vector2 position, out Vector2 textSize)
        {
            string text = Language.GetTextValue("Mods.DeathChest.UI.LootAll");
            textSize = FontAssets.MouseText.Value.MeasureString(text);
            position = GetDimensions().Center();

            hitbox = new Rectangle(
                (int)(position.X - textSize.X / 2f),
                (int)(position.Y - 12),
                (int)textSize.X,
                24
            );

            expandedHitbox = new Rectangle(
                hitbox.X - 10,
                hitbox.Y,
                hitbox.Width + 16,
                hitbox.Height
            );

            bool hovering = _hovered
                ? expandedHitbox.Contains(Main.MouseScreen.ToPoint())
                : hitbox.Contains(Main.MouseScreen.ToPoint());

            if (hovering != _hovered)
            {
                _hovered = hovering;
                if (_hovered)
                    SoundEngine.PlaySound(SoundID.MenuTick);
            }

            if (hovering && !PlayerInput.IgnoreMouseInterface)
            {
                Main.LocalPlayer.mouseInterface = true;

                if (Main.mouseLeft && Main.mouseLeftRelease)
                {
                    SoundEngine.PlaySound(SoundID.Grab);
                    OnClicked?.Invoke();
                }
            }
        }

        private void DrawButton(SpriteBatch spriteBatch, Vector2 position, Vector2 textSize)
        {
            string text = Language.GetTextValue("Mods.DeathChest.UI.LootAll");
            Color color = _hovered ? Main.OurFavoriteColor : GetDefaultButtonColor();

            ChatManager.DrawColorCodedStringWithShadow(
                spriteBatch,
                FontAssets.MouseText.Value,
                text,
                position,
                color,
                0f,
                textSize / 2f,
                Vector2.One * _scale,
                -1f,
                1.5f
            );
        }

        private static Color GetDefaultButtonColor()
        {
            float colorMultiplier = 0.97f * (1f - (255f - Main.mouseTextColor) / 255f * 0.5f);
            return new Color(255, 255, 255, 255) * colorMultiplier;
        }
    }

    public class UISlotGrid : UIElement
    {
        private const float SLOT_SIZE = 52f;
        private const float PADDING = 4f;

        private readonly int _slotsX;
        private readonly int _slotsY;
        private readonly List<ImprovisedItemSlot> _slots;
        private bool _visible = true;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public UISlotGrid(int slotsX, int slotsY, List<ImprovisedItemSlot> slots)
        {
            _slotsX = slotsX;
            _slotsY = slotsY;
            _slots = slots;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!_visible) return;

            if (ContainsPoint(Main.MouseScreen))
            {
                Main.LocalPlayer.mouseInterface = true;
            }

            for (int i = 0; i < _slotsX; i++)
            {
                for (int j = 0; j < _slotsY; j++)
                {
                    DrawSlot(spriteBatch, i, j);
                }
            }
        }

        private void DrawSlot(SpriteBatch spriteBatch, int x, int y)
        {
            int slotIndex = x + y * _slotsX;
            Vector2 position = GetSlotPosition(x, y);

            // Draw slot background
            spriteBatch.Draw(
                TextureAssets.InventoryBack.Value,
                position,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                Main.inventoryScale,
                SpriteEffects.None,
                0f
            );

            // Draw item if exists
            if (slotIndex < _slots.Count && !_slots[slotIndex].StoredItem.IsAir)
            {
                Item item = _slots[slotIndex].StoredItem;
                ItemSlot.Draw(spriteBatch, ref item, ItemSlot.Context.ChestItem, position);

                if (IsMouseInSlot(position))
                {
                    _slots[slotIndex].HandleItemSlotLogic();
                }
            }
        }

        private Vector2 GetSlotPosition(int x, int y)
        {
            var dims = GetDimensions();
            return dims.Position() + new Vector2(
                x * (SLOT_SIZE + PADDING) * Main.inventoryScale,
                y * (SLOT_SIZE + PADDING) * Main.inventoryScale
            );
        }

        private bool IsMouseInSlot(Vector2 position)
        {
            return Utils.FloatIntersect(
                Main.mouseX,
                Main.mouseY,
                0f,
                0f,
                position.X,
                position.Y,
                TextureAssets.InventoryBack.Width() * Main.inventoryScale,
                TextureAssets.InventoryBack.Height() * Main.inventoryScale
            );
        }
    }
}