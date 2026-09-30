using System;
using System.IO;
using NAudio.Wave;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 听力播放内核（v2.27.0，规划 2.9）—— 对 NAudio 的一层薄封装。
///
/// 为什么用 NAudio 而不是系统默认播放器（原来「播放音频」动作的做法）：
///   ① 系统播放器（UWP/Media Player）**播完不会告诉我们**，只能靠老师手动关；
///   ② 我们需要"播完 → 延时自动关窗"，还据此决定**要不要推进听力进度**（听一半关掉 ≠ 听过）；
///   ③ 需要读取进度做进度条与"下一首"。
///
/// 格式选择（⚠ 与 PITFALLS §G 的选型结论一致）：
///   · mp3 / wav / aiff → `AudioFileReader`（NAudio 自带解码，不依赖系统编解码器）
///   · m4a / wma / aac / mp4 → `MediaFoundationReader`（走系统 Media Foundation；
///     ⚠ Windows N 版若没装 Media Feature Pack 会失败 —— 调用方需给友好提示）
///
/// 线程约定：**全部在 UI 线程调用**。`Finished` 事件由 NAudio 的播放线程抛出，
/// 订阅方需自行 `Dispatcher.UIThread.Post` 回 UI 线程（本类刻意不偷偷封送，便于自检同步断言）。
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private IWavePlayer? _output;
    private WaveStream? _reader;
    private bool _stoppingByUs;      // 区分"我们主动停"与"自然播完"
    private bool _disposed;

    /// <summary>整份**自然播完**（我们主动 Stop/换曲 时不触发 —— 这正是"听一半不算听过"的依据）</summary>
    public event Action? Finished;

    /// <summary>当前已载入的文件（未载入为空串）</summary>
    public string CurrentFile { get; private set; } = "";

    public bool HasTrack => _reader != null;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Total => _reader?.TotalTime ?? TimeSpan.Zero;

    /// <summary>音量 0.0~1.0</summary>
    public float Volume
    {
        get => _output?.Volume ?? 1f;
        set { if (_output != null) _output.Volume = Math.Clamp(value, 0f, 1f); }
    }

    /// <summary>载入一份音频（同一时刻只有一份）。失败抛异常，调用方负责提示老师。</summary>
    public void Load(string path)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径为空", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("音频文件不存在", path);

        ReleaseCurrent();
        _reader = OpenReader(path);
        _output = new WaveOutEvent { DesiredLatency = 300, NumberOfBuffers = 3 };
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(_reader);
        CurrentFile = path;
    }

    /// <summary>开始/继续播放（未载入则什么都不做）</summary>
    public void Play()
    {
        if (_disposed || _output == null) return;
        _stoppingByUs = false;
        _output.Play();
    }

    public void Pause()
    {
        if (_disposed || _output == null) return;
        if (_output.PlaybackState == PlaybackState.Playing) _output.Pause();
    }

    /// <summary>主动停止（**不触发 Finished** —— 老师中途关掉不算听过）</summary>
    public void Stop()
    {
        if (_disposed || _output == null) return;
        _stoppingByUs = true;
        try
        {
            if (_output.PlaybackState != PlaybackState.Stopped) _output.Stop();
        }
        catch { }
    }

    /// <summary>停止并回到文件开头（"重听这份"用）</summary>
    public void Restart()
    {
        if (_reader == null) return;
        Stop();
        try { _reader.CurrentTime = TimeSpan.Zero; } catch { }
    }

    /// <summary>跳转到指定位置（进度条拖动用；越界会被夹到有效区间）</summary>
    public void Seek(TimeSpan position)
    {
        if (_reader == null) return;
        try
        {
            var total = _reader.TotalTime;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > total) position = total;
            _reader.CurrentTime = position;
        }
        catch { }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_stoppingByUs) { _stoppingByUs = false; return; }
        // 有异常（设备被拔、解码中断）时不当作"播完" —— 免得把没听完的当成听过、指针被推进
        if (e.Exception != null)
        {
            AppLogger.Warn($"听力播放中断：{e.Exception.Message}");
            return;
        }
        try { Finished?.Invoke(); } catch (Exception ex) { AppLogger.Error("听力播完回调异常", ex); }
    }

    /// <summary>按扩展名选解码器</summary>
    private static WaveStream OpenReader(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".m4a" or ".wma" or ".aac" or ".mp4" => new MediaFoundationReader(path),
            _ => new AudioFileReader(path),
        };
    }

    private void ReleaseCurrent()
    {
        if (_output != null)
        {
            try { _output.PlaybackStopped -= OnPlaybackStopped; } catch { }
            try { _output.Stop(); } catch { }
            try { _output.Dispose(); } catch { }
            _output = null;
        }
        if (_reader != null)
        {
            try { _reader.Dispose(); } catch { }
            _reader = null;
        }
        CurrentFile = "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseCurrent();
    }

    /// <summary>时间显示（mm:ss；超过一小时给 h:mm:ss）</summary>
    public static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
