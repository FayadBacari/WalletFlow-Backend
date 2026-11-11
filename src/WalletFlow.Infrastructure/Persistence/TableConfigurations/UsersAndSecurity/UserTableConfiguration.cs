using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité User à la table utilisateur du script SQL.
public class UserTableConfiguration : IEntityTypeConfiguration<User>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("utilisateur");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(User.Id));
        builder.Property(nameof(User.Id)).HasColumnName("id_utilisateur").ValueGeneratedNever();

        builder.Property(nameof(User.Email)).HasColumnName("email_utilisateur");
        builder.Property(nameof(User.LastName)).HasColumnName("nom_utilisateur");
        builder.Property(nameof(User.FirstName)).HasColumnName("prenom_utilisateur");
        // L'application n'a pas le droit d'écrire le rôle (MPD, section 8 du script) : il ne s'attribue que depuis le serveur (SS §8).
        // À l'inscription, EF Core ne l'envoie donc pas, et la base met sa valeur par défaut, utilisateur.
        // Toute modification du rôle par le code est refusée par EF Core avant même d'atteindre la base.
        builder.Property(nameof(User.Role)).HasColumnName("role_utilisateur")
            .HasConversion(UserRoleDatabaseCodes.Table.Converter)
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(nameof(User.IsTwoFactorEnabled)).HasColumnName("double_auth_activee");
        builder.Property(nameof(User.NotificationChannel)).HasColumnName("canal_notification")
            .HasConversion(NotificationChannelDatabaseCodes.Table.Converter);
        builder.Property(nameof(User.LockoutCount)).HasColumnName("nb_blocages");
        builder.Property(nameof(User.DeletionRequestedAt)).HasColumnName("date_demande_suppression");
        builder.Property(nameof(User.IsEmailConfirmed)).HasColumnName("email_confirme");
        builder.Property(nameof(User.PasswordHash)).HasColumnName("mot_de_passe_hash");
        builder.Property(nameof(User.EncryptedTotpSecret)).HasColumnName("secret_totp_chiffre");
        builder.Property(nameof(User.FailedLoginAttemptCount)).HasColumnName("nb_tentatives_echouees");
        builder.Property(nameof(User.LockoutEndsAt)).HasColumnName("date_fin_blocage");

        // Toute écriture du compte vérifie que personne ne l'a changé depuis sa lecture, quelle que soit la colonne. Par exemple :
        // - une nouvelle inscription ne remplace pas le mot de passe d'un compte confirmé au même instant ;
        // - une connexion en cours avec l'ancien mot de passe ne s'enregistre pas après une récupération ;
        // - une connexion sans code ne s'enregistre pas si la double authentification vient d'être activée ;
        // - 100 connexions simultanées ne lisent pas toutes « 0 essai » pour écrire toutes « 1 essai ».
        RowVersionConfiguration.AddRowVersion(builder);
    }
}
