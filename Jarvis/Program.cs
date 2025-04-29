using Microsoft.SemanticKernel;
using DotNetEnv;
using OpenAI;
using System.ClientModel;
using System.Threading.Tasks;

using Jarvis.SemanticKernel;

public class Program
{
    public static async Task Main(string[] args)
    {
        KernelApp kernelApp = new KernelApp();
        await kernelApp.StartProcessAsync();
    }
}