using System.Diagnostics;
using System.Text;
using MemoryPack;
using System.Text.Json;
using Titan.Abstractions.Models;
using Titan.Grains.Inventory;
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
            ItemTypeId = "legendary_sword",
            Quantity = 1,
            Metadata = new Dictionary<string, string>
            {
                ["enchant"] = "fire",
                ["durability"] = "100",
                ["level"] = "50"
            },
            AcquiredAt = DateTimeOffset.UtcNow
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
        var inventory = new InventoryGrainState
        {
            Items = Enumerable.Range(0, 100).Select(i => new Item
            {
                Id = Guid.NewGuid(),
                ItemTypeId = $"item_type_{i % 10}",
                Quantity = i + 1,
                Metadata = new Dictionary<string, string>
                {
                    ["slot"] = i.ToString(),
                    ["rarity"] = (i % 5).ToString()
                },
                AcquiredAt = DateTimeOffset.UtcNow.AddDays(-i)
            }).ToList()
        };

        var memoryPackBytes = MemoryPackSerializer.Serialize(inventory);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(inventory);

        _output.WriteLine("=== Inventory (100 items) Payload Size ===");
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

        var inventory = new InventoryGrainState
        {
            Items = Enumerable.Range(0, 50).Select(i => new Item
            {
                Id = Guid.NewGuid(),
                ItemTypeId = $"item_{i}",
                Quantity = i + 1,
                Metadata = new Dictionary<string, string> { ["key"] = "value" },
                AcquiredAt = DateTimeOffset.UtcNow
            }).ToList()
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
