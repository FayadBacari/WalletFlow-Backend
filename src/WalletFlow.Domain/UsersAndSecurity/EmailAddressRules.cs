namespace WalletFlow.Domain.UsersAndSecurity;

// Règles d'une adresse e-mail : format valide, 254 caractères au plus, enregistrée en minuscules (DD §3, SS §5).
public static class EmailAddressRules
{
    public const int MaximumLength = 254;
    public const int MaximumLocalPartLength = 64;
    public const int MaximumDomainLabelLength = 63;
    public const int MinimumTopLevelDomainLength = 2;

    // Avant le @ : des lettres et des chiffres sans accent, plus ces cinq signes. Tout le reste est refusé,
    // notamment les guillemets et les caractères invisibles (DD §3, SS §5).
    // Les accents sont refusés car .NET et PostgreSQL ne les mettent pas tous en minuscules de la même façon,
    // et la base vérifie cette règle.
    private const string AllowedLocalPartSigns = "._+-'";

    // Donne la forme enregistrée en base : sans espaces autour, en minuscules.
    public static string Normalize(string typedEmail)
    {
        return typedEmail.Trim().ToLowerInvariant();
    }

    // Renvoie vrai si une adresse, déjà mise sous sa forme enregistrée, a un format valide.
    // Les règles sont écrites ici une par une : MailAddress de .NET accepte des formes dangereuses,
    // comme un texte libre entre guillemets ("<script>"@exemple.fr).
    public static bool IsValid(string email)
    {
        if (email.Length > MaximumLength)
        {
            return false;
        }

        var parts = email.Split('@');
        if (parts.Length != 2)
        {
            return false;
        }

        return IsLocalPartValid(parts[0])
            && IsDomainValid(parts[1]);
    }

    // Vérifie le texte avant le @ : 1 à 64 caractères autorisés, sans point au début, à la fin ni en double.
    private static bool IsLocalPartValid(string localPart)
    {
        if (localPart.Length == 0 || localPart.Length > MaximumLocalPartLength)
        {
            return false;
        }

        if (localPart.StartsWith('.') || localPart.EndsWith('.') || localPart.Contains(".."))
        {
            return false;
        }

        foreach (var character in localPart)
        {
            if (!char.IsAsciiLetterOrDigit(character) && !AllowedLocalPartSigns.Contains(character))
            {
                return false;
            }
        }

        return true;
    }

    // Vérifie le domaine, comme exemple.fr : au moins deux parties séparées par un point, toutes valides.
    private static bool IsDomainValid(string domain)
    {
        var labels = domain.Split('.');
        if (labels.Length < 2)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (!IsDomainLabelValid(label))
            {
                return false;
            }
        }

        // La dernière partie (fr, com...) a au moins 2 caractères et commence par une lettre :
        // a@b.c ne peut recevoir aucun e-mail, et 127.0.0.1 n'est pas un domaine.
        var topLevelDomain = labels[labels.Length - 1];
        return topLevelDomain.Length >= MinimumTopLevelDomainLength
            && char.IsAsciiLetter(topLevelDomain[0]);
    }

    // Vérifie une partie du domaine : 1 à 63 lettres, chiffres ou tirets, sans tiret au début ni à la fin.
    private static bool IsDomainLabelValid(string label)
    {
        if (label.Length == 0 || label.Length > MaximumDomainLabelLength)
        {
            return false;
        }

        if (label.StartsWith('-') || label.EndsWith('-'))
        {
            return false;
        }

        foreach (var character in label)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
