using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;

// Relie l'entité EmailRequest à la table demande_email du script SQL.
public class EmailRequestTableConfiguration : IEntityTypeConfiguration<EmailRequest>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<EmailRequest> builder)
    {
        builder.ToTable("demande_email");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(EmailRequest.Id));
        builder.Property(nameof(EmailRequest.Id)).HasColumnName("id_demande_email").ValueGeneratedNever();

        builder.Property(nameof(EmailRequest.Type)).HasColumnName("type_demande_email")
            .HasConversion(EmailRequestTypeDatabaseCodes.Table.Converter);
        builder.Property(nameof(EmailRequest.EmailAddress)).HasColumnName("adresse_email_demande");
        builder.Property(nameof(EmailRequest.IpAddress)).HasColumnName("adresse_ip_demande");
        builder.Property(nameof(EmailRequest.RequestedAt)).HasColumnName("date_demande_email");
        builder.Property(nameof(EmailRequest.FailedTryCount)).HasColumnName("nb_essais_envoi");
        builder.Property(nameof(EmailRequest.NextTryAt)).HasColumnName("date_prochain_essai");
        builder.Property(nameof(EmailRequest.UserId)).HasColumnName("id_utilisateur");
        builder.Property(nameof(EmailRequest.AlertId)).HasColumnName("id_alerte");
        builder.Property(nameof(EmailRequest.SupportResponseId)).HasColumnName("id_reponse");

        // Un essai raté ou une sortie de la file vérifie que personne n'a changé ni sorti la demande depuis sa lecture.
        RowVersionConfiguration.AddRowVersion(builder);

        // Une demande disparaît avec le compte qu'elle vise (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(EmailRequest.UserId))
            .OnDelete(DeleteBehavior.Cascade);

        // L'e-mail d'une alerte effacée ne part plus (ON DELETE CASCADE).
        builder.HasOne<Alert>().WithMany()
            .HasForeignKey(nameof(EmailRequest.AlertId))
            .OnDelete(DeleteBehavior.Cascade);

        // L'e-mail d'une réponse effacée ne part plus (ON DELETE CASCADE).
        builder.HasOne<SupportResponse>().WithMany()
            .HasForeignKey(nameof(EmailRequest.SupportResponseId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
