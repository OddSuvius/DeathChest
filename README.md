# Death Chest Suvius'

A tModLoader mod for Terraria. When a character dies, everything their death drops goes into a Death Chest at the death spot instead of onto the ground. Every difficulty works this way: Classic and Journey characters drop like Mediumcore.

## Design rule

The chest takes exactly what the game's own death drop hands over, and nothing else.

- **Vanilla stays vanilla.** The starter kit (copper tools) is held back and handed out again as in Mediumcore, a reforged copper tool comes back plain as in vanilla, and the trash slot is cleared on death.
- **Other mods are not fought.** Items another mod drops on death through tModLoader's normal drop path, or spawns with a death source, go into the chest. If a mod keeps items in its own storage, or holds them back on death on purpose, the chest leaves them alone.
- **Nothing is ever created, duplicated or lost.** Every way an item can enter or leave a chest is built and tested around this, including in multiplayer with a dropped connection.

## What players see

- **The chest.** It sits where the character died and glows pink. Hovering it, in the world or on the map, shows "_Name_'s Death Chest (died _date and time_)". Right-click opens it.
- **Taking items.** The chest window sits where a vanilla chest's does and only lets items out. Left-click takes a stack onto the cursor, right-click takes one, shift-click moves an item to the inventory, and Loot All takes everything that fits. Whatever doesn't fit stays in the chest. A chest of any size scrolls with the mouse wheel or the scrollbar. An empty chest disappears.
- **Try to Equip.** Puts every item that was equipped at death back into its slot: armor, accessories, vanity, dyes, the equipment slots (pet, light pet, minecart, mount, hook), the other loadouts, and modded accessory slots on every loadout. An item only goes back if its slot is empty and it could be equipped there by hand. Anything else stays in the chest.
- **Private Death Chests** (server setting, on by default). A chest is visible to and usable by only the character who died. Turning it off makes every chest visible and usable by everyone; the change applies immediately, without a reload. Only the host can change it. A hardcore character's chest stays private like any other, so reaching its items after that character is gone means turning the setting off.

## Mechanics

- **Everything that would drop is collected instead.**
- **The cursor item is not lost.** It reaches the drop only through inventory slot 58, which vanilla refreshes once per frame. A death between picking an item up and that refresh would delete it, so the slot is refreshed at the start of the death.

### Multiplayer (`PacketHandler.cs`, `Journal.cs`)

The server owns every chest's contents and clients only ask. Items that cross the network are kept on the side that has them until the other side confirms:

- **A death (client to server).** The client keeps the death's items in its player file until the server confirms the chest exists. On the next join to that world it sends them again, and the server skips deaths it already handled.
- **Taking items (server to client).** The server removes the item, keeps it in its world-saved journal, and sends it. The client confirms receipt. On the next join the server sends again whatever wasn't confirmed, and the client skips deliveries it already received.

The player file is saved together with the inventory, so even a dropped connection leaves every item in exactly one place, and it reaches its owner on rejoin.

Further details:

- **One player per chest,** like vanilla chests. A request to open a chest in use is ignored.
- **Large item lists** are split across several packets to stay under tModLoader's packet size limit. A list that arrives with a piece missing is thrown away rather than used.
- **Items travel in tModLoader's save format,** the same data a world or player file keeps, because some mods' network sync leaves item data out.

## Limits

- **Crashes.** A crash of the game or the server rolls the player file and the world back to their last saves, which may be from different moments. Vanilla chests and inventories share this limit.
- **Copied player files.** Two copies of one character share its id and its pending transfers.
- **Mods outside the normal death drop.** A mod that deletes items on death without dropping them, or keeps them in its own storage (for example Wing Slot Extra with its own "Loadout Support" option turned on), is outside what the chest handles.
