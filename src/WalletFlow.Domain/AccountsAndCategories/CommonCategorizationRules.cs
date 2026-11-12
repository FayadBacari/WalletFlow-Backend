namespace WalletFlow.Domain.AccountsAndCategories;

// Une règle commune : des mots du libellé, écrits sous forme simple (voir TransactionLabels),
// et le nom de la catégorie par défaut qu'ils donnent (GR Dépenses, DD §6.2).
public record CommonCategorizationRule(string Words, string CategoryName);

// Les règles communes fournies par l'application (GR Dépenses, DD §12) : les mêmes pour tous les utilisateurs,
// jamais enregistrées pour chacun. Une règle reconnaît ses mots entiers dans le libellé, sans tenir compte des majuscules,
// des accents, des chiffres ni de la ponctuation : « CARREFOUR » reconnaît « CB CARREFOUR MARKET 12/11 ».
// Elle vise une catégorie par son nom : si l'utilisateur l'a renommée ou supprimée, l'opération va dans « Autres » (DD §6.2).
// Une règle qui vise « Autres » est une exception : elle reconnaît les mots d'une règle plus courte, et passe avant elle.
public static class CommonCategorizationRules
{
    public static readonly IReadOnlyList<CommonCategorizationRule> All = new List<CommonCategorizationRule>
    {
        // Alimentation : supermarchés, commerces de bouche, restaurants et livraison de repas.
        new CommonCategorizationRule("CARREFOUR", "Alimentation"),
        new CommonCategorizationRule("LECLERC", "Alimentation"),
        new CommonCategorizationRule("AUCHAN", "Alimentation"),
        new CommonCategorizationRule("LIDL", "Alimentation"),
        new CommonCategorizationRule("ALDI", "Alimentation"),
        new CommonCategorizationRule("INTERMARCHE", "Alimentation"),
        new CommonCategorizationRule("MONOPRIX", "Alimentation"),
        new CommonCategorizationRule("FRANPRIX", "Alimentation"),
        new CommonCategorizationRule("SUPER U", "Alimentation"),
        new CommonCategorizationRule("HYPER U", "Alimentation"),
        new CommonCategorizationRule("U EXPRESS", "Alimentation"),
        new CommonCategorizationRule("NETTO", "Alimentation"),
        new CommonCategorizationRule("CORA", "Alimentation"),
        new CommonCategorizationRule("PICARD", "Alimentation"),
        new CommonCategorizationRule("GRAND FRAIS", "Alimentation"),
        new CommonCategorizationRule("BIOCOOP", "Alimentation"),
        new CommonCategorizationRule("NATURALIA", "Alimentation"),
        new CommonCategorizationRule("BOULANGERIE", "Alimentation"),
        new CommonCategorizationRule("BOUCHERIE", "Alimentation"),
        new CommonCategorizationRule("RESTAURANT", "Alimentation"),
        new CommonCategorizationRule("UBER EATS", "Alimentation"),
        new CommonCategorizationRule("DELIVEROO", "Alimentation"),
        new CommonCategorizationRule("JUST EAT", "Alimentation"),
        new CommonCategorizationRule("MCDONALDS", "Alimentation"),
        new CommonCategorizationRule("MCDONALD S", "Alimentation"),
        new CommonCategorizationRule("BURGER KING", "Alimentation"),
        new CommonCategorizationRule("KFC", "Alimentation"),
        new CommonCategorizationRule("STARBUCKS", "Alimentation"),

        // Transport : trains, transports en commun, voitures avec chauffeur, carburant, péages, avions.
        new CommonCategorizationRule("SNCF", "Transport"),
        new CommonCategorizationRule("OUIGO", "Transport"),
        new CommonCategorizationRule("RATP", "Transport"),
        new CommonCategorizationRule("NAVIGO", "Transport"),
        new CommonCategorizationRule("UBER", "Transport"),
        new CommonCategorizationRule("BOLT", "Transport"),
        new CommonCategorizationRule("BLABLACAR", "Transport"),
        new CommonCategorizationRule("TOTALENERGIES", "Transport"),
        new CommonCategorizationRule("ESSO", "Transport"),
        new CommonCategorizationRule("SHELL", "Transport"),
        new CommonCategorizationRule("AVIA", "Transport"),
        new CommonCategorizationRule("PEAGE", "Transport"),
        new CommonCategorizationRule("AUTOROUTE", "Transport"),
        new CommonCategorizationRule("AUTOROUTES", "Transport"),
        new CommonCategorizationRule("SANEF", "Transport"),
        new CommonCategorizationRule("APRR", "Transport"),
        new CommonCategorizationRule("PARKING", "Transport"),
        new CommonCategorizationRule("TAXI", "Transport"),
        new CommonCategorizationRule("VELIB", "Transport"),
        new CommonCategorizationRule("AIR FRANCE", "Transport"),
        new CommonCategorizationRule("EASYJET", "Transport"),
        new CommonCategorizationRule("RYANAIR", "Transport"),
        new CommonCategorizationRule("TRANSAVIA", "Transport"),

        // Logement : loyer, énergie, eau, téléphone et internet, impôts locaux, maison et bricolage.
        new CommonCategorizationRule("LOYER", "Logement"),
        new CommonCategorizationRule("EDF", "Logement"),
        new CommonCategorizationRule("ENGIE", "Logement"),
        new CommonCategorizationRule("VEOLIA", "Logement"),
        new CommonCategorizationRule("SUEZ", "Logement"),
        new CommonCategorizationRule("ORANGE SA", "Logement"),
        new CommonCategorizationRule("SOSH", "Logement"),
        new CommonCategorizationRule("SFR", "Logement"),
        new CommonCategorizationRule("BOUYGUES TELECOM", "Logement"),
        new CommonCategorizationRule("FREE MOBILE", "Logement"),
        new CommonCategorizationRule("FREEBOX", "Logement"),
        new CommonCategorizationRule("SYNDIC", "Logement"),
        new CommonCategorizationRule("TAXE FONCIERE", "Logement"),
        new CommonCategorizationRule("TAXE HABITATION", "Logement"),
        new CommonCategorizationRule("LEROY MERLIN", "Logement"),
        new CommonCategorizationRule("CASTORAMA", "Logement"),
        new CommonCategorizationRule("BRICO DEPOT", "Logement"),
        new CommonCategorizationRule("IKEA", "Logement"),
        new CommonCategorizationRule("CONFORAMA", "Logement"),

        // Loisirs : abonnements de musique et de vidéo, jeux, cinéma, sport.
        new CommonCategorizationRule("NETFLIX", "Loisirs"),
        new CommonCategorizationRule("SPOTIFY", "Loisirs"),
        new CommonCategorizationRule("DEEZER", "Loisirs"),
        new CommonCategorizationRule("DISNEY PLUS", "Loisirs"),
        new CommonCategorizationRule("PRIME VIDEO", "Loisirs"),
        new CommonCategorizationRule("YOUTUBE", "Loisirs"),
        new CommonCategorizationRule("STEAM", "Loisirs"),
        new CommonCategorizationRule("PLAYSTATION", "Loisirs"),
        new CommonCategorizationRule("NINTENDO", "Loisirs"),
        new CommonCategorizationRule("XBOX", "Loisirs"),
        new CommonCategorizationRule("CINEMA", "Loisirs"),
        new CommonCategorizationRule("UGC", "Loisirs"),
        new CommonCategorizationRule("PATHE", "Loisirs"),
        new CommonCategorizationRule("GAUMONT", "Loisirs"),
        new CommonCategorizationRule("BASIC FIT", "Loisirs"),
        new CommonCategorizationRule("FITNESS PARK", "Loisirs"),
        new CommonCategorizationRule("TICKETMASTER", "Loisirs"),

        // Santé : pharmacies, soins, assurance maladie et mutuelles.
        new CommonCategorizationRule("PHARMACIE", "Santé"),
        new CommonCategorizationRule("CPAM", "Santé"),
        new CommonCategorizationRule("AMELI", "Santé"),
        new CommonCategorizationRule("MUTUELLE", "Santé"),
        new CommonCategorizationRule("DOCTOLIB", "Santé"),
        new CommonCategorizationRule("MEDECIN", "Santé"),
        new CommonCategorizationRule("DENTISTE", "Santé"),
        new CommonCategorizationRule("OPTICIEN", "Santé"),
        new CommonCategorizationRule("LABORATOIRE", "Santé"),
        new CommonCategorizationRule("HOPITAL", "Santé"),
        new CommonCategorizationRule("CLINIQUE", "Santé"),

        // Shopping : vente en ligne, vêtements, beauté, sport, culture et électronique.
        new CommonCategorizationRule("AMAZON", "Shopping"),
        new CommonCategorizationRule("CDISCOUNT", "Shopping"),
        new CommonCategorizationRule("FNAC", "Shopping"),
        new CommonCategorizationRule("DARTY", "Shopping"),
        new CommonCategorizationRule("BOULANGER", "Shopping"),
        new CommonCategorizationRule("ZALANDO", "Shopping"),
        new CommonCategorizationRule("ZARA", "Shopping"),
        new CommonCategorizationRule("KIABI", "Shopping"),
        new CommonCategorizationRule("PRIMARK", "Shopping"),
        new CommonCategorizationRule("UNIQLO", "Shopping"),
        new CommonCategorizationRule("SEPHORA", "Shopping"),
        new CommonCategorizationRule("DECATHLON", "Shopping"),
        new CommonCategorizationRule("CULTURA", "Shopping"),
        new CommonCategorizationRule("SHEIN", "Shopping"),
        new CommonCategorizationRule("ALIEXPRESS", "Shopping"),
        new CommonCategorizationRule("VINTED", "Shopping"),
        new CommonCategorizationRule("GALERIES LAFAYETTE", "Shopping"),

        // Revenus : salaire, aides, chômage et retraite.
        new CommonCategorizationRule("SALAIRE", "Revenus"),
        new CommonCategorizationRule("PAIE", "Revenus"),
        new CommonCategorizationRule("CAF", "Revenus"),
        new CommonCategorizationRule("FRANCE TRAVAIL", "Revenus"),
        new CommonCategorizationRule("POLE EMPLOI", "Revenus"),
        new CommonCategorizationRule("CARSAT", "Revenus"),
        new CommonCategorizationRule("AGIRC ARRCO", "Revenus"),

        // Exceptions : des mots plus longs qui envoient dans « Autres » ce qu'une règle plus courte classerait mal,
        // puisque la plus longue l'emporte. « CARREFOUR BANQUE » rembourse un crédit : ce n'est pas de l'alimentation.
        new CommonCategorizationRule("CARREFOUR BANQUE", "Autres")
    };

    // Donne le nom de la catégorie de la règle commune qui reconnaît ce libellé, ou null si aucune ne le reconnaît.
    // Quand plusieurs règles le reconnaissent, la plus longue l'emporte : « UBER EATS » passe avant « UBER ».
    // À longueur égale, la première de la liste l'emporte.
    public static string? FindCategoryName(string label)
    {
        var simpleLabel = TransactionLabels.Simplify(label);
        CommonCategorizationRule? chosenRule = null;

        foreach (var rule in All)
        {
            var isLonger = chosenRule == null || rule.Words.Length > chosenRule.Words.Length;

            if (isLonger && TransactionLabels.ContainsWords(simpleLabel, rule.Words))
            {
                chosenRule = rule;
            }
        }

        return chosenRule?.CategoryName;
    }
}
