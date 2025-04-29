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

using Jarvis.TTS;
using Jarvis.MCP;
using Jarvis.Waker;

namespace Jarvis.SemanticKernel
{
    public class KernelApp
    {
        private Kernel _kernel;
        private ChatHistory _history;
        private Waker.Waker _waker;
        private ManualResetEventSlim _commandWaitHandle = new ManualResetEventSlim(false);
        private string _commandInput = string.Empty;
        private Task? _wakerTask;

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
                                
            // Waker 초기화
            this._waker = new Waker.Waker();
            this._waker.CommandDetected += OnCommandDetected;
        }
        
        // Waker에서 명령이 감지되었을 때 실행되는 이벤트 핸들러
        private void OnCommandDetected(object? sender, string command)
        {
            Console.WriteLine($"\n음성 명령 감지: {command}");
            _commandInput = command;
            _commandWaitHandle.Set(); // 대기 중인 스레드에 신호 보내기
        }

        public async Task StartProcessAsync()
        {
            // 별도의 태스크로 Waker 시작 - 시작 시점에 바로 실행
            Console.WriteLine("\nJarvis 키워드 인식을 시작합니다. 'Jarvis'라고 말한 후 명령을 입력하세요.");
            _wakerTask = Task.Run(() => _waker.Start());
            
            // MCP 서비스 초기화
            var mcpService = new Jarvis.MCP.MCP(this._kernel);

            try
            {
                // MCP 서버 시작 (이 부분에서 Obsidian 경로 입력 요청)
                await using var mcpClient = await mcpService.StartMcpServerAsync();
                var settings = mcpService.CreatePromptSettings();

                Console.WriteLine("\n대화를 시작합니다. 종료하려면 빈 줄을 입력하세요.");

                while (true)
                {
                    Console.Write("\nUser: ");
                    
                    // 음성 명령 또는 키보드 입력 둘 중 하나를 대기
                    string? input;
                    
                    // 비동기 입력을 위한 태스크 생성
                    var readLineTask = Task.Run(() => Console.ReadLine());
                    
                    // 음성 명령이나 키보드 입력 중 먼저 완료되는 것을 대기
                    if (await Task.WhenAny(readLineTask, Task.Run(() => {
                        _commandWaitHandle.Wait();
                        return true;
                    })) == readLineTask)
                    {
                        // 키보드 입력이 완료됨
                        input = await readLineTask;
                    }
                    else
                    {
                        // 음성 명령이 감지됨
                        input = _commandInput;
                        _commandWaitHandle.Reset(); // 다음 명령을 위해 리셋
                        Console.WriteLine(input); // 사용자가 어떤 명령을 입력했는지 표시
                    }
                    
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
                    await TTS.TTS.Speak(responseText);
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"오류 발생: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                // 프로그램 종료 시 필요한 정리 작업
                Console.WriteLine("프로그램을 종료합니다...");
            }
        }
    }
}
