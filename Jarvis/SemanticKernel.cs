using System;
using System.IO;
using System.ClientModel;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Logging;
using DotNetEnv;
using OpenAI;

using Jarvis.TTS;
using Jarvis.MCP;
using Jarvis.Waker;

namespace Jarvis.SemanticKernel
{
    public class KernelApp
    {
        private Kernel _kernel;
        private ChatHistory _history;

        public KernelApp()
        {
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
            
            this._history = new ChatHistory();
            
            this._kernel = Kernel.CreateBuilder()
                                .AddOpenAIChatCompletion(
                                        modelId: "gpt-4o",
                                        openAIClient: client,
                                        serviceId: "github")
                                .Build();
        }

        public async Task StartProcessAsync()
        {
            var mcpService = new Jarvis.MCP.MCP(this._kernel);

            try
            {
                // MCP 서버 시작
                await using var mcpClient = await mcpService.StartMcpServerAsync();
                var settings = mcpService.CreatePromptSettings();

                Console.WriteLine("\n대화를 시작합니다. 종료하려면 빈 줄을 입력하세요.");

                while (true)
                {
                    Console.Write("\nUser: ");
                    string? input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input))
                    {
                        break;
                    }
                    this._history.AddUserMessage(input);

                    Console.Write("Assistant: ");

                    // 스트리밍 응답 가져오기
                    var responseStream = this._kernel.InvokePromptStreamingAsync(
                        promptTemplate: input,
                        arguments: new KernelArguments(settings) { { "ServiceId", "github" } });

                    string responseText = "";
                    await foreach (var chunk in responseStream)
                    {
                        responseText += chunk;
                        Console.Write(chunk);
                    }
                    this._history.AddAssistantMessage(responseText);

                    // TTS 출력
                    string trimString = Regex.Replace(responseText, "[^A-Za-z0-9 ]+", "");
                    await TTS.TTS.Speak(trimString);
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"오류 발생: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
