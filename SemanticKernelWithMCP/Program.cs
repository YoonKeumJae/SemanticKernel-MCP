using Microsoft.SemanticKernel;
using DotNetEnv;
using OpenAI;
using System.ClientModel;

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

var input = default(string);
var message = default(string);
while (true)
{
    Console.Write("User: ");
    input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        break;
    }

    Console.Write("Assistant: ");

    Console.WriteLine();
    
    var responseGH = kernel.InvokePromptStreamingAsync(
            promptTemplate: input,
            arguments: new KernelArguments(new PromptExecutionSettings() { ServiceId = "github" }));
    await foreach (var content in responseGH)
    {
        await Task.Delay(20);
        message += content;
        Console.Write(content);
    }
    Console.WriteLine();

    Console.WriteLine();
}