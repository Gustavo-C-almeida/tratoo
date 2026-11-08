using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tratoo.Tests
{
    /// <summary>
    /// Provedor de log que guarda cada linha formatada (mais a mensagem da exceção anexada),
    /// para os testes conferirem o que vai — e o que NÃO vai — para o log.
    /// </summary>
    public sealed class CapturaDeLog : ILoggerProvider
    {
        public ConcurrentQueue<(string Categoria, LogLevel Nivel, string Texto)> Linhas { get; } = new();

        public ILogger CreateLogger(string categoria) => new Logger(this, categoria);
        public ILogger<T> Para<T>() => new LoggerTipado<T>(CreateLogger(typeof(T).FullName!));
        public void Dispose() { }

        /// <summary>Todo o texto registrado, para buscar vazamentos.</summary>
        public string Tudo => string.Join("\n", Linhas.Select(l => l.Texto));

        private sealed class Logger : ILogger
        {
            private readonly CapturaDeLog _dono;
            private readonly string _categoria;
            public Logger(CapturaDeLog dono, string categoria) { _dono = dono; _categoria = categoria; }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex, Func<TState, Exception?, string> formatar)
            {
                var texto = formatar(estado, ex);
                for (var e = ex; e is not null; e = e.InnerException)
                    texto += " | " + e.Message;
                _dono.Linhas.Enqueue((_categoria, nivel, texto));
            }
        }

        private sealed class LoggerTipado<T> : ILogger<T>
        {
            private readonly ILogger _interno;
            public LoggerTipado(ILogger interno) => _interno = interno;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _interno.BeginScope(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex, Func<TState, Exception?, string> formatar)
                => _interno.Log(nivel, id, estado, ex, formatar);
        }
    }
}
