using System;

namespace ResBinManager.Core
{
    public class WavPlayer : IDisposable
    {
#if !NET40
        private NAudio.Wave.WaveOutEvent? _waveOut;
        private NAudio.Wave.WaveFileReader? _reader;
        private System.IO.MemoryStream? _stream;
#endif
        private bool _disposed;
        private float _volume = 1.0f;

        public event EventHandler? PlaybackStateChanged;

        public bool IsPlaying
        {
            get
            {
#if !NET40
                return _waveOut?.PlaybackState == NAudio.Wave.PlaybackState.Playing;
#else
                return false;
#endif
            }
        }

        public bool IsPaused
        {
            get
            {
#if !NET40
                return _waveOut?.PlaybackState == NAudio.Wave.PlaybackState.Paused;
#else
                return false;
#endif
            }
        }

        public TimeSpan Position
        {
            get
            {
#if !NET40
                return _reader?.CurrentTime ?? TimeSpan.Zero;
#else
                return TimeSpan.Zero;
#endif
            }
        }

        public TimeSpan Duration
        {
            get
            {
#if !NET40
                return _reader?.TotalTime ?? TimeSpan.Zero;
#else
                return TimeSpan.Zero;
#endif
            }
        }

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0f, Math.Min(value, 1f));
#if !NET40
                if (_waveOut != null)
                {
                    _waveOut.Volume = _volume;
                }
#endif
            }
        }

        public void Load(byte[] wavData)
        {
            Stop();
            DisposeResources();

#if !NET40
            try
            {
                _stream = new System.IO.MemoryStream(wavData);
                _reader = new NAudio.Wave.WaveFileReader(_stream);
                _waveOut = new NAudio.Wave.WaveOutEvent();
                _waveOut.Volume = _volume;
                _waveOut.Init(_reader);
                _waveOut.PlaybackStopped += OnPlaybackStopped;

                RaisePlaybackStateChanged();
            }
            catch (Exception ex)
            {
                DisposeResources();
                throw new InvalidOperationException($"Failed to load WAV data: {ex.Message}", ex);
            }
#endif
        }

        public void Play()
        {
#if !NET40
            if (_waveOut == null || _reader == null)
                throw new InvalidOperationException("No WAV data loaded");

            if (_waveOut.PlaybackState == NAudio.Wave.PlaybackState.Paused)
            {
                _waveOut.Play();
            }
            else
            {
                _reader.Position = 0;
                _waveOut.Play();
            }

            RaisePlaybackStateChanged();
#endif
        }

        public void Pause()
        {
#if !NET40
            if (_waveOut?.PlaybackState == NAudio.Wave.PlaybackState.Playing)
            {
                _waveOut.Pause();
                RaisePlaybackStateChanged();
            }
#endif
        }

        public void Stop()
        {
#if !NET40
            if (_waveOut != null)
            {
                _waveOut.Stop();
                RaisePlaybackStateChanged();
            }
#endif
        }

        public void Seek(TimeSpan position)
        {
#if !NET40
            if (_reader == null)
                throw new InvalidOperationException("No WAV data loaded");

            position = TimeSpan.FromSeconds(
                Math.Max(0, Math.Min(position.TotalSeconds, Duration.TotalSeconds)));

            _reader.CurrentTime = position;
#endif
        }

#if !NET40
        private void OnPlaybackStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
        {
            RaisePlaybackStateChanged();
        }
#endif

        private void RaisePlaybackStateChanged()
        {
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DisposeResources()
        {
#if !NET40
            _waveOut?.Dispose();
            _waveOut = null;

            _reader?.Dispose();
            _reader = null;

            _stream?.Dispose();
            _stream = null;
#endif
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                DisposeResources();
                _disposed = true;
            }
        }

        ~WavPlayer()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Dispose();
            }
        }
    }
}
