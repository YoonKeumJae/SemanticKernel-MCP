using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Jarvis.MCP;

public class MCP
{
    private readonly Kernel _kernel;
    private string? _obsidianVaultPath;

    public MCP(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    public void SetObsidianPath(string path)
    {
        _obsidianVaultPath = path;
    }

    public async Task<IMcpClient> StartMcpServerAsync()
    {
        if (string.IsNullOrWhiteSpace(_obsidianVaultPath))
        {
            Console.WriteLine("Obsidian 저장소 경로를 입력하세요 (입력하지 않으면 기본 경로 사용):");
            string? obsidianVaultPath = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(obsidianVaultPath))
            {
                // obsidianVaultPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                obsidianVaultPath = "/Users/yoonkeumjae/dev/obsidian";
                Console.WriteLine($"기본 경로를 사용합니다: {obsidianVaultPath}");
            }
            _obsidianVaultPath = obsidianVaultPath;
        }

        var transportOptions = new StdioClientTransportOptions
        {
            Name = "mcp-obsidian",
            Command = "npx",
            Arguments = ["-y", "mcp-obsidian", _obsidianVaultPath],
            WorkingDirectory = Directory.GetCurrentDirectory()
        };

        Console.WriteLine($"MCP 서버를 시작합니다: {transportOptions.Command} {string.Join(" ", transportOptions.Arguments)}");
        
        var mcpClient = await McpClientFactory.CreateAsync(
            new StdioClientTransport(transportOptions));
            
        var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
        Console.WriteLine($"MCP 도구 {tools.Count}개를 불러왔습니다:");
        
        foreach (var tool in tools)
        {
            Console.WriteLine($"- {tool.Name}: {tool.Description}");
        }

        _kernel.Plugins.AddFromFunctions(
            "MCPTools",
            tools.Select(tool => tool.AsKernelFunction()));
            
        return mcpClient;
    }

    public PromptExecutionSettings CreatePromptSettings()
    {
        return new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
        };
    }
}