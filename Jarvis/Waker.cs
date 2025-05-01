using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using DotNetEnv;
using System.Threading;

namespace Jarvis.Waker
{
    public class Waker
    {
        // 음성 명령이 감지되었을 때 발생하는 이벤트
        public event EventHandler<string>? CommandDetected;

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _listeningTask;

        public async Task Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _listeningTask = Listen(_cancellationTokenSource.Token);
            
            // 비동기로 실행하고 반환
            await Task.CompletedTask;
        }

        public async Task Stop()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                if (_listeningTask != null)
                {
                    await _listeningTask;
                }
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task Listen(CancellationToken cancellationToken)
        {
            string envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            Env.Load(envPath);
            
            // AZURE_API_KEY 사용 (.env 파일에서 이 키가 유효한 Speech API 키로 보임)
            var key = Environment.GetEnvironmentVariable("AZURE_API_KEY");
            
            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine("Error: Azure Speech API 키가 .env 파일에 설정되지 않았습니다.");
                Console.WriteLine("AZURE_API_KEY 환경 변수를 확인하세요.");
                return;
            }
            
            Console.WriteLine("Azure Speech 서비스에 연결 중...");
            
            try
            {
                // 1) SpeechConfig 생성 - 리전은 .env에 없으므로 일반적인 리전 사용
                var speechConfig = SpeechConfig.FromSubscription(key, "swedencentral");
                speechConfig.SpeechRecognitionLanguage = "ko-KR"; // 한국어 인식
                
                // 2) AudioConfig 인스턴스화
                var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
                
                // 3) SpeechRecognizer 생성
                using var speechRecognizer = new SpeechRecognizer(speechConfig, audioConfig);
                
                // 4) 디버깅을 위한 이벤트 등록
                speechRecognizer.SessionStarted += (s, e) => {
                    Console.WriteLine("세션 시작됨");
                };
                
                speechRecognizer.SessionStopped += (s, e) => {
                    Console.WriteLine("세션 종료됨");
                };
                
                // 5) 인식 결과 이벤트 등록
                speechRecognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.RecognizedSpeech)
                    {
                        Console.WriteLine($"[인식 텍스트]: {e.Result.Text}");
                        
                        // "Hey Jarvis" 또는 "하이 자비스" 등의 키워드가 포함된 경우 (간단한 구현)
                        string lowerText = e.Result.Text.ToLower();
                        if (lowerText.Contains("hey jarvis") || lowerText.Contains("하이 자비스") || 
                            lowerText.Contains("자비스"))
                        {
                            Console.WriteLine("[Wake Word 감지됨]");
                            
                            // 다음 명령 대기
                            Console.WriteLine("음성 인식 모드 전환: 말하세요...");
                            
                            // 여기서는 다음 명령을 기다리기 위해 간단히 딜레이를 추가
                            Task.Delay(1000).Wait();
                            
                            // 실제로는 여기서 다른 명령을 기다려야 함
                            // 이 예제에서는 다음 음성 인식 결과를 명령으로 처리
                        }
                        else
                        {
                            // 웨이크 워드가 감지된 후의 명령으로 처리
                            CommandDetected?.Invoke(this, e.Result.Text);
                        }
                    }
                };

                // 연결 오류 이벤트 등록
                speechRecognizer.Canceled += (s, e) =>
                {
                    Console.WriteLine($"인식 취소됨: {e.Reason}");
                    if (e.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"오류 코드: {e.ErrorCode}");
                        Console.WriteLine($"오류 상세: {e.ErrorDetails}");
                    }
                };

                // 6) 연속 인식 시작
                Console.WriteLine("연속 음성 인식 시작 중...");
                await speechRecognizer.StartContinuousRecognitionAsync();
                
                Console.WriteLine("음성 인식 모드: 'Hey, Jarvis'를 말하세요...");
                
                try 
                {
                    // 취소 토큰이 호출될 때까지 대기
                    await Task.Delay(-1, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // 정상적인 취소 처리
                }
                finally 
                {
                    await speechRecognizer.StopContinuousRecognitionAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"음성 인식 초기화 중 오류 발생: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        // Controller.cs에서 호출하는 정적 메서드
        public static async Task Listen()
        {
            var waker = new Waker();
            await waker.Start();
            
            // 종료 대기
            Console.WriteLine("엔터 키를 눌러 종료합니다.");
            Console.ReadLine();
            
            await waker.Stop();
        }
    }
}
