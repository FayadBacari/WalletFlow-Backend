using System.Reflection;
using System.Runtime.CompilerServices;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Règle d'architecture : une entité ne se change jamais de l'extérieur. Ses valeurs se donnent à la création (« init »),
// puis seules ses propres méthodes changent son état. Ce test refuse le retour d'un « { get; set; } » dans le domaine.
public class EntitiesProtectStateTests
{
    [Fact]
    public void NoPropertyOfTheDomainCanBeChangedFromOutsideAfterTheCreation()
    {
        var mutableProperties = new List<string>();

        foreach (var domainType in typeof(User).Assembly.GetTypes())
        {
            foreach (var property in domainType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (HasPublicNonInitSetter(property))
                {
                    mutableProperties.Add(domainType.Name + "." + property.Name);
                }
            }
        }

        Assert.Empty(mutableProperties);
    }

    // Renvoie vrai si la propriété a un « set » public ordinaire. Un « init » porte la marque IsExternalInit.
    private static bool HasPublicNonInitSetter(PropertyInfo property)
    {
        var setter = property.SetMethod;

        if (setter == null || !setter.IsPublic)
        {
            return false;
        }

        return !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
    }
}
