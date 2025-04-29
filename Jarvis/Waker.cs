using System;
using System.IO;
using System.Collections.Generic;
using NAudio.Wave;
using Pv;
using DotNetEnv;

namespace Jarvis.Waker
{
    public class Waker : IDisposable
    {
        private readonly Porcupine _porcupine;
        private readonly WaveInEvent _waveIn;
        private bool _isRecording;

        public Waker()
        {
            // 1) .env 로드 및 AccessKey 가져오기
            string envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            Env.Load(envPath);

            string key = Environment.GetEnvironmentVariable("PORCUPINE_API_KEY")
                         ?? throw new InvalidOperationException("PORCUPINE_API_KEY가 설정되지 않았습니다.");

            // 2) Porcupine 엔진 초기화
            // 사용자 정의 키워드 파일 (.ppn) 및 모델 파일 (.pv) 경로
            string keywordFile = Path.Combine(Directory.GetCurrentDirectory(), "resources", "jarvis.ppn");
            string modelFile = Path.Combine(Directory.GetCurrentDirectory(), "resources", "porcupine_params.pv");

           
            // 로드 실패 시 기본 내장 키워드 사용
            this._porcupine = Porcupine.FromBuiltInKeywords(
                accessKey: key,
                keywords: new List<BuiltInKeyword> { BuiltInKeyword.JARVIS },
                sensitivities: new List<float> { 0.6f }
            );
    

            // 3) 오디오 캡처 설정
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(_porcupine.SampleRate, 16, 1),
                BufferMilliseconds = (int)(_porcupine.FrameLength * 1000.0 / _porcupine.SampleRate)
            };
            _waveIn.DataAvailable += OnDataAvailable;
        }

        // DataAvailable 이벤트 핸들러
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            var pcm = new short[e.BytesRecorded / 2];
            Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);

            if (_porcupine.Process(pcm) >= 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔔 Wake word detected!");
            }
        }

        /// <summary>
        /// 녹음 및 키워드 감지 시작
        /// </summary>
        public void Start()
        {
            if (!_isRecording)
            {
                _waveIn.StartRecording();
                _isRecording = true;
                Console.WriteLine("Listening for wake word...");
            }
        }

        /// <summary>
        /// 녹음 중지
        /// </summary>
        public void Stop()
        {
            if (_isRecording)
            {
                _waveIn.StopRecording();
                _isRecording = false;
                Console.WriteLine("Stopped listening.");
            }
        }

        // IDisposable 구현
        public void Dispose()
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            if (_isRecording)
            {
                _waveIn.StopRecording();
                _isRecording = false;
            }
            _porcupine.Dispose();
            _waveIn.Dispose();
        }
    }
}
