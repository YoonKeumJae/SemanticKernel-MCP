using Jarvis.MCP;
using Jarvis.SemanticKernel;
using Jarvis.TTS;
using Jarvis.Waker;

namespace Jarvis.Controller
{
    public class Controller
    {
        public async Task RunAsync()
        {
            await Jarvis.Waker.Waker.Listen();
        }
    }
}