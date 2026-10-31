using ComputerSetupWizard.Core;

namespace ComputerSetupWizard;

/// <summary>Commands for scripts, git and Claude.</summary>
static class Cli
{
    const string Usage = """
        Usage: ComputerSetupWizard.exe [command]

        With no command, opens the setup wizard.

        Commands:
          token                   Print a SpeakingInBits-Bot token (valid ~1 hour)
          info                    Show the app, installation and permissions, without the token
          git-credential get      Git credential helper; the Bot identity step sets this up
        """;

    public static int Run(string[] args)
    {
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    static async Task<int> RunAsync(string[] args)
    {
        switch (args[0].ToLowerInvariant())
        {
            case "token":
                Console.Out.WriteLine((await new BotTokenService().GetTokenAsync()).Token);
                return 0;

            case "info":
                var info = await new BotTokenService().GetInfoAsync();
                Console.Out.WriteLine($"""
                    App                 : {info.AppName}
                    Slug                : {info.AppSlug}
                    KeyPath             : {info.KeyPath}
                    InstallationId      : {info.InstallationId}
                    RepositorySelection : {info.RepositorySelection}
                    Permissions         : {info.Permissions}
                    TokenExpires        : {info.TokenExpires:u}
                    BotName             : {info.BotName}
                    BotEmail            : {info.BotEmail}
                    """);
                return 0;

            case "git-credential":
                return await GitCredentialAsync(args.ElementAtOrDefault(1));

            case "help" or "--help" or "-h" or "/?":
                Console.Out.WriteLine(Usage);
                return 0;

            default:
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    /// <summary>
    /// Answers git's credential requests for github.com with a fresh bot token.
    /// See https://git-scm.com/docs/gitcredentials#_custom_helpers.
    /// </summary>
    static async Task<int> GitCredentialAsync(string? operation)
    {
        // Only "get" needs an answer; tokens expire on their own, so "store" and "erase" have nothing to do
        if (operation != "get")
            return 0;

        var request = new Dictionary<string, string>();
        while (Console.In.ReadLine() is { Length: > 0 } line)
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
                request[parts[0]] = parts[1];
        }
        if (request.GetValueOrDefault("protocol") != "https" || request.GetValueOrDefault("host") != "github.com")
            return 0;

        var token = await new BotTokenService().GetTokenAsync();
        Console.Out.Write($"username=x-access-token\npassword={token.Token}\n");
        return 0;
    }
}
