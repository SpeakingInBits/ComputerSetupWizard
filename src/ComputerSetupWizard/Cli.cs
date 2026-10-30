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
          configure-repo [path]   Set up a SpeakingInBits clone (default: current folder) for the bot
          reset-repo [path]       Remove the bot setup from a clone
          git-credential get      Git credential helper; configure-repo sets this up
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

            case "configure-repo":
                return await ConfigureRepoAsync(RepoPath(args), bot: true);

            case "reset-repo":
                return await ConfigureRepoAsync(RepoPath(args), bot: false);

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

    static string RepoPath(string[] args) => Path.GetFullPath(args.ElementAtOrDefault(1) ?? Environment.CurrentDirectory);

    static async Task<int> ConfigureRepoAsync(string path, bool bot)
    {
        var top = await ProcessRunner.RunAsync("git", ["-C", path, "rev-parse", "--show-toplevel"]);
        if (!top.Succeeded)
        {
            Console.Error.WriteLine($"{path} isn't a Git repository.");
            return 1;
        }
        var repo = Path.GetFullPath(top.Output);

        if (!bot)
        {
            await RepoService.ResetToPersonalAsync(repo);
            Console.Out.WriteLine($"{repo} now uses your personal Git identity.");
            return 0;
        }

        var exe = AppInstall.Exe;
        if (await RepoService.GetInfoAsync(repo, exe) is null)
        {
            Console.Error.WriteLine($"{repo} has no {BotConfig.Org} remote on GitHub, so it keeps your personal identity.");
            return 1;
        }

        await RepoService.ConfigureForBotAsync(repo, exe);
        Console.Out.WriteLine($"{repo} now commits as {BotConfig.CommitName} and pushes with bot tokens.");
        return 0;
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
