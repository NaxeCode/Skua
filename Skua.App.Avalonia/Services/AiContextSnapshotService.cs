using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Services;

public sealed class AiContextSnapshotService
{
    private readonly IScriptPlayer _player;
    private readonly IScriptMap _map;
    private readonly IScriptInventory _inventory;
    private readonly IScriptHouseInv _houseInventory;
    private readonly IScriptTempInv _temporaryInventory;
    private readonly IScriptBank _bank;
    private readonly IScriptFaction _reputation;
    private readonly IScriptQuest _quests;
    private readonly IScriptBotStats _stats;

    public AiContextSnapshotService(
        IScriptPlayer player,
        IScriptMap map,
        IScriptInventory inventory,
        IScriptHouseInv houseInventory,
        IScriptTempInv temporaryInventory,
        IScriptBank bank,
        IScriptFaction reputation,
        IScriptQuest quests,
        IScriptBotStats stats)
    {
        _player = player;
        _map = map;
        _inventory = inventory;
        _houseInventory = houseInventory;
        _temporaryInventory = temporaryInventory;
        _bank = bank;
        _reputation = reputation;
        _quests = quests;
        _stats = stats;
    }

    public string LatestPath => Path.Combine(ClientFileSources.SkuaDIR, "AI", "context-latest.json");

    public async Task<string> WriteSnapshotAsync(bool includeBank = true)
    {
        return await Task.Run(() => WriteSnapshot(includeBank));
    }

    private string WriteSnapshot(bool includeBank)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LatestPath)!);

        List<string> warnings = new();
        DateTimeOffset timestamp = DateTimeOffset.Now;

        bool bankLoadAttempted = false;
        if (includeBank && _player.Playing)
        {
            try
            {
                bankLoadAttempted = true;
                _bank.Load(waitForLoad: true);
            }
            catch (Exception ex)
            {
                warnings.Add($"Bank load failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        try
        {
            _stats.GetSpace();
        }
        catch (Exception ex)
        {
            warnings.Add($"Inventory/bank space refresh failed: {ex.GetType().Name}: {ex.Message}");
        }

        var snapshot = new
        {
            schema = "skua.ai-context.v1",
            generatedAt = timestamp,
            source = new
            {
                app = "Skua.App.Avalonia",
                version = ClientFileSources.AssemblyVersion,
                file = LatestPath,
                note = "On-demand local context snapshot for AI planning/script generation."
            },
            warnings,
            player = new
            {
                _player.Username,
                _player.LoggedIn,
                _player.Loaded,
                _player.Playing,
                _player.Alive,
                _player.Level,
                _player.XP,
                _player.RequiredXP,
                _player.Gold,
                _player.Coins,
                _player.Health,
                _player.MaxHealth,
                _player.Mana,
                _player.MaxMana,
                _player.State,
                _player.Cell,
                _player.Pad,
                _player.Guild,
                _player.IsMember,
                CurrentClass = ToItem(_player.CurrentClass),
                CurrentClassRank = _player.CurrentClassRank,
                Stats = _player.Stats,
                Skills = _player.Skills
            },
            map = new
            {
                _map.Name,
                _map.FullName,
                _map.RoomID,
                _map.Loaded,
                _map.Loading,
                _map.PlayerCount,
                _map.Cells,
                _map.FileName,
                _map.FilePath
            },
            inventory = new
            {
                _inventory.Slots,
                _inventory.UsedSlots,
                _inventory.FreeSlots,
                Items = Items(_inventory.Items),
                Equipped = Items(_inventory.Items.Where(i => i.Equipped || i.Wearing))
            },
            bank = new
            {
                includeBank,
                bankLoadAttempted,
                _bank.Loaded,
                _bank.Slots,
                _bank.UsedSlots,
                _bank.FreeSlots,
                Items = includeBank && _bank.Loaded ? Items(_bank.Items) : new List<object>()
            },
            houseInventory = new
            {
                _houseInventory.Slots,
                _houseInventory.UsedSlots,
                _houseInventory.FreeSlots,
                Items = Items(_houseInventory.Items)
            },
            temporaryInventory = new
            {
                Items = _temporaryInventory.Items.Select(ToItem).ToList()
            },
            reputation = _reputation.FactionList
                .OrderBy(f => f.Name)
                .Select(f => new { f.ID, f.Name, f.Rank, f.TotalRep, f.Rep, f.RequiredRep, f.RemainingRep })
                .ToList(),
            quests = new
            {
                Active = _quests.Active.Select(ToQuest).ToList(),
                Completed = _quests.Completed.Select(ToQuest).ToList(),
                Registered = _quests.Registered.ToList(),
                CachedCount = _quests.Cached.Count,
                TreeCount = _quests.Tree.Count
            },
            botStats = new
            {
                _stats.Deaths,
                _stats.Drops,
                _stats.Kills,
                _stats.QuestsAccepted,
                _stats.QuestsCompleted,
                _stats.Relogins,
                _stats.InventorySpace,
                _stats.InventoryFilledSpace,
                _stats.InventoryFreeSpace,
                _stats.BankSpace,
                _stats.BankFilledSpace,
                _stats.BankFreeSpace
            }
        };

        string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
        File.WriteAllText(LatestPath, json);

        string historyPath = Path.Combine(
            Path.GetDirectoryName(LatestPath)!,
            $"context-{timestamp:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(historyPath, json);

        return LatestPath;
    }

    private static List<object> Items(IEnumerable<InventoryItem> items)
    {
        return items
            .OrderBy(i => i.CategoryString)
            .ThenBy(i => i.Name)
            .Select(ToItem)
            .ToList();
    }

    private static object ToQuest(Skua.Core.Models.Quests.Quest quest)
    {
        return new
        {
            quest.ID,
            quest.Name,
            quest.Index,
            quest.Slot,
            quest.Value,
            quest.Status,
            quest.Active,
            quest.Once,
            quest.Upgrade,
            quest.Level,
            quest.RequiredClassID,
            quest.RequiredClassPoints,
            quest.RequiredFactionId,
            quest.RequiredFactionRep,
            quest.Gold,
            quest.XP,
            Requirements = quest.Requirements.Select(ToItem).ToList(),
            AcceptRequirements = quest.AcceptRequirements.Select(ToItem).ToList(),
            Rewards = quest.Rewards.Select(ToItem).ToList()
        };
    }

    private static object ToItem(ItemBase? item)
    {
        if (item is null)
            return new { };

        return new
        {
            item.ID,
            item.Name,
            item.Description,
            item.Quantity,
            item.MaxStack,
            item.Upgrade,
            item.Wearing,
            item.Coins,
            item.CategoryString,
            Category = item.Category.ToString(),
            item.EnhancementPatternID,
            item.ProcID,
            item.Temp,
            item.ItemGroup,
            item.FileName,
            item.FileLink,
            item.Meta,
            CharItemID = item is InventoryItem inventoryItem ? inventoryItem.CharItemID : 0,
            Equipped = item is InventoryItem inventoryItem2 && inventoryItem2.Equipped,
            Level = item is InventoryItem inventoryItem3 ? inventoryItem3.Level : 0,
            EnhancementLevel = item is InventoryItem inventoryItem4 ? inventoryItem4.EnhancementLevel : 0
        };
    }
}
