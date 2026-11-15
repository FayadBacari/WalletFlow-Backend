using System.Globalization;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Emails;

// Texte en français des quatre alertes qu'on peut désactiver (GR Notif). Aucune ne cite de montant ni ne contient de lien :
// l'utilisateur ouvre lui-même WalletFlow (SS §9). Les noms de catégorie, de sac et de banque sont écrits dans le texte,
// jamais dans l'objet. Les dates sont celles de Paris, en toutes lettres.
public class FrenchAlertEmails : IAlertEmails
{
    private const string SettingsReminder = "Tu peux couper cette alerte dans les réglages de WalletFlow.";

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // Crée l'e-mail de cette alerte.
    public EmailMessage CreateEmail(string recipient, AlertEmailContent content)
    {
        return content switch
        {
            AlertEmailContent.BudgetReached budget => CreateBudgetEmail(recipient, budget),
            AlertEmailContent.NewSuggestions suggestions => CreateSuggestionsEmail(recipient, suggestions),
            AlertEmailContent.BagGoalReached bag => CreateBagEmail(recipient, bag),
            AlertEmailContent.BankAuthorizationToRenew authorization => CreateAuthorizationEmail(recipient, authorization),
            _ => throw new InvalidOperationException("Cette alerte n'a pas de texte.")
        };
    }

    // Les dépenses du mois dans une catégorie atteignent 80 %, ou tout son budget.
    private static EmailMessage CreateBudgetEmail(string recipient, AlertEmailContent.BudgetReached budget)
    {
        var isFullBudget = budget.Threshold == AlertRules.FullBudgetThreshold;
        var reached = isFullBudget ? "son budget du mois" : $"{budget.Threshold} % de son budget du mois";
        var body = $"""
            Bonjour,

            En {WriteMonth(budget.Month)}, tes dépenses dans la catégorie « {budget.CategoryName} » ont atteint {reached}.

            Ouvre WalletFlow pour voir où tu en es.

            {SettingsReminder}
            """;
        var subject = isFullBudget ? "Alerte de budget : budget atteint" : $"Alerte de budget : {budget.Threshold} % atteints";

        return new EmailMessage(recipient, subject, body);
    }

    // Le calcul du mois propose de nouvelles suggestions d'épargne.
    private static EmailMessage CreateSuggestionsEmail(string recipient, AlertEmailContent.NewSuggestions suggestions)
    {
        var body = $"""
            Bonjour,

            WalletFlow a étudié tes opérations {WriteMonthAfterOf(suggestions.Month)} et te propose de nouvelles suggestions d'épargne.

            Ouvre WalletFlow pour les voir, les accepter ou les ignorer.

            {SettingsReminder}
            """;

        return new EmailMessage(recipient, "De nouvelles suggestions d'épargne t'attendent", body);
    }

    // Un sac atteint son objectif.
    private static EmailMessage CreateBagEmail(string recipient, AlertEmailContent.BagGoalReached bag)
    {
        var body = $"""
            Bonjour,

            Ton sac « {bag.BagName} » a atteint son objectif. Bravo !

            Tu peux lui donner un nouvel objectif dans WalletFlow.

            {SettingsReminder}
            """;

        return new EmailMessage(recipient, "Un de tes sacs a atteint son objectif", body);
    }

    // L'autorisation donnée à une banque est à renouveler. Aucun lien : un e-mail qui propose de reconnecter sa banque
    // en un clic est exactement ce qu'imiterait un hameçonnage (SS §9).
    private static EmailMessage CreateAuthorizationEmail(string recipient, AlertEmailContent.BankAuthorizationToRenew authorization)
    {
        var endDay = DateOnly.FromDateTime(UsersCalendar.ConvertToUsersTime(authorization.AuthorizationExpiresAt));
        var body = $"""
            Bonjour,

            L'autorisation qui permet à WalletFlow de lire tes comptes chez « {authorization.BankName} » est à renouveler. Sa date de fin est le {WriteDay(endDay)}.

            Pour continuer à voir tes soldes et tes opérations, ouvre WalletFlow et renouvelle-la. Cet e-mail ne contient aucun lien : ne renouvelle jamais ton autorisation depuis un lien reçu par e-mail.

            {SettingsReminder}
            """;

        return new EmailMessage(recipient, "Ton autorisation bancaire est à renouveler", body);
    }

    // Écrit le mois : « novembre 2026 ».
    private static string WriteMonth(DateOnly month)
    {
        return $"{French.DateTimeFormat.GetMonthName(month.Month)} {month.Year}";
    }

    // Écrit le mois après « de » : « de novembre 2026 », mais « d'avril », « d'août » et « d'octobre ».
    private static string WriteMonthAfterOf(DateOnly month)
    {
        var monthName = French.DateTimeFormat.GetMonthName(month.Month);
        var preposition = "aeiou".Contains(monthName[0]) ? "d'" : "de ";

        return $"{preposition}{WriteMonth(month)}";
    }

    // Écrit le jour : « 1er novembre 2026 », « 22 novembre 2026 ».
    private static string WriteDay(DateOnly day)
    {
        var dayNumber = day.Day == 1 ? "1er" : day.Day.ToString(French);

        return $"{dayNumber} {French.DateTimeFormat.GetMonthName(day.Month)} {day.Year}";
    }
}
