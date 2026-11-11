using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : un visiteur demande à retrouver son mot de passe (GR Auth, SS §2 et §6).
// Ce service ne lit jamais la base : sa réponse et son temps de réponse sont donc les mêmes pour toutes les adresses.
public class PasswordRecoveryRequestService
{
    private readonly IPasswordRecoveryRequestLimiter requestLimiter;
    private readonly IBackgroundEmailRequestQueue emailQueue;

    // Reçoit la limite par adresse e-mail et la file des e-mails.
    public PasswordRecoveryRequestService(
        IPasswordRecoveryRequestLimiter requestLimiter,
        IBackgroundEmailRequestQueue emailQueue)
    {
        this.requestLimiter = requestLimiter;
        this.emailQueue = emailQueue;
    }

    // Vérifie l'adresse et la limite, puis confie la suite à la file des e-mails.
    // La limite compte les demandes, qu'un compte existe ou non : elle ne révèle donc rien.
    public PasswordRecoveryRequestResult Request(string typedEmail, IPAddress? ipAddress)
    {
        var email = EmailAddressRules.Normalize(typedEmail);

        if (!EmailAddressRules.IsValid(email))
        {
            return PasswordRecoveryRequestResult.InvalidEmailAddressFormat;
        }

        if (!requestLimiter.TryCountRequest(email))
        {
            return PasswordRecoveryRequestResult.TooManyRequests;
        }

        emailQueue.Add(new BackgroundEmailRequest.PasswordRecovery
        {
            EmailAddress = email,
            IpAddress = ipAddress
        });

        return PasswordRecoveryRequestResult.RequestAccepted;
    }
}
