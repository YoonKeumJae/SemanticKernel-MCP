using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using DotNetEnv;
using System.Threading;
using System.Timers;
using Jarvis.SemanticKernel;
using Jarvis.TTS; // TTS 네임스페이스 추가

namespace Jarvis.Waker;
public class Waker
{
    // 음성 명령이 감지되었을 때 발생하는 이벤트
    public event EventHandler<string>? CommandDetected;

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listeningTask;
    private KernelApp? _kernelApp;
    
    // 웨이크 워드 감지 상태를 추적하는 변수
    private bool _isWakeWordDetected = false;
    
    // 무음 감지 타이머
    private System.Timers.Timer? _silenceTimer;
    
    // 현재 인식된 명령을 저장
    private string? _lastCommand;
    
    // TTS 재생 중인지 추적하는 변수
    private bool _isSpeaking = false;
    
    // SpeechRecognizer 인스턴스 (클래스 전체에서 접근)
    private SpeechRecognizer? _speechRecognizer;
    
    // TTS 음성 재생 후 인식 재개를 위한 지연 타이머 (음성 에코 방지)
    private System.Timers.Timer? _recognitionResumeTimer;
    
    // 음성 인식 처리 활성화 상태 추적
    private bool _processingEnabled = true;

    // 작동 모드 정의
    private enum Mode
    {
        Waiting,     // 대기 모드 (웨이크워드 기다림)
        Listening,   // 인식 모드 (명령어 인식)
        Processing   // 프로세싱 모드 (명령어 처리)
    }
    
    // 현재 모드
    private Mode _currentMode = Mode.Waiting;

    public Waker()
    {
        // KernelApp 인스턴스 생성
        _kernelApp = new KernelApp();
        
        // TTS 이벤트 구독
        TTS.TTS.SpeakCompleted += OnTTSSpeakCompleted;
    }

    // TTS 재생 완료 이벤트 처리
    private void OnTTSSpeakCompleted(object? sender, EventArgs e)
    {
        _isSpeaking = false;
        
        // 인식 모드로 돌아갈 준비가 됨
        if (_currentMode == Mode.Processing)
        {
            Console.WriteLine("TTS 재생이 완료되었습니다.");
            
            // TTS 재생 완료 후 약간의 지연 시간을 두고 음성 인식 재개
            // (에코 방지를 위한 지연)
            _recognitionResumeTimer = new System.Timers.Timer(1000); // 1초 지연
            _recognitionResumeTimer.Elapsed += OnRecognitionResumeTimerElapsed;
            _recognitionResumeTimer.AutoReset = false;
            _recognitionResumeTimer.Start();
            
            Console.WriteLine("음성 인식 처리 재개를 위한 1초 대기 중...");
        }
    }
    
    // 음성 인식 재개 타이머 완료 이벤트 처리
    private void OnRecognitionResumeTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // 음성 인식 처리 활성화
        _processingEnabled = true;
        
        // 처리 완료 후 바로 대기 모드로 설정 (인식 모드가 아닌)
        _currentMode = Mode.Waiting;
        Console.WriteLine("TTS 응답 완료: 대기 모드로 돌아갑니다.");
        Console.WriteLine("대기 모드: 'Hey, Jarvis' 또는 'Jarvis'를 말하세요...");
        
        // 타이머 정리
        if (_recognitionResumeTimer != null)
        {
            _recognitionResumeTimer.Dispose();
            _recognitionResumeTimer = null;
        }
    }
    
    // 음성 인식 처리 일시 중지 (세션은 유지하고 처리만 비활성화)
    private void PauseRecognitionProcessing()
    {
        if (_processingEnabled)
        {
            Console.WriteLine("음성 인식 처리 일시 중지...");
            _processingEnabled = false;
            Console.WriteLine("음성 인식 처리가 일시 중지되었습니다.");
        }
    }
    
    // 음성 인식 처리 재개
    private void ResumeRecognitionProcessing()
    {
        if (!_processingEnabled)
        {
            Console.WriteLine("음성 인식 처리 재개...");
            _processingEnabled = true;
            Console.WriteLine("음성 인식 처리가 재개되었습니다.");
        }
    }

    public async Task Start()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _listeningTask = Listen(_cancellationTokenSource.Token);
        
        // 비동기로 실행하고 반환
        await Task.CompletedTask;
    }

    public async Task Stop()
    {
        // TTS 이벤트 구독 해제
        TTS.TTS.SpeakCompleted -= OnTTSSpeakCompleted;
        
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
        
        // 타이머가 활성화되어 있다면 정리
        StopSilenceTimer();
        
        if (_recognitionResumeTimer != null)
        {
            _recognitionResumeTimer.Stop();
            _recognitionResumeTimer.Dispose();
            _recognitionResumeTimer = null;
        }
        
        // 음성 인식기 해제
        if (_speechRecognizer != null)
        {
            await _speechRecognizer.StopContinuousRecognitionAsync();
            _speechRecognizer.Dispose();
            _speechRecognizer = null;
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
            speechConfig.SpeechRecognitionLanguage = "en-US"; // 영어만 인식
            
            // 2) AudioConfig 인스턴스화
            var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            
            // 3) SpeechRecognizer 생성
            _speechRecognizer = new SpeechRecognizer(speechConfig, audioConfig);
            
            // 4) 디버깅을 위한 이벤트 등록
            _speechRecognizer.SessionStarted += (s, e) => {
                Console.WriteLine("세션 시작됨");
            };
            
            _speechRecognizer.SessionStopped += (s, e) => {
                Console.WriteLine("세션 종료됨");
            };
            
            // 5) 인식 결과 이벤트 등록
            _speechRecognizer.Recognized += async (s, e) =>
            {
                // 음성 인식 처리가 비활성화된 상태면 이벤트 무시
                if (!_processingEnabled)
                {
                    return;
                }
                
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    string recognizedText = e.Result.Text;
                    string lowerText = recognizedText.ToLower();
                    
                    switch(_currentMode)
                    {
                        case Mode.Waiting:
                            // 대기 모드: 웨이크 워드만 인식
                            if (lowerText.Contains("hey jarvis") || lowerText.Contains("하이 자비스") || 
                                lowerText.Contains("자비스") || lowerText.Contains("jarvis"))
                            {
                                Console.WriteLine("[웨이크 워드 감지됨]: " + recognizedText);
                                
                                // 인식 모드로 전환
                                _currentMode = Mode.Listening;
                                Console.WriteLine("인식 모드로 전환: 명령을 말씀해주세요...");
                                
                                // 무음 타이머 시작 (5초 후 대기 모드로 돌아감)
                                StartSilenceTimer();
                            }
                            break;
                            
                        case Mode.Listening:
                            // 인식 모드: 모든 음성을 명령으로 처리
                            Console.WriteLine($"[명령 인식]: {recognizedText}");
                            
                            // 명령 저장 및 이벤트 발생
                            _lastCommand = recognizedText;
                            CommandDetected?.Invoke(this, recognizedText);
                            
                            // 타이머 중지 (명령 처리 중에는 타이머 작동 안 함)
                            StopSilenceTimer();
                            
                            // 프로세싱 모드로 전환
                            _currentMode = Mode.Processing;
                            Console.WriteLine("프로세싱 모드로 전환: 명령 처리 중...");
                            
                            // 음성 인식 처리 일시 중지
                            PauseRecognitionProcessing();
                            
                            // 명령어를 SemanticKernel에 전달하여 처리
                            await ProcessCommandAsync(recognizedText);
                            break;
                        
                        case Mode.Processing:
                            // 프로세싱 모드에서는 음성 입력을 무시함
                            Console.WriteLine("[프로세싱 중] 음성 입력을 무시합니다.");
                            break;
                    }
                }
            };

            // 연결 오류 이벤트 등록
            _speechRecognizer.Canceled += (s, e) =>
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
            await _speechRecognizer.StartContinuousRecognitionAsync();
            _processingEnabled = true;
            
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
                if (_speechRecognizer != null)
                {
                    await _speechRecognizer.StopContinuousRecognitionAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"음성 인식 초기화 중 오류 발생: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
    
    // 명령어를 SemanticKernel에 전달하여 처리하는 메소드
    private async Task ProcessCommandAsync(string command)
    {
        try
        {
            if (_kernelApp != null)
            {
                Console.WriteLine($"명령어를 처리합니다: {command}");
                
                // TTS 재생 시작 표시
                _isSpeaking = true;
                
                // SemanticKernel의 StartProcessAsync 메소드 호출
                await _kernelApp.StartProcessAsync(command);
                
                Console.WriteLine("명령어 처리가 완료되었습니다.");
                
                // TTS 음성 재생이 진행 중인 경우, TTS.SpeakCompleted 이벤트에서 인식 모드로 전환
                if (_isSpeaking)
                {
                    Console.WriteLine("TTS 음성 재생이 진행 중입니다. 재생이 끝날 때까지 대기합니다.");
                    // Processing 모드 유지 (음성 재생 완료 이벤트에서 인식 모드로 전환)
                    return;
                }
            }
            else
            {
                Console.WriteLine("KernelApp이 초기화되지 않았습니다.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"명령어 처리 중 오류 발생: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            _isSpeaking = false; // 오류 발생 시 재생 중 상태 초기화
        }
        
        // TTS 재생 중이 아닌 경우에만 인식 모드로 돌아감
        if (!_isSpeaking)
        {
            // 처리 완료 후 인식 모드로 돌아감
            _currentMode = Mode.Listening;
            Console.WriteLine("인식 모드로 돌아갑니다: 다음 명령을 말씀해주세요...");
            
            // 다시 타이머 시작 (5초 후 대기 모드로 돌아감)
            StartSilenceTimer();
            
            // 음성 인식 처리 재개
            ResumeRecognitionProcessing();
        }
    }
    
    // 무음 감지 타이머 시작 메소드
    private void StartSilenceTimer()
    {
        // 기존 타이머가 있다면 중지하고 제거
        StopSilenceTimer();
        
        // 5초 타이머 설정 (3초에서 5초로 변경)
        _silenceTimer = new System.Timers.Timer(5000);
        _silenceTimer.Elapsed += OnSilenceDetected;
        _silenceTimer.AutoReset = false; // 한 번만 실행
        _silenceTimer.Start();
    }
    
    // 타이머 중지 메소드 (별도로 분리)
    private void StopSilenceTimer()
    {
        if (_silenceTimer != null)
        {
            _silenceTimer.Stop();
            _silenceTimer.Dispose();
            _silenceTimer = null;
        }
    }
    
    // 타이머 재설정 메소드
    private void ResetSilenceTimer()
    {
        if (_currentMode == Mode.Listening)
        {
            StopSilenceTimer();
            StartSilenceTimer();
        }
    }
    
    // 무음 감지 이벤트 처리
    private void OnSilenceDetected(object? sender, ElapsedEventArgs e)
    {
        if (_currentMode == Mode.Listening)
        {
            if (!string.IsNullOrEmpty(_lastCommand))
            {
                Console.WriteLine($"[완료된 명령]: {_lastCommand}");
                _lastCommand = null;
            }
            
            // 대기 모드로 돌아감
            _currentMode = Mode.Waiting;
            Console.WriteLine("[5초간 무음 감지] 명령 입력이 끝났습니다. 대기 모드로 돌아갑니다.");
            Console.WriteLine("대기 모드: 'Hey, Jarvis' 또는 'Jarvis'를 말하세요...");
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
