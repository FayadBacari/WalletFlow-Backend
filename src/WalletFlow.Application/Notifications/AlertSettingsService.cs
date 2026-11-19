using WalletFlow.Application.Persistence;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Notifications;

// Cas d'utilisation : l'utilisateur connecté lit et change ses réglages d'alerte (GR Notif) : le canal qui reçoit ses alertes,
// et chaque type d'alerte, activé ou non. Une alerte de sécurité ne se désactive pas. Sans choix enregistré, une alerte est activée.
public class AlertSettingsService
{
    private readonly IAlertRepository alertRepository;
    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux alertes et aux comptes, et l'enregistrement avec réessai.
    public AlertSettingsService(
        IAlertRepository alertRepository,
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService)
    {
        this.alertRepository = alertRepository;
        this.accountRepository = accountRepository;
        this.savingService = savingService;
    }

    // Donne les réglages de l'utilisateur, ou null si son compte n'existe plus.
    public async Task<AlertSettings?> ReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(userId, cancellationToken);

        if (account == null)
        {
            return null;
        }

        var types = await alertRepository.FindTypesAsync(cancellationToken);
        var preferences = await alertRepository.FindPreferencesAsync(userId, cancellationToken);
        var settings = types
            .OrderBy(type => type.Kind)
            .Select(type => new AlertSetting(
                type.Kind,
                type.Label,
                type.CanBeDisabled,
                preferences.SingleOrDefault(preference => preference.Kind == type.Kind)?.IsEnabled ?? true))
            .ToList();

        return new AlertSettings(account.NotificationChannel, settings);
    }

    // Garde le canal choisi. Tant que l'application mobile n'existe pas, les alertes partent quand même par e-mail.
    public async Task<AlertSettingsResult> ChangeChannelAsync(Guid userId, NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        var refusal = await savingService.SaveWithRetriesAsync(
            () => PrepareChannelAsync(userId, channel, cancellationToken),
            (AlertSettingsResult?)new AlertSettingsResult.NotSaved(),
            cancellationToken);

        return await AnswerAsync(userId, refusal, cancellationToken);
    }

    // Active ou désactive ce type d'alerte.
    public async Task<AlertSettingsResult> SwitchAsync(Guid userId, AlertKind kind, bool enabled, CancellationToken cancellationToken = default)
    {
        var refusal = await savingService.SaveWithRetriesAsync(
            () => PrepareSwitchAsync(userId, kind, enabled, cancellationToken),
            (AlertSettingsResult?)new AlertSettingsResult.NotSaved(),
            cancellationToken);

        return await AnswerAsync(userId, refusal, cancellationToken);
    }

    // Un passage : lit le compte, puis prépare le nouveau canal. Renvoie le refus, ou null quand le changement est prêt.
    private async Task<PreparedChanges<AlertSettingsResult?>> PrepareChannelAsync(Guid userId, NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(userId, cancellationToken);

        if (account == null)
        {
            return PreparedChanges<AlertSettingsResult?>.NothingToSave(new AlertSettingsResult.AccountGone());
        }

        account.ChangeNotificationChannel(channel);

        return PreparedChanges<AlertSettingsResult?>.SaveThenReturn(null);
    }

    // Un passage : lit le compte et le type d'alerte, puis crée ou change le choix. Renvoie le refus, ou null quand le
    // changement est prêt. De deux premiers choix simultanés pour le même type, le second bute sur la clé de la table,
    // relit, et change le premier.
    private async Task<PreparedChanges<AlertSettingsResult?>> PrepareSwitchAsync(Guid userId, AlertKind kind, bool enabled, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(userId, cancellationToken);

        if (account == null)
        {
            return PreparedChanges<AlertSettingsResult?>.NothingToSave(new AlertSettingsResult.AccountGone());
        }

        var type = (await alertRepository.FindTypesAsync(cancellationToken)).Single(alertType => alertType.Kind == kind);

        if (!enabled && !type.CanBeDisabled)
        {
            return PreparedChanges<AlertSettingsResult?>.NothingToSave(new AlertSettingsResult.CannotBeDisabled());
        }

        var preference = await alertRepository.FindPreferenceAsync(userId, kind, cancellationToken);

        if (preference == null)
        {
            alertRepository.PreparePreference(AlertPreference.Create(userId, type, enabled));
        }
        else
        {
            preference.Switch(type, enabled);
        }

        return PreparedChanges<AlertSettingsResult?>.SaveThenReturn(null);
    }

    // Donne le refus, ou les réglages tels qu'ils sont maintenant enregistrés.
    private async Task<AlertSettingsResult> AnswerAsync(Guid userId, AlertSettingsResult? refusal, CancellationToken cancellationToken = default)
    {
        if (refusal != null)
        {
            return refusal;
        }

        var settings = await ReadAsync(userId, cancellationToken);

        if (settings == null)
        {
            return new AlertSettingsResult.AccountGone();
        }

        return new AlertSettingsResult.Saved { Settings = settings };
    }
}
