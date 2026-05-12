using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;

namespace BFGDL.NET.Services;

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message);

public sealed class ObservableLogProvider : ILoggerProvider
{
    private readonly Subject<LogEntry> _subject = new();
    public IObservable<LogEntry> Entries => _subject;

    public ILogger CreateLogger(string categoryName) => new ObservableLogger(categoryName, _subject);

    public void Dispose() => _subject.OnCompleted();

    private sealed class ObservableLogger(string category, Subject<LogEntry> subject) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            if (exception is not null) message += $"\n{exception}";
            subject.OnNext(new LogEntry(DateTimeOffset.Now, logLevel, category, message));
        }
    }
}
