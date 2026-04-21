using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Locora.Infrastructure.Diagnostics;

public sealed class JsonLineFileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _syncRoot = new();

    public JsonLineFileLoggerProvider(string path)
    {
        _path = path;

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName) => new JsonLineFileLogger(categoryName, _path, _syncRoot);

    public void Dispose()
    {
    }

    private sealed class JsonLineFileLogger : ILogger
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _categoryName;
        private readonly string _path;
        private readonly object _syncRoot;

        public JsonLineFileLogger(string categoryName, string path, object syncRoot)
        {
            _categoryName = categoryName;
            _path = path;
            _syncRoot = syncRoot;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

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

            var payload = new
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = logLevel.ToString(),
                Category = _categoryName,
                EventId = eventId.Id,
                Message = formatter(state, exception),
                Exception = exception?.ToString()
            };

            var line = JsonSerializer.Serialize(payload, SerializerOptions);

            lock (_syncRoot)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
