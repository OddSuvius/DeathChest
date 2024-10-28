using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria;
using System;

namespace DeathChest.UI
{
    public class UISystem : ModSystem
    {
        private DeathChestUI _deathChestUI;
        private UserInterface _interface;
        private GameTime _lastUpdateUiGameTime;
        private bool _lastPlayerInventory = false;
        internal UserInterface Interface => _interface;
        public DeathChestUI DeathChestUIInstance => _deathChestUI;

        public override void Load()
        {
            if (Main.dedServ)
                return;

            _deathChestUI = new DeathChestUI();
            _deathChestUI.Activate();
            _interface = new UserInterface();
        }

        public override void Unload()
        {
            HideUI();
            _deathChestUI = null;
            _interface = null;
            _lastUpdateUiGameTime = null;
        }



        public override void UpdateUI(GameTime gameTime)
        {
            _lastUpdateUiGameTime = gameTime;
            if (_interface?.CurrentState != null)
            {
                // Close UI if inventory closes
                if (!Main.playerInventory && _lastPlayerInventory == true)
                {
                    Main.LocalPlayer.GetModPlayer<DeathChestPlayer>().CloseChest();
                    return;
                }

                _interface.Update(gameTime);
            }
            _lastPlayerInventory = Main.playerInventory;
        }


        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int inventoryIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Inventory"));
            if (inventoryIndex != -1)
            {
                layers.Insert(inventoryIndex + 1, new LegacyGameInterfaceLayer(
                    "DeathChest: UI",
                    delegate {
                        if (_lastUpdateUiGameTime != null && _interface?.CurrentState != null)
                        {
                            _interface.Draw(Main.spriteBatch, _lastUpdateUiGameTime);
                        }
                        return true;
                    },
                    InterfaceScaleType.UI)
                );
            }
        }

        public void ShowUI()
        {
                Logging.PublicLogger.Info("[Death Chest] Showing UI");
                _interface.SetState(_deathChestUI);
                Main.recBigList = false;
                Main.CreativeMenu.CloseMenu();
            }

        public void HideUI() {
        
            if (_interface?.CurrentState != null)
            {
                _interface.SetState(null);
            }
        }

        public bool IsUIOpen() => _interface?.CurrentState != null;
    }
}