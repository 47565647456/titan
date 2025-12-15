using System.CommandLine;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using Titan.LoadTests.Scenarios;

// =========================================
// Titan Load Tests - NBomber Stress Testing
// =========================================

var urlOption = new Option<string>(
    name: "--url",
    description: "API base URL (e.g., https://localhost:7001)",
    getDefaultValue: () => "https://localhost:7001");

var scenarioOption = new Option<string>(
    name: "--scenario",
    description: "Scenario to run: auth, character, trading, all",
    getDefaultValue: () => "all");

var usersOption = new Option<int>(
    name: "--users",
    description: "Number of concurrent users/requests per second",
    getDefaultValue: () => 10);

var durationOption = new Option<int>(
    name: "--duration",
    description: "Test duration in seconds",
    getDefaultValue: () => 30);

var rootCommand = new RootCommand("Titan Load Testing Tool")
{
    urlOption,
    scenarioOption,
    usersOption,
    durationOption
};

rootCommand.SetHandler((string url, string scenario, int users, int duration) =>
{
    Console.WriteLine("=========================================");
    Console.WriteLine("  Titan Load Tests - NBomber");
    Console.WriteLine("=========================================");
    Console.WriteLine($"  URL:      {url}");
    Console.WriteLine($"  Scenario: {scenario}");
    Console.WriteLine($"  Users:    {users}");
    Console.WriteLine($"  Duration: {duration}s");
    Console.WriteLine("=========================================\n");
    
    var testDuration = TimeSpan.FromSeconds(duration);
    
    ScenarioProps[] scenarios = scenario.ToLowerInvariant() switch
    {
        "auth" => [AuthScenario.Create(url, users, testDuration)],
        "character" => [CharacterScenario.Create(url, users, testDuration)],
        "trading" => [TradingScenario.Create(url, Math.Max(1, users / 2), testDuration)],
        "all" => 
        [ 
            AuthScenario.Create(url, users, testDuration),
            CharacterScenario.Create(url, Math.Max(1, users / 2), testDuration),
            TradingScenario.Create(url, Math.Max(1, users / 4), testDuration)
        ],
        _ => throw new ArgumentException($"Unknown scenario: {scenario}")
    };
    
    NBomberRunner
        .RegisterScenarios(scenarios)
        .WithReportFolder("./reports")
        .WithReportFormats(ReportFormat.Html, ReportFormat.Csv)
        .Run();
    
}, urlOption, scenarioOption, usersOption, durationOption);

return await rootCommand.InvokeAsync(args);
