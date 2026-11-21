using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.Diagnostics;

/// <summary>
/// Simple rolling file logger under LocalAppData. No telemetry; local diagnostics only.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _writeLock = new();
    private readonly string _filePath;
    private bool _disposed;

    public FileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Information)
    {
        _directory = directory;
        _minLevel = minLevel;
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, $"explorerpilot-{DateTime.UtcNow:yyyyMMdd}.log");
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    internal bool IsEnabled(LogLevel level) => !_disposed && level >= _minLevel;

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        if (_disposed || level < _minLevel)
        {
            return;
        }

        var line = $"{DateTime.UtcNow:O} [{level}] {category}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(_filePath, line + Environment.NewLine);
                TryTrimFile();
            }
            catch
            {
                // Logging must never throw into the app.
            }
        }
    }

    private void TryTrimFile()
    {
        try
        {
            var info = new FileInfo(_filePath);
            if (!info.Exists || info.Length < 2_000_000)
            {
                return;
            }

            // Keep the last ~1 MB when the log grows large.
            using var stream = File.Open(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (stream.Length < 2_000_000)
            {
                return;
            }

            var keepFrom = stream.Length - 1_000_000;
            stream.Position = keepFrom;
            using var reader = new StreamReader(stream, leaveOpen: true);
            reader.ReadLine(); // align to next line
            var remainder = reader.ReadToEnd();
            stream.SetLength(0);
            stream.Position = 0;
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(remainder);
            writer.Flush();
        }
        catch
        {
            // ignore trim failures
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _loggers.Clear();
    }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
