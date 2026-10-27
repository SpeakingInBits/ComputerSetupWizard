# ComputerSetupWizard

A Windows app that sets up a dev machine: it installs tools with winget, signs you in to GitHub, and sets up the SpeakingInBits-Bot so commits and pull requests in SpeakingInBits repos come from the bot while every other repo keeps your own identity.

It's a single self-contained exe (WinUI 3 on .NET 10), so it runs on a fresh machine with nothing else installed.

## Use it

Download `ComputerSetupWizard-win-x64.exe` (or `-win-arm64`) from the [latest release](https://github.com/SpeakingInBits/ComputerSetupWizard/releases/latest) and run it. The steps are:

1. **Tools**: installs with winget: Git, the GitHub CLI, Claude Code, the .NET 10 SDK, Visual Studio Community 2026 (with the ASP.NET and web development, and .NET MAUI workloads), SQL Server 2025 Developer, Blender and Godot (.NET), plus optional PowerShell 7 and VS Code. If Visual Studio 2026 is already installed without those workloads, the wizard adds them.
2. **GitHub sign-in**: `gh auth login` and your personal global Git identity.
3. **Bot key**: gets this machine's private key for the bot into `C:\Dev`.
4. **Bot repos**: finds SpeakingInBits clones and sets them up for the bot.
5. **Claude Code**: installs the mods from [SpeakingInBits/claude-mods](https://github.com/SpeakingInBits/claude-mods) for your user, so they run in every session, and adds the bot instructions to `~/.claude/CLAUDE.md`. The mod list is read from the marketplace, so new mods show up here without a new wizard. Without the `claude` command line, the mods are turned on in `~/.claude/settings.json` and Claude Code downloads them at the next session start.

The Overview page shows what's done, and every step can be run again.

The first time it sets up a repo, the wizard copies itself to `%LOCALAPPDATA%\Programs\ComputerSetupWizard`, because git runs it from there to get bot tokens.

## Command line

The same exe has commands for scripts, git and Claude:

```powershell
$wizard = "$env:LOCALAPPDATA\Programs\ComputerSetupWizard\ComputerSetupWizard.exe"
& $wizard configure-repo C:\path\to\clone   # set up a SpeakingInBits clone for the bot
& $wizard reset-repo C:\path\to\clone       # back to your personal identity
$env:GH_TOKEN = & $wizard token             # bot token for gh (valid ~1 hour)
& $wizard info                              # app, installation and permissions, no token
```

## How the bot setup works

See [Setting up the SpeakingInBits bot on a new machine](docs/bot-setup.md) for the details, and for doing it by hand.

## Build

Needs the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet test tests/ComputerSetupWizard.Core.Tests
dotnet run --project src/ComputerSetupWizard
dotnet publish src/ComputerSetupWizard -c Release -r win-x64 -p:PublishSingleFile=true -p:DebugType=none -o artifacts/win-x64
```

Pushing a `v*` tag builds both exes and publishes them as a GitHub release.
