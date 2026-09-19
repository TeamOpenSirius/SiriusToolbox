using MessagePack;

namespace Sirius.Toolbox.Master.Protocol;

public enum AccountRegisterErrorTypes { None = 0 }
public enum BanLevels { None = 0 }
public enum GameVersions { Unknown = 0, AppStore = 1, GooglePlay = 2 }
[MessagePackObject(false)]
public sealed class AccountRegistResult
{
    [Key(0)] public string Token { get; set; } = string.Empty;
    [Key(1)] public AccountRegisterErrorTypes ErrorType { get; set; }
}

[MessagePackObject(false)]
public sealed class RegisterPayload
{
    [Key(0)] public string Name { get; set; } = string.Empty;
}

[MessagePackObject(false)]
public sealed class AuthenticatePayload
{
    [Key(0)] public string LoginToken { get; set; } = string.Empty;
    [Key(1)] public GameVersions GameVersion { get; set; }
    [Key(2)] public string ApkHash { get; set; } = string.Empty;
    [Key(3)] public string ApkApplicationSignature { get; set; } = string.Empty;
    [Key(4)] public string ApplicationVersion { get; set; } = string.Empty;
}

[MessagePackObject(false)]
public sealed class AuthenticateResult
{
    [Key(0)] public string Token { get; set; } = string.Empty;
    [Key(1)] public BanLevels BanLevel { get; set; }
    [Key(2)] public DateTime? WarnedUntil { get; set; }
}

[MessagePackObject(false)]
public sealed class EnvironmentResult
{
    [Key(0)] public string ApplicationVersion { get; set; } = string.Empty;
    [Key(1)] public string AssetVersion { get; set; } = string.Empty;
    [Key(2)] public string ApiEndpoint { get; set; } = string.Empty;
    [Key(3)] public string MaintenanceApiEndpoint { get; set; } = string.Empty;
    [Key(4)] public string NewsApiEndpoint { get; set; } = string.Empty;
    [Key(5)] public bool IsMaintenance { get; set; }
    [Key(6)] public string MasterDataUrl { get; set; } = string.Empty;
    [Key(7)] public string StaticContentUrl { get; set; } = string.Empty;
    [Key(8)] public string AssetUrl { get; set; } = string.Empty;
    [Key(9)] public bool IsAppReview { get; set; }
    [Key(10)] public string PhotoContentUrl { get; set; } = string.Empty;
    [Key(11)] public string MultiRealTimeServerUrl { get; set; } = string.Empty;
    [Key(12)] public string ExternalPaymentUrl { get; set; } = string.Empty;
}

[MessagePackObject(false)]
public sealed class LoginPayload
{
    [Key(0)] public string PushNotificationToken { get; set; } = string.Empty;
}

[MessagePackObject(false)]
public sealed class LoginResult
{
    [Key(0)] public int[] InvalidedStarPasses { get; set; } = [];
    [Key(1)] public int LoginPassNotification { get; set; }
    [Key(2)] public bool IsApproachingLoginPassInvalided { get; set; }
    [Key(3)] public long[] InvalidedItemMasterIds { get; set; } = [];
    [Key(4)] public long[] ApproachingItemMasterIds { get; set; } = [];
    [Key(5)] public object?[] StoryEventPointExchangeResult { get; set; } = [];
}

[MessagePackObject(false)]
public sealed class MasterDataManifest
{
    [Key(0)] public string Uri { get; set; } = string.Empty;
    [Key(1)] public string SasToken { get; set; } = string.Empty;
    [Key(2)] public string Version { get; set; } = string.Empty;
    [Key(3)] public long PublishTimestamp { get; set; }
}
