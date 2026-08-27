using Sirius.AssetTool.Charts;
using Sirius.AssetTool.Episodes;

namespace Sirius.AssetTool;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "chart" => ChartCommands.Run(args[1..]),
                "episode" => EpisodeCommands.Run(args[1..]),
                _ => Fail($"Unknown command group '{args[0]}'.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            if (Environment.GetEnvironmentVariable("SIRIUS_ASSET_TOOL_DEBUG") == "1" ||
                Environment.GetEnvironmentVariable("SIRIUS_PACKER_DEBUG") == "1")
            {
                Console.Error.WriteLine(exception);
            }

            return 1;
        }
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run --help for usage.");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Sirius Asset Tool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Sirius.AssetTool chart <command> [arguments]");
        Console.WriteLine("  Sirius.AssetTool episode <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Run 'Sirius.AssetTool chart --help' or 'Sirius.AssetTool episode --help' for command details.");
    }
}
