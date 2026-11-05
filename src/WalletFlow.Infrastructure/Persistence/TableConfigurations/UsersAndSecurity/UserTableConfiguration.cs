using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité User à la table utilisateur du script SQL.
public class UserTableConfiguration : IEntityTypeConfiguration<User>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<User> builderOfTheUserTableMapping)
    {
        builderOfTheUserTableMapping.ToTable("utilisateur");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheUserTableMapping.HasKey(nameof(User.Id));
        builderOfTheUserTableMapping.Property(nameof(User.Id)).HasColumnName("id_utilisateur").ValueGeneratedNever();

        builderOfTheUserTableMapping.Property(nameof(User.Email)).HasColumnName("email_utilisateur");
        builderOfTheUserTableMapping.Property(nameof(User.LastName)).HasColumnName("nom_utilisateur");
        builderOfTheUserTableMapping.Property(nameof(User.FirstName)).HasColumnName("prenom_utilisateur");
        builderOfTheUserTableMapping.Property(nameof(User.Role)).HasColumnName("role_utilisateur")
            .HasConversion(UserRoleDatabaseCodes.ConverterBetweenUserRoleAndDatabaseCode);
        builderOfTheUserTableMapping.Property(nameof(User.IsTwoFactorEnabled)).HasColumnName("double_auth_activee");
        builderOfTheUserTableMapping.Property(nameof(User.EncryptedTotpSecret)).HasColumnName("secret_totp_chiffre");
        builderOfTheUserTableMapping.Property(nameof(User.NotificationChannel)).HasColumnName("canal_notification")
            .HasConversion(NotificationChannelDatabaseCodes.ConverterBetweenNotificationChannelAndDatabaseCode);
        builderOfTheUserTableMapping.Property(nameof(User.LockoutCount)).HasColumnName("nb_blocages");
        builderOfTheUserTableMapping.Property(nameof(User.DeletionRequestedAt)).HasColumnName("date_demande_suppression");

        ConfigureColumnsCheckedAtEveryUpdateOfTheUser(builderOfTheUserTableMapping);
    }

    // Chaque mise à jour vérifie que ces colonnes n'ont pas changé depuis la lecture du compte. Sinon, rien n'est écrit.
    private static void ConfigureColumnsCheckedAtEveryUpdateOfTheUser(EntityTypeBuilder<User> builderOfTheUserTableMapping)
    {
        // Sans ce contrôle, une nouvelle inscription pourrait remplacer le mot de passe d'un compte confirmé au même instant.
        builderOfTheUserTableMapping.Property(nameof(User.IsEmailConfirmed)).HasColumnName("email_confirme").IsConcurrencyToken();

        // Une connexion en cours avec l'ancien mot de passe ne peut pas s'enregistrer après une récupération.
        builderOfTheUserTableMapping.Property(nameof(User.PasswordHash)).HasColumnName("mot_de_passe_hash").IsConcurrencyToken();

        // Sans ce contrôle, 100 requêtes simultanées liraient toutes « 0 essai » et écriraient toutes « 1 essai »,
        // et le compte ne se bloquerait jamais.
        builderOfTheUserTableMapping.Property(nameof(User.FailedLoginAttemptCount)).HasColumnName("nb_tentatives_echouees").IsConcurrencyToken();

        // À chaque blocage, le compteur revient à 5. Une requête qui a lu le blocage précédent verrait le même compteur,
        // et effacerait le nouveau blocage (vu dans une vraie course).
        builderOfTheUserTableMapping.Property(nameof(User.LockoutEndsAt)).HasColumnName("date_fin_blocage").IsConcurrencyToken();
    }
}
