# Setting up the SpeakingInBits bot on a new machine

Commits and pull requests that Claude makes in SpeakingInBits repos use the **SpeakingInBits-Bot** GitHub App (`speakinginbits-bot[bot]`) instead of a personal account. This guide sets that up on a new Windows machine.

| | |
|---|---|
| App | [SpeakingInBits-Bot](https://github.com/organizations/SpeakingInBits/settings/apps/speakinginbits-bot) |
| App ID | `5450211` |
| Installed on | SpeakingInBits org, all repositories |
| Permissions | Contents, Pull requests, Workflows (read & write) |
| Commit name | `speakinginbits-bot[bot]` |
| Commit email | `347181781+speakinginbits-bot[bot]@users.noreply.github.com` |

## The easy way: run ComputerSetupWizard

[ComputerSetupWizard](../README.md) does all of the steps below except generating the key on GitHub, which it opens for you. It also does more than the manual steps:

- It creates bot tokens itself, so it doesn't need `Get-BotToken.ps1` or OpenSSL.
- In each SpeakingInBits clone it sets up, it puts itself in place of gh as the github.com credential helper, in that repo's local config only:

  ```ini
  [credential "https://github.com"]
      helper =
      helper = !'C:\\Users\\you\\AppData\\Local\\Programs\\ComputerSetupWizard\\ComputerSetupWizard.exe' git-credential
  ```

  The empty `helper =` clears the global gh helper for that repo, so `git push` there always gets a fresh bot token, with no `GH_TOKEN` to refresh every hour. Other repos still use gh and your own account.
- `gh` doesn't use git's credential helpers, so pull requests still need `$env:GH_TOKEN = & <wizard exe> token`. The CLAUDE.md section the wizard writes tells Claude to do this.

The rest of this guide is the manual way to set up the bot.

## 1. Install Git and the GitHub CLI

```powershell
winget install --id Git.Git -e --source winget
winget install --id GitHub.cli -e --source winget
```

Approve the administrator prompts, then open a new terminal so `git` and `gh` are on your PATH.

## 2. Sign in to GitHub and set your own Git identity

```powershell
gh auth login
gh auth setup-git
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
```

`gh auth setup-git` makes Git use the GitHub CLI for credentials, which lets the bot token in step 5 handle pushes.

Your personal identity stays the global default. The bot identity is set per repo.

## 3. Create a private key for this machine

Give each machine its own key, so you can revoke one machine without breaking the others.

1. Open the [app settings](https://github.com/organizations/SpeakingInBits/settings/apps/speakinginbits-bot) and click **Generate a private key**.
2. Save the downloaded `.pem` file to `C:\Dev`. Keep the name GitHub gives it (`speakinginbits-bot.<date>.private-key.pem`).
3. Never commit the key or copy it into a repo. When a machine is retired, delete its key on the app settings page.

## 4. Install the token script

Copy [`scripts/Get-BotToken.ps1`](../scripts/Get-BotToken.ps1) to `C:\Dev\Get-BotToken.ps1`. It signs a short-lived request with the newest bot key in `C:\Dev` and exchanges it for an installation token that lasts one hour. Use `-KeyPath` if the key is somewhere else.

If PowerShell refuses to run scripts, allow local scripts for your user:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

Check the setup. This prints the app, installation, permissions and bot identity, but never the token:

```powershell
C:\Dev\Get-BotToken.ps1 -Info
```

## 5. Use the bot in a repo

```powershell
$env:GH_TOKEN = & C:\Dev\Get-BotToken.ps1
git config user.name "speakinginbits-bot[bot]"
git config user.email "347181781+speakinginbits-bot[bot]@users.noreply.github.com"
```

While `GH_TOKEN` is set, `git push` and `gh pr create` act as the bot. Get a new token when it expires after an hour. Run the `git config` lines without `--global` in each repo, so other repos keep your own identity.

## 6. Tell Claude to use the bot

Add this to `~/.claude/CLAUDE.md` so every Claude Code session on the machine follows it:

````markdown
# SpeakingInBits GitHub org: use the bot identity

For any repo in the SpeakingInBits GitHub org, make commits and open PRs as the SpeakingInBits-Bot GitHub App, not as my personal account.

- Get a token (valid ~1 hour; get a fresh one if it expires): `$env:GH_TOKEN = & C:\Dev\Get-BotToken.ps1`. `gh` and `git push` (through the `gh auth setup-git` credential helper) then act as the bot. Never print the token or read the private key.
- Set the bot identity in each repo's local config, never globally:
  - `git config user.name "speakinginbits-bot[bot]"`
  - `git config user.email "347181781+speakinginbits-bot[bot]@users.noreply.github.com"`
- `C:\Dev\Get-BotToken.ps1 -Info` shows the app, installation and permissions without the token.
- The bot can write contents, pull requests and workflows only. Ask me before working around anything outside those permissions.
- Repos outside SpeakingInBits use my normal Git identity.
````
