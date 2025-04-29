using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using NAudio.Wave;
using Pv;
using DotNetEnv;

namespace Jarvis.Waker
{
    public class Waker : IDisposable
    {
        private readonly Porcupine       _porcupine;
        private readonly WaveInEvent     _wakeDetector;
        private readonly WaveInEvent     _speechRecorder;
        private WaveFileWriter?          _waveWriter;
        private bool                     _isDetecting;
        private bool                     _isRecordingSpeech;
        private string?                  _tempFilePath;

        private readonly string          _azureApiKey;
        private readonly string          _transcribeEndpoint;

        public Waker()
        {
            // 1) .env 로드
            Env.Load();

            // 2) 키 & 엔드포인트 읽기
            _azureApiKey = Environment.GetEnvironmentVariable("AZURE_API_KEY")
                           ?? throw new InvalidOperationException("AZURE_API_KEY가 설정되지 않았습니다.");
            _transcribeEndpoint = Environment.GetEnvironmentVariable("AZURE_TRANSCRIBE_ENDPOINT")
                                  ?? throw new InvalidOperationException("AZURE_TRANSCRIBE_ENDPOINT가 설정되지 않았습니다.");

            var porcKey = Environment.GetEnvironmentVariable("PORCUPINE_API_KEY")
                          ?? throw new InvalidOperationException("PORCUPINE_API_KEY가 설정되지 않았습니다.");

            // 3) Porcupine 초기화
            _porcupine = Porcupine.FromBuiltInKeywords(
                accessKey: porcKey,
                keywords: new List<BuiltInKeyword> { BuiltInKeyword.JARVIS },
                sensitivities: new List<float> { 0.6f }
            );

            // 4) 웨이크 워드 감지용 WaveIn
            _wakeDetector = new WaveInEvent
            {
                WaveFormat = new WaveFormat(_porcupine.SampleRate, 16, 1),
                BufferMilliseconds = (int)(_porcupine.FrameLength * 1000.0 / _porcupine.SampleRate)
            };
            _wakeDetector.DataAvailable += OnWakeDataAvailable;

            // 5) 사용자 음성 녹음용 WaveIn
            _speechRecorder = new WaveInEvent
            {
                WaveFormat = new WaveFormat(_porcupine.SampleRate, 16, 1),
                BufferMilliseconds = _wakeDetector.BufferMilliseconds
            };
            _speechRecorder.DataAvailable  += OnSpeechDataAvailable;
            _speechRecorder.RecordingStopped += OnSpeechRecordingStopped;
        }

        public void Start()
        {
            if (!_isDetecting)
            {
                _wakeDetector.StartRecording();
                _isDetecting = true;
                Console.WriteLine("Listening for wake word...");
            }
        }

        public void Stop()
        {
            if (_isDetecting)
            {
                _wakeDetector.StopRecording();
                _isDetecting = false;
                Console.WriteLine("Stopped listening.");
            }
            if (_isRecordingSpeech)
            {
                _speechRecorder.StopRecording();
            }
        }

        private void OnWakeDataAvailable(object? sender, WaveInEventArgs e)
        {
            var pcm = new short[e.BytesRecorded / 2];
            Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);
            if (_porcupine.Process(pcm) >= 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔔 Wake word detected!");
                TriggerSpeechRecording();
            }
        }

        private void TriggerSpeechRecording()
        {
            if (_isRecordingSpeech) return;

            _isRecordingSpeech = true;
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
            _waveWriter = new WaveFileWriter(_tempFilePath, _speechRecorder.WaveFormat);

            Console.WriteLine(">>> Recording user speech for 5 seconds...");
            _speechRecorder.StartRecording();

            Task.Delay(TimeSpan.FromSeconds(5))
                .ContinueWith(_ => _speechRecorder.StopRecording());
        }

        private void OnSpeechDataAvailable(object? sender, WaveInEventArgs e)
        {
            _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            _waveWriter?.Flush();
        }

        private async void OnSpeechRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _waveWriter?.Dispose();
            _waveWriter = null;
            _isRecordingSpeech = false;

            if (string.IsNullOrEmpty(_tempFilePath) || !File.Exists(_tempFilePath))
            {
                Console.WriteLine(">>> No audio file to transcribe.");
                return;
            }

            Console.WriteLine(">>> Recording stopped. Sending to transcription service...");
            try
            {
                var text = await TranscribeAsync(_tempFilePath);
                Console.WriteLine($">>> Transcript: {text}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> Transcription error: {ex.Message}");
            }
            finally
            {
                File.Delete(_tempFilePath);
            }
        }

        private async Task<string> TranscribeAsync(string filePath)
        {
            using var client = new HttpClient();
            using var form   = new MultipartFormDataContent();

            client.DefaultRequestHeaders.Add("api-key", _azureApiKey);

            var bytes = await File.ReadAllBytesAsync(filePath);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            form.Add(fileContent, "file", Path.GetFileName(filePath));
            form.Add(new StringContent("gpt-4o-transcribe"), "model");

            var response = await client.PostAsync(_transcribeEndpoint, form);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            return json;
        }

        public void Dispose()
        {
            Stop();
            _wakeDetector.DataAvailable  -= OnWakeDataAvailable;
            _speechRecorder.DataAvailable -= OnSpeechDataAvailable;
            _speechRecorder.RecordingStopped -= OnSpeechRecordingStopped;
            _porcupine.Dispose();
            _wakeDetector.Dispose();
            _speechRecorder.Dispose();
        }
    }
}
