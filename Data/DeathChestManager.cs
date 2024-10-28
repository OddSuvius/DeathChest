using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DeathChest.Data
{
    // Central storage for all death chest data
    public static class DeathChestManager
    {
        // Primary storage - NPC ID to chest data mapping 
        private static Dictionary<int, DeathChestData> _activeDeathChests = new();

        public static DeathChestData GetChestItems(int npcId) =>
            _activeDeathChests.TryGetValue(npcId, out var data) ? data : null;

        public static void RegisterChest(int npcId, DeathChestData data)
        {
            _activeDeathChests[npcId] = data;
        }

        public static void RemoveChest(int npcId)
        {
            _activeDeathChests.Remove(npcId);
        }

        public static void Clear()
        {
            _activeDeathChests.Clear();
        }

        // Useful for iterating through all chests (like in config changes)
        public static IEnumerable<KeyValuePair<int, DeathChestData>> GetAllChests() => _activeDeathChests;

        // Optional helper method to check if a player owns a chest
        public static bool PlayerOwnsChest(int npcId, Guid playerGuid)
        {
            return _activeDeathChests.TryGetValue(npcId, out var data) &&
                   data.PlayerGuid == playerGuid;
        }
    }

    public class DeathChestData
    {
        public Item[] Items { get; private set; }
        public Guid PlayerGuid { get; private set; }
        public bool IsEmpty => Items?.All(item => item == null || item.IsAir) ?? true;

        public DeathChestData(Item[] items, Guid playerGuid)
        {
            Items = items?.Select(i => i?.Clone() ?? new Item()).ToArray() ?? new Item[0];
            PlayerGuid = playerGuid;
        }

        // Optional: Only if you need saving/loading
        public TagCompound Save()
        {
            return new TagCompound
            {
                ["items"] = Items.Where(i => i != null && !i.IsAir).ToList(),
                ["playerGuid"] = PlayerGuid.ToString()
            };
        }

        public static DeathChestData Load(TagCompound tag)
        {
            if (!tag.TryGet("items", out List<Item> items) ||
                !tag.TryGet("playerGuid", out string guidString) ||
                !Guid.TryParse(guidString, out Guid playerGuid))
                return null;

            return new DeathChestData(items.ToArray(), playerGuid);
        }
    }
}