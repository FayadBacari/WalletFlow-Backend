namespace WalletFlow.Tests.EndToEnd;

// Les tests qui écrivent pour de vrai dans la base de test tournent seuls, après les autres et l'un après l'autre :
// leurs lignes validées ne gênent ainsi aucun test qui compte des lignes, et les courses ne se gênent pas entre elles.
[CollectionDefinition(Name, DisableParallelization = true)]
public class RealDatabaseCollection
{
    public const string Name = "Base de test réelle";
}
