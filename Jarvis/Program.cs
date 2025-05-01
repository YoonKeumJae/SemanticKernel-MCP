using Microsoft.SemanticKernel;
using DotNetEnv;
using OpenAI;
using System.ClientModel;
using System.Threading.Tasks;
using System;

using Jarvis.SemanticKernel;
using Jarvis.MCP;
using Jarvis.Controller;

public class Program
{
    public static async Task Main(string[] args)
    {
        try 
        {
            Console.WriteLine("Jarvis 시스템을 초기화합니다...");
            
            Jarvis.Controller.Controller controller = new Jarvis.Controller.Controller();
            await controller.RunAsync();
            
            // 주석 처리된 코드는 나중에 구현할 수 있도록 유지
            // // SemanticKernel 앱 초기화
            // KernelApp kernelApp = new KernelApp();
            // // 대화 시작 및 Jarvis 키워드 감지 시작
            // await kernelApp.StartProcessAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"오류가 발생했습니다: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}