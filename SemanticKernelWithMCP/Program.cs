using Microsoft.SemanticKernel;
using DotNetEnv;
using OpenAI;
using System.ClientModel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

using SemanticKernelWithMCP.TTS;

string envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
Env.Load(envPath);
var token = Environment.GetEnvironmentVariable("GITHUB_ACCESS_TOKEN");

if (string.IsNullOrEmpty(token))
{
    throw new InvalidOperationException("The GITHUB_ACCESS_TOKEN environment variable is not set.");
}

var client = new OpenAIClient(
    credential: new ApiKeyCredential(token),
    options: new OpenAIClientOptions { Endpoint = new Uri("https://models.inference.ai.azure.com") });

var kernel = Kernel.CreateBuilder()
                   .AddOpenAIChatCompletion(
                        modelId: "gpt-4o",
                        openAIClient: client,
                        serviceId: "github")
                   .Build();

Console.WriteLine("Obsidian 저장소 경로를 입력하세요 (입력하지 않으면 기본 경로 사용):");
string? obsidianVaultPath = Console.ReadLine();
if (string.IsNullOrWhiteSpace(obsidianVaultPath))
{
    // obsidianVaultPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    obsidianVaultPath = "/Users/yoonkeumjae/dev/obsidian";
    Console.WriteLine($"기본 경로를 사용합니다: {obsidianVaultPath}");
}

var transportOptions = new StdioClientTransportOptions
{
    Name = "mcp-obsidian",
    Command = "npx",
    Arguments = ["-y", "mcp-obsidian", obsidianVaultPath],
    WorkingDirectory = Directory.GetCurrentDirectory()
};

Console.WriteLine($"MCP 서버를 시작합니다: {transportOptions.Command} {string.Join(" ", transportOptions.Arguments)}");

try
{
    await using var mcpClient = await McpClientFactory.CreateAsync(
        new StdioClientTransport(transportOptions));

    var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
    Console.WriteLine($"MCP 도구 {tools.Count}개를 불러왔습니다:");
    
    foreach (var tool in tools)
    {
        Console.WriteLine($"- {tool.Name}: {tool.Description}");
    }

    kernel.Plugins.AddFromFunctions(
        "MCPTools",
        tools.Select(tool => tool.AsKernelFunction()));

    var settings = new PromptExecutionSettings
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
    };

    Console.WriteLine("\n대화를 시작합니다. 종료하려면 빈 줄을 입력하세요.");
    string? input;
    while (true)
    {
        Console.Write("\nUser: ");
        input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
        {
            break;
        }

        Console.Write("Assistant: ");

        var responseStream = kernel.InvokePromptStreamingAsync(
            promptTemplate: input,
            arguments: new KernelArguments(settings) { { "ServiceId", "github" } });

        string responseText = "";
        await foreach (var content in responseStream)
        {
            responseText += content;
            Console.Write(content);
        }
        await TTS.Speak(responseText);
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"오류 발생: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}