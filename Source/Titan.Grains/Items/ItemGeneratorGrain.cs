using Orleans;
using Orleans.Concurrency;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Items;

/// <summary>
/// Stateless worker for generating items.
/// </summary>
[StatelessWorker]
public class ItemGeneratorGrain : Grain, IItemGeneratorGrain
{
    private readonly IGrainFactory _grainFactory;
    private readonly Random _random = new();

    public ItemGeneratorGrain(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public async Task<Item> GenerateAsync(string baseTypeId, int itemLevel)
    {
        return await GenerateAsync(baseTypeId, itemLevel, ItemRarity.Normal);
    }

    public async Task<Item> GenerateAsync(string baseTypeId, int itemLevel, ItemRarity rarity)
    {
        var baseTypeReader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        var baseType = await baseTypeReader.GetAsync(baseTypeId);
        if (baseType == null)
            throw new ArgumentException($"Base type '{baseTypeId}' not found");

        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = itemLevel,
            Rarity = rarity,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Roll implicit if base type has one
        if (!string.IsNullOrEmpty(baseType.ImplicitModifierId))
        {
            var modReader = _grainFactory.GetGrain<IModifierReaderGrain>("default");
            var implicit_ = await modReader.RollModifierAsync(baseType.ImplicitModifierId);
            item = item with { Implicit = implicit_ };
        }

        // Roll explicit modifiers based on rarity
        item = rarity switch
        {
            ItemRarity.Magic => await RollMagicModsAsync(item, baseTypeId, itemLevel),
            ItemRarity.Rare => await RollRareModsAsync(item, baseTypeId, itemLevel),
            _ => item
        };

        // Roll sockets if applicable
        if (baseType.MaxSockets > 0)
        {
            var sockets = await RollSocketsAsync(baseTypeId, itemLevel);
            item = item with { Sockets = sockets };
        }

        // Generate name for rare items
        if (rarity == ItemRarity.Rare)
        {
            item = item with { Name = GenerateRareName() };
        }

        return item;
    }

    public async Task<Item> GenerateUniqueAsync(string uniqueId)
    {
        var uniqueRegistry = _grainFactory.GetGrain<IUniqueRegistryGrain>("default");
        var unique = await uniqueRegistry.GetAsync(uniqueId);
        if (unique == null)
            throw new ArgumentException($"Unique '{uniqueId}' not found");

        var baseTypeReader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        var baseType = await baseTypeReader.GetAsync(unique.BaseTypeId);
        if (baseType == null)
            throw new ArgumentException($"Base type '{unique.BaseTypeId}' not found for unique '{uniqueId}'");

        // Roll unique modifiers
        var prefixes = new List<RolledModifier>();
        foreach (var mod in unique.Modifiers)
        {
            var values = new int[mod.Ranges.Length];
            for (int i = 0; i < mod.Ranges.Length; i++)
            {
                values[i] = _random.Next(mod.Ranges[i].Min, mod.Ranges[i].Max + 1);
            }

            prefixes.Add(new RolledModifier
            {
                ModifierId = $"unique_{uniqueId}_{prefixes.Count}",
                Values = values,
                DisplayText = string.Format(mod.DisplayText, values.Cast<object>().ToArray())
            });
        }

        return new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = unique.BaseTypeId,
            ItemLevel = unique.RequiredItemLevel,
            Rarity = ItemRarity.Unique,
            Name = unique.Name,
            UniqueId = uniqueId,
            Prefixes = prefixes,
            Sockets = await RollSocketsAsync(unique.BaseTypeId, unique.RequiredItemLevel),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<List<Socket>> RollSocketsAsync(string baseTypeId, int itemLevel)
    {
        var baseTypeReader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        var baseType = await baseTypeReader.GetAsync(baseTypeId);
        if (baseType == null || baseType.MaxSockets == 0)
            return new List<Socket>();

        // Calculate max sockets based on item level (PoE formula approximation)
        int maxPossible = Math.Min(baseType.MaxSockets, (itemLevel / 10) + 1);
        maxPossible = Math.Max(1, maxPossible);

        // Roll number of sockets
        int socketCount = _random.Next(1, maxPossible + 1);

        // Roll socket colors based on requirements
        var sockets = new List<Socket>();
        int totalReq = baseType.RequiredStrength + baseType.RequiredDexterity + baseType.RequiredIntelligence;
        totalReq = Math.Max(totalReq, 1);

        for (int i = 0; i < socketCount; i++)
        {
            var color = RollSocketColor(baseType.RequiredStrength, baseType.RequiredDexterity, 
                                         baseType.RequiredIntelligence, totalReq);
            
            // Simple link logic: first socket starts group 0, then 50% chance to link to previous
            int group = i == 0 ? 0 : (_random.NextDouble() < 0.5 ? sockets[i - 1].Group : i);
            
            sockets.Add(new Socket { Group = group, Color = color });
        }

        return sockets;
    }

    public async Task<Item?> TransmuteAsync(Item item)
    {
        if (item.Rarity != ItemRarity.Normal)
            return null;

        return await RollMagicModsAsync(item with { Rarity = ItemRarity.Magic }, item.BaseTypeId, item.ItemLevel);
    }

    public async Task<Item?> AlterAsync(Item item)
    {
        if (item.Rarity != ItemRarity.Magic)
            return null;

        var newItem = item with { Prefixes = new List<RolledModifier>(), Suffixes = new List<RolledModifier>() };
        return await RollMagicModsAsync(newItem, item.BaseTypeId, item.ItemLevel);
    }

    public async Task<Item?> RegalAsync(Item item)
    {
        if (item.Rarity != ItemRarity.Magic)
            return null;

        // Keep existing mods, upgrade to rare and add one more mod
        var newItem = item with 
        { 
            Rarity = ItemRarity.Rare,
            Name = GenerateRareName()
        };

        var modReader = _grainFactory.GetGrain<IModifierReaderGrain>("default");
        var totalMods = newItem.Prefixes.Count + newItem.Suffixes.Count;

        if (newItem.Prefixes.Count < 3 && _random.NextDouble() < 0.5)
        {
            var prefixes = await modReader.GetAvailablePrefixesAsync(item.BaseTypeId, item.ItemLevel);
            if (prefixes.Count > 0)
            {
                var selected = SelectWeighted(prefixes);
                var rolled = await modReader.RollModifierAsync(selected.ModifierId);
                newItem = newItem with { Prefixes = newItem.Prefixes.Append(rolled).ToList() };
            }
        }
        else if (newItem.Suffixes.Count < 3)
        {
            var suffixes = await modReader.GetAvailableSuffixesAsync(item.BaseTypeId, item.ItemLevel);
            if (suffixes.Count > 0)
            {
                var selected = SelectWeighted(suffixes);
                var rolled = await modReader.RollModifierAsync(selected.ModifierId);
                newItem = newItem with { Suffixes = newItem.Suffixes.Append(rolled).ToList() };
            }
        }

        return newItem;
    }

    public async Task<Item?> ChaosAsync(Item item)
    {
        if (item.Rarity != ItemRarity.Rare)
            return null;

        var newItem = item with { Prefixes = new List<RolledModifier>(), Suffixes = new List<RolledModifier>() };
        return await RollRareModsAsync(newItem, item.BaseTypeId, item.ItemLevel);
    }

    public async Task<Item?> ExaltAsync(Item item)
    {
        if (item.Rarity != ItemRarity.Rare)
            return null;

        var modReader = _grainFactory.GetGrain<IModifierReaderGrain>("default");

        // Try to add a prefix or suffix
        bool tryPrefix = item.Prefixes.Count < 3;
        bool trySuffix = item.Suffixes.Count < 3;

        if (!tryPrefix && !trySuffix)
            return null; // Already full

        if (tryPrefix && (!trySuffix || _random.NextDouble() < 0.5))
        {
            var prefixes = await modReader.GetAvailablePrefixesAsync(item.BaseTypeId, item.ItemLevel);
            prefixes = prefixes.Where(p => item.Prefixes.All(ip => ip.ModifierId != p.ModifierId)).ToList();
            if (prefixes.Count > 0)
            {
                var selected = SelectWeighted(prefixes);
                var rolled = await modReader.RollModifierAsync(selected.ModifierId);
                return item with { Prefixes = item.Prefixes.Append(rolled).ToList() };
            }
        }

        if (trySuffix)
        {
            var suffixes = await modReader.GetAvailableSuffixesAsync(item.BaseTypeId, item.ItemLevel);
            suffixes = suffixes.Where(s => item.Suffixes.All(is_ => is_.ModifierId != s.ModifierId)).ToList();
            if (suffixes.Count > 0)
            {
                var selected = SelectWeighted(suffixes);
                var rolled = await modReader.RollModifierAsync(selected.ModifierId);
                return item with { Suffixes = item.Suffixes.Append(rolled).ToList() };
            }
        }

        return item;
    }

    public async Task<Item> JewellerAsync(Item item)
    {
        var newSockets = await RollSocketsAsync(item.BaseTypeId, item.ItemLevel);
        return item with { Sockets = newSockets };
    }

    public Task<Item> FusingAsync(Item item)
    {
        if (item.Sockets.Count < 2)
            return Task.FromResult(item);

        // Re-roll links
        var newSockets = new List<Socket>();
        for (int i = 0; i < item.Sockets.Count; i++)
        {
            int group = i == 0 ? 0 : (_random.NextDouble() < 0.5 ? newSockets[i - 1].Group : i);
            newSockets.Add(item.Sockets[i] with { Group = group });
        }

        return Task.FromResult(item with { Sockets = newSockets });
    }

    public async Task<Item> ChromaticAsync(Item item)
    {
        if (item.Sockets.Count == 0)
            return item;

        var baseTypeReader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        var baseType = await baseTypeReader.GetAsync(item.BaseTypeId);
        if (baseType == null)
            return item;

        int totalReq = baseType.RequiredStrength + baseType.RequiredDexterity + baseType.RequiredIntelligence;
        totalReq = Math.Max(totalReq, 1);

        var newSockets = item.Sockets.Select(s =>
        {
            var newColor = RollSocketColor(baseType.RequiredStrength, baseType.RequiredDexterity,
                                           baseType.RequiredIntelligence, totalReq);
            return s with { Color = newColor };
        }).ToList();

        return item with { Sockets = newSockets };
    }

    #region Private Helpers

    private async Task<Item> RollMagicModsAsync(Item item, string baseTypeId, int itemLevel)
    {
        var modReader = _grainFactory.GetGrain<IModifierReaderGrain>("default");
        
        // Magic items have 1-2 total mods
        int modCount = _random.Next(1, 3);
        var prefixes = new List<RolledModifier>();
        var suffixes = new List<RolledModifier>();

        for (int i = 0; i < modCount; i++)
        {
            bool rollPrefix = prefixes.Count == 0 || (_random.NextDouble() < 0.5 && suffixes.Count > 0);
            
            if (rollPrefix && prefixes.Count < 1)
            {
                var available = await modReader.GetAvailablePrefixesAsync(baseTypeId, itemLevel);
                available = available.Where(p => prefixes.All(ip => ip.ModifierId != p.ModifierId)).ToList();
                if (available.Count > 0)
                {
                    var selected = SelectWeighted(available);
                    prefixes.Add(await modReader.RollModifierAsync(selected.ModifierId));
                }
            }
            else if (suffixes.Count < 1)
            {
                var available = await modReader.GetAvailableSuffixesAsync(baseTypeId, itemLevel);
                available = available.Where(s => suffixes.All(is_ => is_.ModifierId != s.ModifierId)).ToList();
                if (available.Count > 0)
                {
                    var selected = SelectWeighted(available);
                    suffixes.Add(await modReader.RollModifierAsync(selected.ModifierId));
                }
            }
        }

        return item with { Prefixes = prefixes, Suffixes = suffixes };
    }

    private async Task<Item> RollRareModsAsync(Item item, string baseTypeId, int itemLevel)
    {
        var modReader = _grainFactory.GetGrain<IModifierReaderGrain>("default");
        
        // Rare items have 4-6 total mods (1-3 prefixes, 1-3 suffixes)
        int prefixCount = _random.Next(1, 4);
        int suffixCount = _random.Next(1, 4);
        
        // Ensure at least 4 total
        while (prefixCount + suffixCount < 4)
        {
            if (_random.NextDouble() < 0.5 && prefixCount < 3) prefixCount++;
            else if (suffixCount < 3) suffixCount++;
            else prefixCount++;
        }

        var prefixes = new List<RolledModifier>();
        var suffixes = new List<RolledModifier>();

        // Roll prefixes
        var availablePrefixes = await modReader.GetAvailablePrefixesAsync(baseTypeId, itemLevel);
        for (int i = 0; i < prefixCount && availablePrefixes.Count > 0; i++)
        {
            var selected = SelectWeighted(availablePrefixes);
            prefixes.Add(await modReader.RollModifierAsync(selected.ModifierId));
            availablePrefixes = availablePrefixes.Where(p => p.ModifierId != selected.ModifierId && 
                                                              p.ModifierGroup != selected.ModifierGroup).ToList();
        }

        // Roll suffixes
        var availableSuffixes = await modReader.GetAvailableSuffixesAsync(baseTypeId, itemLevel);
        for (int i = 0; i < suffixCount && availableSuffixes.Count > 0; i++)
        {
            var selected = SelectWeighted(availableSuffixes);
            suffixes.Add(await modReader.RollModifierAsync(selected.ModifierId));
            availableSuffixes = availableSuffixes.Where(s => s.ModifierId != selected.ModifierId && 
                                                              s.ModifierGroup != selected.ModifierGroup).ToList();
        }

        return item with 
        { 
            Prefixes = prefixes, 
            Suffixes = suffixes,
            Name = GenerateRareName()
        };
    }

    private ModifierDefinition SelectWeighted(IReadOnlyList<ModifierDefinition> mods)
    {
        int totalWeight = mods.Sum(m => m.Weight);
        int roll = _random.Next(totalWeight);
        
        int cumulative = 0;
        foreach (var mod in mods)
        {
            cumulative += mod.Weight;
            if (roll < cumulative)
                return mod;
        }

        return mods[^1];
    }

    private SocketColor RollSocketColor(int str, int dex, int intel, int total)
    {
        double roll = _random.NextDouble() * total;
        if (roll < str) return SocketColor.Red;
        if (roll < str + dex) return SocketColor.Green;
        if (roll < str + dex + intel) return SocketColor.Blue;
        return SocketColor.White; // Off-color
    }

    private static readonly string[] NamePrefixes = 
        { "Agony", "Apocalypse", "Armageddon", "Beast", "Behemoth", "Blight", "Blood", "Bramble", 
          "Brimstone", "Brood", "Carrion", "Cataclysm", "Chimeric", "Corpse", "Corruption", 
          "Damnation", "Death", "Demon", "Dire", "Doom", "Dragon", "Dread", "Empyrean", "Entropy" };
    
    private static readonly string[] NameSuffixes = 
        { "Bane", "Bite", "Bender", "Blow", "Breach", "Crusher", "Edge", "Fang", "Gaze", "Grasp", 
          "Grip", "Heart", "Knell", "Mark", "Needle", "Ruin", "Scratch", "Scream", "Slayer", 
          "Song", "Spark", "Strike", "Thirst", "Touch", "Wail", "Wound" };

    private string GenerateRareName()
    {
        var prefix = NamePrefixes[_random.Next(NamePrefixes.Length)];
        var suffix = NameSuffixes[_random.Next(NameSuffixes.Length)];
        return $"{prefix} {suffix}";
    }

    #endregion
}
