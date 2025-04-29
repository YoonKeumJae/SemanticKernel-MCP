using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Pv;
using DotNetEnv;
using System.Text;

namespace Jarvis.Waker
{
    public class Waker 
    {
        private readonly string? _accessKey;
        private bool _isListeningForCommand = false;
        private StringBuilder _commandBuffer = new StringBuilder();
        private readonly WaveFileWriter? _commandRecorder;
        private string _commandAudioPath;

        public event EventHandler<string>? CommandDetected;

        public Waker()
        {
            string envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            Env.Load(envPath);
            _accessKey = Environment.GetEnvironmentVariable("PORCUPINE_API_KEY");
            
            if (string.IsNullOrEmpty(_accessKey))
            {
                throw new InvalidOperationException("PORCUPINE_API_KEY environment variable is not set in .env file.");
            }
            
            // 명령 녹음을 위한 임시 파일 경로
            _commandAudioPath = Path.Combine(Path.GetTempPath(), "jarvis_command.wav");
        }

        public void Start()
        {
            // 1) Porcupine 엔진 초기화 (Jarvis 키워드만 감지)
            using var porcupine = Porcupine.FromBuiltInKeywords(
                _accessKey,
                new List<BuiltInKeyword> { BuiltInKeyword.JARVIS }
            );

            // 2) 마이크 입력 초기화 (NAudio 사용)
            var waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(porcupine.SampleRate, 16, 1),
                BufferMilliseconds = 20
            };

            Console.WriteLine("녹음을 시작합니다. 마이크에 'Jarvis'라고 말해보세요...");

            short[] audioBuffer = new short[porcupine.FrameLength];
            int audioBufferIndex = 0;
            
            // 명령 녹음을 위한 버퍼와 카운터
            int silenceCounter = 0;
            WaveFileWriter? commandRecorder = null;

            waveIn.DataAvailable += (sender, e) =>
            {
                // 바이트 버퍼를 16비트 short로 변환
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    if (audioBufferIndex >= audioBuffer.Length)
                    {
                        // 버퍼가 찼으면 키워드 감지 처리
                        int result = porcupine.Process(audioBuffer);
                        if (result == 0 && !_isListeningForCommand)  // Jarvis 인덱스는 0
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔔 Jarvis detected! 명령을 말씀해주세요...");
                            _isListeningForCommand = true;
                            silenceCounter = 0;
                            
                            // 명령 녹음 시작
                            if (commandRecorder != null)
                            {
                                commandRecorder.Dispose();
                            }
                            if (File.Exists(_commandAudioPath))
                            {
                                File.Delete(_commandAudioPath);
                            }
                            commandRecorder = new WaveFileWriter(_commandAudioPath, waveIn.WaveFormat);
                        }
                        audioBufferIndex = 0;
                    }

                    if (i + 1 < e.BytesRecorded)
                    {
                        short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                        audioBuffer[audioBufferIndex++] = sample;
                        
                        // 명령 모드일 때 오디오 저장
                        if (_isListeningForCommand && commandRecorder != null)
                        {
                            commandRecorder.WriteSample(sample);
                            
                            // 음성 활동 감지 (간단한 음량 기반)
                            if (Math.Abs(sample) < 500) // 소리가 작으면 침묵으로 간주
                            {
                                silenceCounter++;
                            }
                            else
                            {
                                silenceCounter = 0; // 소리가 감지되면 카운터 리셋
                            }
                            
                            // 약 2초 동안 침묵이 계속되면 명령 입력 종료
                            if (silenceCounter > porcupine.SampleRate * 2)
                            {
                                Console.WriteLine("명령 입력이 완료되었습니다. 처리 중...");
                                _isListeningForCommand = false;
                                
                                // 명령 녹음 종료 및 파일 저장
                                commandRecorder.Dispose();
                                commandRecorder = null;
                                
                                // 여기서 명령 오디오 파일을 STT로 전송하거나 처리
                                ProcessCommandAudio();
                            }
                        }
                    }
                }
            };

            // 녹음 시작
            waveIn.StartRecording();

            Console.WriteLine("아무 키나 누르면 종료합니다...");
            Console.ReadKey();

            // 정리
            waveIn.StopRecording();
            waveIn.Dispose();
        }
        
        private void ProcessCommandAudio()
        {
            if (!File.Exists(_commandAudioPath))
            {
                Console.WriteLine("명령 오디오 파일이 없습니다.");
                return;
            }
            
            // 여기서는 간단한 예시로 파일 존재 확인만 하고 이벤트를 발생시킵니다.
            // 실제 구현에서는 STT 서비스를 사용하여 오디오를 텍스트로 변환해야 합니다.
            Console.WriteLine($"명령 오디오가 저장되었습니다: {_commandAudioPath}");
            
            // 예시 명령 (실제로는 STT로 변환된 텍스트가 들어갑니다)
            string commandText = "사용자 명령 (STT로 변환 필요)";
            
            // 명령 감지 이벤트 발생
            CommandDetected?.Invoke(this, commandText);
        }
        
        // 명령 검출 메서드 (실제 STT 서비스 사용 예시)
        public async Task<string> ConvertSpeechToText()
        {
            // 여기에 실제 STT 서비스를 사용하여 _commandAudioPath 오디오를 텍스트로 변환하는 코드 구현
            // 예: Azure Speech Service, Google Speech-to-Text 등
            
            // 임시 구현 (실제로는 STT 서비스 사용)
            await Task.Delay(500); // STT 처리 시간 시뮬레이션
            return "이것은 예시 명령입니다";
        }
    }
}
