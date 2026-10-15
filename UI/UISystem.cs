using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace DeathChest.UI
{
	public class UISystem : ModSystem
	{
		private UserInterface _interface;
		private DeathChestUI _state;
		private GameTime _lastGameTime;

		public DeathChestUI State => _state;
		public bool IsUIOpen() => _interface?.CurrentState != null;

		public override void Load()
		{
			if (Main.dedServ)
				return;
			_state = new DeathChestUI();
			_state.Activate();
			_interface = new UserInterface();
		}

		public void ShowUI()
		{
			_state.Reset();
			_interface.SetState(_state);
			Main.recBigList = false;
			Main.CreativeMenu.CloseMenu();
		}

		public void HideUI() => _interface?.SetState(null);

		public override void UpdateUI(GameTime gameTime)
		{
			_lastGameTime = gameTime;
			_interface?.Update(gameTime);
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int index = layers.FindIndex(layer => layer.Name == "Vanilla: Inventory");
			if (index == -1)
				return;
			layers.Insert(index + 1, new LegacyGameInterfaceLayer("DeathChest: UI", () => {
				if (_lastGameTime != null && IsUIOpen() && Main.playerInventory)
					_interface.Draw(Main.spriteBatch, _lastGameTime);
				return true;
			}, InterfaceScaleType.UI));
		}
	}
}
