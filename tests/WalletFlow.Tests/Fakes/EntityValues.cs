using System.Reflection;
using System.Runtime.CompilerServices;

namespace WalletFlow.Tests.Fakes;

// Lit et écrit toutes les valeurs d'une entité, comme EF Core quand il lit une ligne de la base :
// il passe par les accesseurs « init », que le code de l'application ne peut plus appeler après la création.
// Les fausses bases s'en servent seulement pour annuler une transaction simulée, ou pour jouer l'écriture d'une autre requête.
public static class EntityValues
{
    // Crée une copie de l'entité, avec toutes ses valeurs.
    public static TEntity Copy<TEntity>(TEntity entity) where TEntity : class
    {
        var copy = (TEntity)RuntimeHelpers.GetUninitializedObject(typeof(TEntity));
        Restore(entity, copy);
        return copy;
    }

    // Écrit sur l'entité toutes les valeurs de la copie : l'entité redevient ce qu'elle était quand la copie a été faite.
    public static void Restore<TEntity>(TEntity copy, TEntity target) where TEntity : class
    {
        foreach (var property in typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.CanWrite)
            {
                property.SetValue(target, property.GetValue(copy));
            }
        }
    }

    // Écrit une seule valeur dans l'entité, comme une autre requête qui change une colonne de la ligne.
    public static void WriteProperty<TEntity>(TEntity target, string propertyName, object? value) where TEntity : class
    {
        var property = typeof(TEntity).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        if (property == null || !property.CanWrite)
        {
            throw new ArgumentException("Cette propriété n'existe pas ou ne peut pas être écrite.", nameof(propertyName));
        }

        property.SetValue(target, value);
    }
}
