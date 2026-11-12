using System.Net;

namespace WalletFlow.Application.BankConnections;

// L'utilisateur est là, dans WalletFlow : son adresse IP et son navigateur sont transmis à la banque, comme le prévoit la DSP2.
// Une lecture faite en sa présence ne compte pas dans les 4 lectures par jour que la banque permet sans lui.
public record UserPresence(IPAddress? IpAddress, string? UserAgent);
