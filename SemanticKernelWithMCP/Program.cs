using Microsoft.SemanticKernel;
using DotNetEnv;
using OpenAI;
using System.ClientModel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

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

// Obsidian 저장소 경로 설정 - 실제 경로로 수정하세요
Console.WriteLine("Obsidian 저장소 경로를 입력하세요 (입력하지 않으면 기본 경로 사용):");
string? obsidianVaultPath = Console.ReadLine();
if (string.IsNullOrWhiteSpace(obsidianVaultPath))
{
    obsidianVaultPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    Console.WriteLine($"기본 경로를 사용합니다: {obsidianVaultPath}");
}

// MCP 클라이언트 생성 - mcp-obsidian 서버와 연결
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
    // MCP 클라이언트 생성 및 연결
    await using var mcpClient = await McpClientFactory.CreateAsync(
        new StdioClientTransport(transportOptions));

    // MCP 도구 목록 가져오기
    var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
    Console.WriteLine($"MCP 도구 {tools.Count}개를 불러왔습니다:");
    
    foreach (var tool in tools)
    {
        Console.WriteLine($"- {tool.Name}: {tool.Description}");
    }

    // Semantic Kernel에 MCP 도구 등록
    kernel.Plugins.AddFromFunctions(
        "MCPTools",
        tools.Select(tool => tool.AsKernelFunction()));

    // 프롬프트 설정
    var settings = new PromptExecutionSettings
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
    };

    // 대화 루프
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

        // 응답 생성 및 출력
        var responseStream = kernel.InvokePromptStreamingAsync(
            promptTemplate: input,
            arguments: new KernelArguments(settings) { { "ServiceId", "github" } });

        string responseText = "";
        await foreach (var content in responseStream)
        {
            await Task.Delay(10); // 출력 속도 조절
            responseText += content;
            Console.Write(content);
        }
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"오류 발생: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}