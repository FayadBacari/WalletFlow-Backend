using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WalletFlow.Tests.Fakes;

// Garde en mémoire tous les messages du journal technique, pour qu'un test puisse les relire.
public class LoggerProviderThatKeepsTheMessages : ILoggerProvider
{
    public ConcurrentQueue<string> MessagesWrittenInTheLog { get; } = new ConcurrentQueue<string>();

    // Crée le journal d'une partie de l'application (nom imposé par le framework).
    public ILogger CreateLogger(string nameOfTheApplicationPartThatLogs)
    {
        return new LoggerThatKeepsTheMessages(MessagesWrittenInTheLog);
    }

    // Rien à libérer : les messages restent en mémoire pendant le test (nom imposé par le framework).
    public void Dispose()
    {
    }

    // Journal qui ajoute chaque message à la liste commune, avec le texte de son erreur.
    private class LoggerThatKeepsTheMessages : ILogger
    {
        private readonly ConcurrentQueue<string> messagesWrittenInTheTechnicalLog;

        // Reçoit la liste commune des messages.
        public LoggerThatKeepsTheMessages(ConcurrentQueue<string> messagesWrittenInTheTechnicalLog)
        {
            this.messagesWrittenInTheTechnicalLog = messagesWrittenInTheTechnicalLog;
        }

        // Aucune portée à suivre dans les tests (nom imposé par le framework).
        public IDisposable? BeginScope<TState>(TState stateOfTheScopeBeingStartedNow) where TState : notnull
        {
            return null;
        }

        // Tous les niveaux sont gardés : le réglage de chaque test décide des messages reçus (nom imposé par le framework).
        public bool IsEnabled(LogLevel levelOfTheMessageBeingCheckedNow)
        {
            return true;
        }

        // Ajoute le message à la liste (nom imposé par le framework).
        public void Log<TState>(
            LogLevel levelOfTheMessageBeingWrittenNow,
            EventId identifierOfTheMessageBeingWrittenNow,
            TState stateOfTheMessageBeingWrittenNow,
            Exception? errorAttachedToTheMessageBeingWritten,
            Func<TState, Exception?, string> functionThatFormatsTheMessageBeingWritten)
        {
            messagesWrittenInTheTechnicalLog.Enqueue(
                levelOfTheMessageBeingWrittenNow + " : "
                + functionThatFormatsTheMessageBeingWritten(stateOfTheMessageBeingWrittenNow, errorAttachedToTheMessageBeingWritten) + " "
                + errorAttachedToTheMessageBeingWritten);
        }
    }
}
