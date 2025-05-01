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
using System.Threading;
using ModelContextProtocol.Client;
using System.Collections.Generic;

using Jarvis.TTS;
using Jarvis.MCP;

namespace Jarvis.SemanticKernel;

public class KernelApp
{
    private Kernel _kernel;
    // ChatHistory를 static으로 변경하여 인스턴스 간에 공유되도록 함
    private static ChatHistory? _history;
    // 대화 히스토리 최대 저장 개수 (너무 많은 대화가 쌓이는 것을 방지)
    private const int MaxHistoryMessages = 20;
    private string _commandInput = string.Empty;
    private Task? _wakerTask;
    private KernelArguments? _promptSettings;
    private IMcpClient? _mcpClient; // McpClient → IMcpClient 인터페이스로 변경
    private IChatCompletionService? _chatCompletionService; // 채팅 완성 서비스 추가

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

        // ChatHistory가 아직 생성되지 않은 경우에만 새로 생성
        if (_history == null)
        {
            _history = new ChatHistory();
            // 시스템 메시지 추가 - AI 비서 역할 지정
            _history.AddSystemMessage("당신은 Jarvis라는 이름의 AI 비서입니다. 사용자의 질문에 친절하고 정확하게 대답해주세요. 이전 대화 내용을 기억하고 맥락을 유지하세요.");
            Console.WriteLine("새로운 대화 세션이 시작되었습니다.");
        }
        else
        {
            Console.WriteLine($"기존 대화 세션을 이어갑니다. (저장된 대화: {_history.Count}건)");
        }

        this._kernel = Kernel.CreateBuilder()
                            .AddOpenAIChatCompletion(
                                    modelId: "gpt-4o",
                                    openAIClient: client,
                                    serviceId: "github")
                            .Build();
                            
        // 채팅 완성 서비스 가져오기
        _chatCompletionService = this._kernel.GetRequiredService<IChatCompletionService>();
    }

    // KernelApp 초기화를 위한 비동기 메서드
    public async Task InitializeAsync()
    {
        // MCP 서비스 초기화
        var mcpService = new Jarvis.MCP.MCP(this._kernel);
        // MCP 서버 시작 
        _mcpClient = await mcpService.StartMcpServerAsync();
        _promptSettings = mcpService.CreatePromptSettings();
    }

    public async Task StartProcessAsync(string prompt)
    {
        try
        {
            // 아직 초기화되지 않았다면 초기화 수행
            if (_promptSettings == null)
            {
                await InitializeAsync();
            }

            if (_chatCompletionService == null)
            {
                throw new InvalidOperationException("채팅 완성 서비스가 초기화되지 않았습니다.");
            }

            // 히스토리가 최대 개수를 초과하면 가장 오래된 메시지 쌍(유저+어시스턴트)을 제거
            if (_history != null && _history.Count >= MaxHistoryMessages)
            {
                // 시스템 메시지는 항상 유지해야 하므로, 메시지 유형 확인
                if (_history.Count > 0 && _history[0].Role == AuthorRole.System)
                {
                    // 시스템 메시지 다음부터 제거
                    _history.RemoveAt(1); // 첫 번째 사용자 메시지 제거
                    
                    // 어시스턴트 메시지가 있는지 확인 후 제거
                    if (_history.Count > 1)
                    {
                        _history.RemoveAt(1); // 첫 번째 어시스턴트 메시지 제거
                    }
                }
                else
                {
                    // 시스템 메시지가 없으면 처음부터 제거
                    _history.RemoveAt(0); // 사용자 메시지 제거
                    
                    if (_history.Count > 0)
                    {
                        _history.RemoveAt(0); // 어시스턴트 메시지 제거
                    }
                }

                Console.WriteLine("오래된 대화 내용이 제거되었습니다.");
            }

            // 현재 명령을 히스토리에 추가
            _history?.AddUserMessage(prompt);

            Console.Write("Assistant: ");

            // 채팅 완성 서비스를 사용하여 스트리밍 응답 가져오기
            var settings = new OpenAIPromptExecutionSettings 
            { 
                MaxTokens = 1000,
                Temperature = 0.7,
                TopP = 0.95,
                FrequencyPenalty = 0,
                PresencePenalty = 0,
            };

            // 프롬프트 설정을 적용
            var kernelArguments = new KernelArguments();
            if (_promptSettings != null)
            {
                foreach (var item in _promptSettings)
                {
                    kernelArguments.Add(item.Key, item.Value);
                }
            }
            
            // 이제 올바르게 ChatCompletionService를 사용하여 대화 맥락을 전달
            var responseStream = _chatCompletionService.GetStreamingChatMessageContentsAsync(
                _history!,
                executionSettings: settings,
                kernel: _kernel);

            string responseText = "";
            await foreach (var content in responseStream)
            {
                if (content.Content != null)
                {
                    responseText += content.Content;
                    Console.Write(content.Content);
                }
            }

            // 어시스턴트 응답을 히스토리에 추가
            _history?.AddAssistantMessage(responseText);

            // TTS 출력
            await TTS.TTS.Speak(responseText);
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"오류 발생: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
