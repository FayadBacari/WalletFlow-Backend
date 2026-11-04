namespace WalletFlow.Domain.UsersAndSecurity;

// Règles d'une adresse e-mail : format valide, 254 caractères au plus, enregistrée en minuscules (DD §3, SS §5).
public static class EmailAddressRules
{
    public const int MaximumNumberOfCharactersInEmailAddress = 254;
    public const int MaximumNumberOfCharactersBeforeTheAtSign = 64;
    public const int MaximumNumberOfCharactersInOneDomainPart = 63;
    public const int MinimumNumberOfCharactersInLastDomainPart = 2;

    // Avant le @ : des lettres et des chiffres sans accent, plus ces cinq signes. Tout le reste est refusé,
    // notamment les guillemets et les caractères invisibles (DD §3, SS §5).
    // Les accents sont refusés car .NET et PostgreSQL ne les mettent pas tous en minuscules de la même façon,
    // et la base vérifie cette règle.
    private const string SignsAllowedBeforeTheAtSign = "._+-'";

    // Donne la forme enregistrée en base : sans espaces autour, en minuscules.
    public static string ConvertEmailAddressIntoItsStoredForm(string emailAddressTypedByTheVisitor)
    {
        return emailAddressTypedByTheVisitor.Trim().ToLowerInvariant();
    }

    // Renvoie vrai si une adresse, déjà mise sous sa forme enregistrée, a un format valide.
    // Les règles sont écrites ici une par une : MailAddress de .NET accepte des formes dangereuses,
    // comme un texte libre entre guillemets ("<script>"@exemple.fr).
    public static bool CheckThatEmailAddressHasValidFormat(string emailAddressInItsStoredForm)
    {
        if (emailAddressInItsStoredForm.Length > MaximumNumberOfCharactersInEmailAddress)
        {
            return false;
        }

        var partsOfTheAddressAroundTheAtSign = emailAddressInItsStoredForm.Split('@');
        if (partsOfTheAddressAroundTheAtSign.Length != 2)
        {
            return false;
        }

        return CheckThatTextBeforeTheAtSignIsValid(partsOfTheAddressAroundTheAtSign[0])
            && CheckThatDomainAfterTheAtSignIsValid(partsOfTheAddressAroundTheAtSign[1]);
    }

    // Vérifie le texte avant le @ : 1 à 64 caractères autorisés, sans point au début, à la fin ni en double.
    private static bool CheckThatTextBeforeTheAtSignIsValid(string textBeforeTheAtSignToCheck)
    {
        if (textBeforeTheAtSignToCheck.Length == 0 || textBeforeTheAtSignToCheck.Length > MaximumNumberOfCharactersBeforeTheAtSign)
        {
            return false;
        }

        if (textBeforeTheAtSignToCheck.StartsWith('.') || textBeforeTheAtSignToCheck.EndsWith('.') || textBeforeTheAtSignToCheck.Contains(".."))
        {
            return false;
        }

        foreach (var characterBeforeTheAtSignToCheck in textBeforeTheAtSignToCheck)
        {
            if (!char.IsAsciiLetterOrDigit(characterBeforeTheAtSignToCheck) && !SignsAllowedBeforeTheAtSign.Contains(characterBeforeTheAtSignToCheck))
            {
                return false;
            }
        }

        return true;
    }

    // Vérifie le domaine, comme exemple.fr : au moins deux parties séparées par un point, toutes valides.
    private static bool CheckThatDomainAfterTheAtSignIsValid(string domainAfterTheAtSignToCheck)
    {
        var partsOfTheDomainSeparatedByDots = domainAfterTheAtSignToCheck.Split('.');
        if (partsOfTheDomainSeparatedByDots.Length < 2)
        {
            return false;
        }

        foreach (var onePartOfTheDomainToCheck in partsOfTheDomainSeparatedByDots)
        {
            if (!CheckThatOnePartOfTheDomainIsValid(onePartOfTheDomainToCheck))
            {
                return false;
            }
        }

        // La dernière partie (fr, com...) a au moins 2 caractères et commence par une lettre :
        // a@b.c ne peut recevoir aucun e-mail, et 127.0.0.1 n'est pas un domaine.
        var lastPartOfTheDomainToCheck = partsOfTheDomainSeparatedByDots[partsOfTheDomainSeparatedByDots.Length - 1];
        return lastPartOfTheDomainToCheck.Length >= MinimumNumberOfCharactersInLastDomainPart
            && char.IsAsciiLetter(lastPartOfTheDomainToCheck[0]);
    }

    // Vérifie une partie du domaine : 1 à 63 lettres, chiffres ou tirets, sans tiret au début ni à la fin.
    private static bool CheckThatOnePartOfTheDomainIsValid(string onePartOfTheDomainToCheck)
    {
        if (onePartOfTheDomainToCheck.Length == 0 || onePartOfTheDomainToCheck.Length > MaximumNumberOfCharactersInOneDomainPart)
        {
            return false;
        }

        if (onePartOfTheDomainToCheck.StartsWith('-') || onePartOfTheDomainToCheck.EndsWith('-'))
        {
            return false;
        }

        foreach (var characterOfTheDomainPartToCheck in onePartOfTheDomainToCheck)
        {
            if (!char.IsAsciiLetterOrDigit(characterOfTheDomainPartToCheck) && characterOfTheDomainPartToCheck != '-')
            {
                return false;
            }
        }

        return true;
    }
}
