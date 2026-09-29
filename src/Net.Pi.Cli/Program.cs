using Net.Pi.Ai;
using Net.Pi.Ai.Models;
using Net.Pi.Core;
using Net.Pi.Core.Skills;
using Net.Pi.Tools;
using Net.Pi.Tui;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine(Ansi.Color(@"
  _   _      _     ____  _ 
 | \ | | ___| |_  |  _ \(_)
 |  \| |/ _ \ __| | |_) | |
 | |\  |  __/ |_  |  __/| |
 |_| \_|\___|\__| |_|   |_|
", Ansi.Cyan + Ansi.Bold));
Console.WriteLine(Ansi.GrayText(" Lightweight, Deterministic Agent Toolkit for .NET (Ported from Pi)"));
Console.WriteLine(Ansi.GrayText(" ------------------------------------------------------------------\n"));

var isMock = args.Contains("--mock");
var apiKey = Environment.GetEnvironmentVariable("NET_PI_API_KEY") 
             ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
             ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
             ?? "";
var baseUrl = Environment.GetEnvironmentVariable("NET_PI_BASE_URL") 
              ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL") 
              ?? "https://api.openai.com/v1";
var model = Environment.GetEnvironmentVariable("NET_PI_MODEL") 
            ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") 
            ?? "gpt-4o-mini";

ILlmClient llmClient;
if (isMock || string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine(Ansi.YellowText("⚠️  No API key detected or --mock specified. Running in Mock/Simulated LLM mode."));
    Console.WriteLine(Ansi.GrayText("   Set OPENAI_API_KEY or NET_PI_API_KEY to connect to real LLM providers.\n"));

    var mock = new MockLlmClient { DefaultModel = "net-pi-simulator" };
    // Provide a sample tool-use flow in mock mode
    mock.EnqueueResponse(history =>
    {
        var last = history.LastOrDefault()?.Content ?? "";
        if (last.Contains("dir", StringComparison.OrdinalIgnoreCase) || last.Contains("list", StringComparison.OrdinalIgnoreCase))
        {
            return ("I will check the files in the current directory for you.", new[]
            {
                new ToolCall("call_1", "list_dir", "{}")
            });
        }
        return ($"Echoing from Net.Pi Mock LLM: I received your request: '{last}'.", null);
    });
    mock.EnqueueResponse(history =>
    {
        return ("Based on the directory listing above, your project files look healthy and ready!", null);
    });

    llmClient = mock;
}
else
{
    Console.WriteLine(Ansi.GreenText($"✓ Connected to LLM provider at {baseUrl} (Model: {model})\n"));
    llmClient = new OpenAiCompatibleClient(apiKey, baseUrl, model);
}

var currentDir = Directory.GetCurrentDirectory();

// 1. Initialize Skills Manager
var skillManager = new SkillManager();
var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
skillManager.LoadSkillsFromDirectory(Path.Combine(homeDir, ".cc-switch", "skills"));
skillManager.LoadSkillsFromDirectory(Path.Combine(currentDir, ".skills"));
skillManager.LoadSkillsFromDirectory(Path.Combine(currentDir, "skills"));

if (skillManager.Skills.Count > 0)
{
    Console.WriteLine(Ansi.GreenText($"✓ Loaded {skillManager.Skills.Count} skill(s): {string.Join(", ", skillManager.Skills.Select(s => s.Name))}\n"));
}

// 2. Register all tools with workspace boundaries
var tools = new ITool[]
{
    new ReadFileTool(currentDir),
    new WriteFileTool(currentDir),
    new EditFileTool(currentDir),
    new ListDirTool(currentDir),
    new GlobTool(currentDir),
    new GrepTool(currentDir),
    new WebSearchTool(),
    new WebFetchTool(),
    new AgentBrowserTool(),
    new ReadSkillTool(skillManager),
    new ExecuteCommandTool(currentDir)
};

var systemPrompt = $"""
You are Pi, a fast, pragmatic, highly capable coding assistant running inside Net.Pi.
Working Directory: {currentDir}
Operating System: {Environment.OSVersion}

Conventions:
- Be concise. Keep prose brief.
- Use your tools to inspect code and solve tasks directly:
  * glob: Find files by pattern (e.g. '**/*.cs')
  * grep: Search file contents by regex or keywords (ReDoS-protected)
  * read_file: Read file lines with line numbers
  * write_file: Write or overwrite files (guarded against workspace escape)
  * edit_file: Exact string replacement
  * list_dir: List directory entries
  * web_search: Search web pages (Bing / keywords)
  * web_fetch: Fetch and parse web page markdown/text
  * agent_browser: Headless browser automation (navigate/screenshot)
  * read_skill: Read full instructions for an available skill
  * execute_command: Run terminal commands (UTF-8 encoded)
- Verify work when feasible.
{skillManager.BuildCatalogPrompt()}
""";

var loop = new AgentLoop(llmClient, tools, new AgentLoopOptions
{
    SystemPrompt = systemPrompt,
    MaxTurns = 25
});

var renderer = new AgentConsoleRenderer();

// Setup Ctrl+C cancellation handler
CancellationTokenSource? currentTurnCts = null;
Console.CancelKeyPress += (s, e) =>
{
    if (currentTurnCts != null && !currentTurnCts.IsCancellationRequested)
    {
        e.Cancel = true; // Keep process alive, cancel turn only
        currentTurnCts.Cancel();
        Console.WriteLine(Ansi.YellowText("\n[Ctrl+C received: cancelling active turn...]"));
    }
};

// Single prompt execution mode if command-line args provided
var promptArg = args.FirstOrDefault(a => !a.StartsWith("--"));
if (!string.IsNullOrWhiteSpace(promptArg))
{
    using var singleCts = new CancellationTokenSource();
    currentTurnCts = singleCts;
    Console.WriteLine(Ansi.BoldText($"User > {promptArg}"));
    try
    {
        await renderer.RenderStreamAsync(loop.RunAsync(promptArg, singleCts.Token), singleCts.Token);
    }
    finally
    {
        currentTurnCts = null;
    }
    return;
}

// Interactive REPL Mode
Console.WriteLine(Ansi.Color("Commands: /clear (clear screen), /reset (clear session history), /history (token stats), exit\n", Ansi.Gray));

while (true)
{
    Console.Write(Ansi.Color("You > ", Ansi.Green + Ansi.Bold));
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;

    var trimmed = input.Trim();
    if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) || 
        trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(Ansi.GrayText("Goodbye!"));
        break;
    }

    if (trimmed.Equals("/clear", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
    {
        Console.Clear();
        continue;
    }

    if (trimmed.Equals("/reset", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        loop.ResetHistory();
        Console.Clear();
        Console.WriteLine(Ansi.YellowText("✓ Session history reset. Fresh conversation started.\n"));
        continue;
    }

    if (trimmed.Equals("/history", StringComparison.OrdinalIgnoreCase))
    {
        var tokens = ContextCompactor.EstimateTokens(loop.History);
        Console.WriteLine(Ansi.GrayText($"Session history: {loop.History.Count} message(s), ~{tokens} estimated tokens.\n"));
        continue;
    }

    using var turnCts = new CancellationTokenSource();
    currentTurnCts = turnCts;

    try
    {
        await renderer.RenderStreamAsync(loop.RunAsync(trimmed, turnCts.Token), turnCts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine(Ansi.YellowText("\n[Turn cancelled by user]\n"));
    }
    catch (Exception ex)
    {
        Console.WriteLine(Ansi.RedText($"[Fatal Error] {ex.Message}"));
    }
    finally
    {
        currentTurnCts = null;
    }
}
