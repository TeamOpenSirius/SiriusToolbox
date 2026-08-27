namespace Sirius.MasterTool;

internal sealed class DownloaderOptions
{
    public string OutputDirectory { get; set; } = "output";
    public string ApiBootstrapUrl { get; set; } = "https://api.wds-stellarium.com";
    public string? LoginToken { get; set; }
    public string? AccessToken { get; set; }
    public string RegistrationName { get; set; } = "ArchiveUser";
    public string ApplicationVersion { get; set; } = "2.30.1";
    public string AuthenticationVersionSuffix { get; set; } = ".486";
    public int GameVersion { get; set; } = 2;
    public string Platform { get; set; } = "google-play";
    public string Fm { get; set; } = "0";
    public bool InsecureTls { get; set; }
    public bool Force { get; set; }
    public bool ExportJson { get; set; } = true;
    public string? TableSchemaPath { get; set; }
    public bool SyncMode { get; set; }

    public string AuthenticationApplicationVersion => ApplicationVersion + AuthenticationVersionSuffix;
}
