using System;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DotNetEnv;

namespace Jarvis.TTS;

public static class TTS
{
    public static async Task Speak(string text)
    {
        string envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        Env.Load(envPath);
        var apiKey = Environment.GetEnvironmentVariable("AZURE_API_KEY");
        var url = Environment.GetEnvironmentVariable("AZURE_TTS_ENDPOINT");

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("api-key", apiKey);
            string trimString = Regex.Replace(responseText, "[^A-Za-z0-9 ]+", "");

            var json = $@"{{
                ""model"": ""tts-hd"",
                ""input"": ""{trimString}"",
                ""voice"": ""alloy""
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var responseData = await response.Content.ReadAsByteArrayAsync();

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using (var stream = new MemoryStream(responseData))
                    using (var player = new SoundPlayer(stream))
                    {
                        player.PlaySync(); 
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"tts_audio_{Guid.NewGuid()}.wav");
                    await File.WriteAllBytesAsync(tempFile, responseData);

                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "afplay",
                        Arguments = $"\"{tempFile}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    using (var process = Process.Start(processInfo))
                    {
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                            try
                            {
                                File.Delete(tempFile);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"임시 파일 삭제 중 오류: {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("현재 OS에서는 오디오 재생이 지원되지 않습니다.");
                }

                Console.WriteLine("오디오 재생 완료!");
            }
            else
            {
                Console.WriteLine($"요청 실패: {response.StatusCode}");
            }
        }
    }
}