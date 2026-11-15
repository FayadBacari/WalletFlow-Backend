using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : un visiteur demande à retrouver son mot de passe (GR Auth, SS §2 et §6).
// Ce service ne lit jamais la base : il y écrit seulement la demande, la même pour toutes les adresses.
// Sa réponse et son temps de réponse ne disent donc pas si un compte utilise l'adresse.
public class PasswordRecoveryRequestService
{
    private readonly IPasswordRecoveryRequestLimiter requestLimiter;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit la limite par adresse e-mail, la file des e-mails et l'enregistrement avec réessai.
    public PasswordRecoveryRequestService(
        IPasswordRecoveryRequestLimiter requestLimiter,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.requestLimiter = requestLimiter;
        this.emailRequests = emailRequests;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Vérifie l'adresse et la limite, puis confie la suite à la file des e-mails.
    // La limite compte les demandes, qu'un compte existe ou non : elle ne révèle donc rien.
    public async Task<PasswordRecoveryRequestResult> RequestAsync(string typedEmail, IPAddress? ipAddress)
    {
        var email = EmailAddressRules.Normalize(typedEmail);

        if (!EmailAddressRules.IsValid(email))
        {
            return new PasswordRecoveryRequestResult.InvalidEmailAddressFormat();
        }

        if (!requestLimiter.TryCountRequest(email))
        {
            return new PasswordRecoveryRequestResult.TooManyRequests();
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // Une demande seule ne touche aucune ligne existante : aucune autre requête ne peut empêcher son enregistrement.
        await savingService.SaveWithRetriesAsync(() => PrepareRequestAsync(email, ipAddress, now), false);

        return new PasswordRecoveryRequestResult.RequestAccepted();
    }

    // Un passage : prépare l'entrée de la demande dans la file.
    private Task<PreparedChanges<bool>> PrepareRequestAsync(string email, IPAddress? ipAddress, DateTime now)
    {
        emailRequests.PrepareRequest(EmailRequest.ForPasswordRecovery(email, ipAddress, now));

        return Task.FromResult(PreparedChanges<bool>.SaveThenReturn(true));
    }
}
