namespace Sirius.MasterTool;

internal static class Cli
{
    public static DownloaderOptions? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return null;
        }

        var options = new DownloaderOptions
        {
            LoginToken = Environment.GetEnvironmentVariable("WDS_ACCOUNT_TOKEN"),
            AccessToken = Environment.GetEnvironmentVariable("WDS_AUTH_TOKEN")
        };

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dir": options.OutputDirectory = Next(); break;
                case "--api": options.ApiBootstrapUrl = Next(); break;
                case "--login-token": options.LoginToken = Next(); break;
                case "--access-token": options.AccessToken = Next(); break;
                case "--register-name": options.RegistrationName = Next(); break;
                case "--app-version": options.ApplicationVersion = Next(); break;
                case "--auth-version-suffix": options.AuthenticationVersionSuffix = Next(); break;
                case "--game-version": options.GameVersion = int.Parse(Next()); break;
                case "--platform": options.Platform = Next(); break;
                case "--fm": options.Fm = Next(); break;
                case "--insecure": options.InsecureTls = true; break;
                case "--force": options.Force = true; break;
                case "--no-json": options.ExportJson = false; break;
                case "--sync": options.SyncMode = true; break;
                case "-h":
                case "--help": PrintHelp(); Environment.Exit(0); break;
                default: throw new ArgumentException($"Unknown option: {args[i]}");
            }

            continue;
            string Next() => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"Missing value after {args[i]}");
        }

        if (!options.SyncMode)
            throw new ArgumentException("Specify --sync.");
        return options;
    }

    private static void PrintHelp() => Console.WriteLine("""
Sirius Master Tool

Downloads/updates official MasterData and provides typed MasterMemory database editing through Sirius.Protocol.

Usage:
  Sirius.MasterTool.exe sync [options]
  Sirius.MasterTool.exe db --help

Legacy form also supported:
  Sirius.MasterTool.exe --sync [options]

Options:
  --dir <path>                 Output directory (default: output)
  --api <url>                  Bootstrap API endpoint
  --login-token <token>        Account LoginToken; registers automatically when omitted
  --access-token <token>       Existing authenticated Bearer token
  --register-name <name>       Name used for automatic registration
  --app-version <version>      Public application version
  --auth-version-suffix <text> Authenticate suffix (default: .486)
  --game-version <number>      Game protocol version (default: 2)
  --platform <name>            X-Platform header (default: google-play)
  --fm <value>                 X-FM header (default: 0)
  --force                      Download even when the version is unchanged
  --no-json                    Do not export MasterData tables to JSON
  --insecure                   Disable TLS certificate validation

Environment variables:
  WDS_ACCOUNT_TOKEN
  WDS_AUTH_TOKEN
""");
}
