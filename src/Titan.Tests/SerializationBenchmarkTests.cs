using System.Diagnostics;
using System.Text;
using MemoryPack;
using System.Text.Json;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;
using Titan.Grains.Items;
using Xunit.Abstractions;

namespace Titan.Tests;

/// <summary>
/// Benchmark tests comparing MemoryPack vs JSON serialization performance.
/// Run with: dotnet test --filter "SerializationBenchmarkTests" --logger "console;verbosity=detailed"
/// </summary>
public class SerializationBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public SerializationBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PayloadSize_Comparison_SingleItem()
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = "legendary_sword",
            ItemLevel = 50,
            Rarity = ItemRarity.Rare,
            Name = "Doom Blade",
            Prefixes = new List<RolledModifier>
            {
                new RolledModifier { ModifierId = "mod_fire", Values = new[] { 100, 150 }, DisplayText = "+100-150 Fire Damage" }
            },
            Suffixes = new List<RolledModifier>
            {
                new RolledModifier { ModifierId = "mod_attack_speed", Values = new[] { 15 }, DisplayText = "+15% Attack Speed" }
            },
            Sockets = new List<Socket>
            {
                new Socket { Group = 0, Color = SocketColor.Red },
                new Socket { Group = 0, Color = SocketColor.Green }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        var memoryPackBytes = MemoryPackSerializer.Serialize(item);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(item);

        _output.WriteLine("=== Single Item Payload Size ===");
        _output.WriteLine($"MemoryPack: {memoryPackBytes.Length,6:N0} bytes");
        _output.WriteLine($"JSON:       {jsonBytes.Length,6:N0} bytes");
        _output.WriteLine($"Reduction:  {(1 - (double)memoryPackBytes.Length / jsonBytes.Length):P1}");

        Assert.True(memoryPackBytes.Length < jsonBytes.Length, "MemoryPack should produce smaller payloads");
    }

    [Fact]
    public void PayloadSize_Comparison_InventoryState()
    {
        var inventory = new CharacterInventoryState
        {
            Stats = new CharacterStats { Level = 50, Strength = 100, Dexterity = 50, Intelligence = 30 },
            BagGrid = InventoryGrid.Create(12, 5),
            BagItems = Enumerable.Range(0, 20).Select(i => new Item
            {
                Id = Guid.NewGuid(),
                BaseTypeId = $"item_type_{i % 10}",
                ItemLevel = i + 1,
                Rarity = (ItemRarity)(i % 4),
                Prefixes = new List<RolledModifier>
                {
                    new RolledModifier { ModifierId = $"mod_{i}", Values = new[] { i * 10 }, DisplayText = $"+{i * 10} Stat" }
                },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-i)
            }).ToDictionary(item => item.Id, item => item)
        };

        var memoryPackBytes = MemoryPackSerializer.Serialize(inventory);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(inventory);

        _output.WriteLine("=== Inventory (20 items) Payload Size ===");
        _output.WriteLine($"MemoryPack: {memoryPackBytes.Length,6:N0} bytes");
        _output.WriteLine($"JSON:       {jsonBytes.Length,6:N0} bytes");
        _output.WriteLine($"Reduction:  {(1 - (double)memoryPackBytes.Length / jsonBytes.Length):P1}");

        Assert.True(memoryPackBytes.Length < jsonBytes.Length, "MemoryPack should produce smaller payloads");
    }

    [Fact]
    public void PayloadSize_Comparison_TradeSession()
    {
        var trade = new TradeSession
        {
            TradeId = Guid.NewGuid(),
            InitiatorCharacterId = Guid.NewGuid(),
            TargetCharacterId = Guid.NewGuid(),
            SeasonId = "season-2024-winter",
            Status = TradeStatus.Pending,
            InitiatorItemIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList(),
            TargetItemIds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList(),
            InitiatorAccepted = true,
            TargetAccepted = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var memoryPackBytes = MemoryPackSerializer.Serialize(trade);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(trade);

        _output.WriteLine("=== Trade Session Payload Size ===");
        _output.WriteLine($"MemoryPack: {memoryPackBytes.Length,6:N0} bytes");
        _output.WriteLine($"JSON:       {jsonBytes.Length,6:N0} bytes");
        _output.WriteLine($"Reduction:  {(1 - (double)memoryPackBytes.Length / jsonBytes.Length):P1}");

        Assert.True(memoryPackBytes.Length < jsonBytes.Length, "MemoryPack should produce smaller payloads");
    }

    [Fact]
    public void SerializationSpeed_Comparison()
    {
        const int iterations = 10000;

        var inventory = new CharacterInventoryState
        {
            Stats = new CharacterStats { Level = 50, Strength = 100, Dexterity = 50, Intelligence = 30 },
            BagGrid = InventoryGrid.Create(12, 5),
            BagItems = Enumerable.Range(0, 20).Select(i => new Item
            {
                Id = Guid.NewGuid(),
                BaseTypeId = $"item_{i}",
                ItemLevel = i + 1,
                Rarity = ItemRarity.Normal,
                CreatedAt = DateTimeOffset.UtcNow
            }).ToDictionary(item => item.Id, item => item)
        };

        // Warmup
        for (int i = 0; i < 100; i++)
        {
            MemoryPackSerializer.Serialize(inventory);
            JsonSerializer.Serialize(inventory);
        }

        // Benchmark MemoryPack
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            MemoryPackSerializer.Serialize(inventory);
        }
        var memoryPackMs = sw.ElapsedMilliseconds;

        // Benchmark JSON
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            JsonSerializer.Serialize(inventory);
        }
        var jsonMs = sw.ElapsedMilliseconds;

        _output.WriteLine($"=== Serialization Speed ({iterations:N0} iterations) ===");
        _output.WriteLine($"MemoryPack: {memoryPackMs,6:N0} ms ({iterations * 1000.0 / memoryPackMs:N0} ops/sec)");
        _output.WriteLine($"JSON:       {jsonMs,6:N0} ms ({iterations * 1000.0 / jsonMs:N0} ops/sec)");
        _output.WriteLine($"Speedup:    {(double)jsonMs / memoryPackMs:N1}x faster");

        Assert.True(memoryPackMs < jsonMs, "MemoryPack should be faster than JSON");
    }
}
