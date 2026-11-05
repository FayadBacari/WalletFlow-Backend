using System.Net;

namespace WalletFlow.Application.AccountRecovery;

// Une demande d'e-mail à traiter en arrière-plan.
// Une classe plutôt qu'un record : un record afficherait l'adresse e-mail si on l'écrivait dans un journal.
public class BackgroundEmailRequest
{
    public required BackgroundEmailRequestType TypeOfTheRequestedBackgroundEmail { get; init; }

    // Adresse donnée par le visiteur, pour une récupération de mot de passe.
    public string? EmailAddressOfTheRecoveryRequest { get; init; }

    // Compte qu'un essai de connexion vient de bloquer, pour un déblocage.
    public Guid? IdentifierOfTheAccountJustLocked { get; init; }

    public IPAddress? IpAddressOfTheRequestingVisitor { get; init; }
}
