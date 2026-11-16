namespace WalletFlow.Api.Contracts;

// Un type d'alerte et le choix de l'utilisateur (GR Notif). Type vaut « budgetReached », « newSuggestion », « bagGoalReached »,
// « bankAuthorizationToRenew », « newDevice », « accountLocked », « passwordChange » ou « emailChange ».
// Une alerte qui ne peut pas être désactivée est toujours activée.
public record AlertSettingResponse(string Type, string Label, bool CanBeDisabled, bool IsEnabled);

// Les réglages d'alerte de l'utilisateur : le canal (« email », « phone » ou « emailAndPhone ») et les 10 types d'alerte,
// dans l'ordre de la liste. Tant que l'application mobile n'existe pas, toutes les alertes partent par e-mail, quel que soit le canal.
public record AlertSettingsResponse(string Channel, List<AlertSettingResponse> Alerts);
