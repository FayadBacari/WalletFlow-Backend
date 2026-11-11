using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WalletFlow.Tests.Fakes;

// Garde en mémoire tous les messages du journal technique, pour qu'un test puisse les relire.
public class MemoryLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new ConcurrentQueue<string>();

    // Crée le journal d'une partie de l'application (nom imposé par le framework).
    public ILogger CreateLogger(string categoryName)
    {
        return new MemoryLogger(Messages);
    }

    // Rien à libérer : les messages restent en mémoire pendant le test (nom imposé par le framework).
    public void Dispose()
    {
    }

    // Journal qui ajoute chaque message à la liste commune, avec le texte de son erreur.
    private class MemoryLogger : ILogger
    {
        private readonly ConcurrentQueue<string> messages;

        // Reçoit la liste commune des messages.
        public MemoryLogger(ConcurrentQueue<string> messages)
        {
            this.messages = messages;
        }

        // Aucune portée à suivre dans les tests (nom imposé par le framework).
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        // Tous les niveaux sont gardés : le réglage de chaque test décide des messages reçus (nom imposé par le framework).
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        // Ajoute le message à la liste (nom imposé par le framework).
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(
                logLevel + " : "
                + formatter(state, exception) + " "
                + exception);
        }
    }
}
