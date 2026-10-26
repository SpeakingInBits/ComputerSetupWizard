<#
.SYNOPSIS
  Gets a SpeakingInBits-Bot installation token for an org (valid ~1 hour).
.DESCRIPTION
  See docs/bot-setup.md. With -Info, prints a summary of the app, installation
  and bot identity instead of the token.
.EXAMPLE
  $env:GH_TOKEN = & C:\Dev\Get-BotToken.ps1
#>
param(
    [string]$AppId = '5450211',
    [string]$KeyPath,
    [string]$Org = 'SpeakingInBits',
    [switch]$Info
)

$ErrorActionPreference = 'Stop'
$openssl = 'C:\Program Files\Git\usr\bin\openssl.exe'
if (-not (Test-Path $openssl)) { throw "OpenSSL not found at $openssl (comes with Git for Windows)." }

# Default to the newest bot key in C:\Dev
if (-not $KeyPath) {
    $KeyPath = Get-ChildItem 'C:\Dev' -Filter 'speakinginbits-bot.*.private-key.pem' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $KeyPath -or -not (Test-Path $KeyPath)) { throw 'Private key not found. Save it in C:\Dev or pass -KeyPath.' }

function ConvertTo-Base64Url([byte[]]$bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

# Build and sign the app JWT (RS256)
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"RS256","typ":"JWT"}'))
$payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes("{""iat"":$($now - 60),""exp"":$($now + 540),""iss"":""$AppId""}"))
$signingInput = "$header.$payload"

$tmp = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $inFile = Join-Path $tmp 'in'
    $sigFile = Join-Path $tmp 'sig'
    [IO.File]::WriteAllBytes($inFile, [Text.Encoding]::ASCII.GetBytes($signingInput))
    & $openssl dgst -sha256 -sign $KeyPath -binary -out $sigFile $inFile
    if ($LASTEXITCODE -ne 0) { throw 'OpenSSL failed to sign the JWT.' }
    $jwt = "$signingInput." + (ConvertTo-Base64Url ([IO.File]::ReadAllBytes($sigFile)))
}
finally {
    Remove-Item -Recurse -Force $tmp -Confirm:$false
}

$headers = @{
    Authorization          = "Bearer $jwt"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'Get-BotToken'
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$installation = Invoke-RestMethod -Uri "https://api.github.com/orgs/$Org/installation" -Headers $headers
$token = Invoke-RestMethod -Method Post -Uri "https://api.github.com/app/installations/$($installation.id)/access_tokens" -Headers $headers

if ($Info) {
    # Safe summary for checking setup; never includes the token itself
    $app = Invoke-RestMethod -Uri 'https://api.github.com/app' -Headers $headers
    $botLogin = "$($app.slug)[bot]"
    $bot = Invoke-RestMethod -Uri "https://api.github.com/users/$([Uri]::EscapeDataString($botLogin))" -Headers @{ 'User-Agent' = 'Get-BotToken' }
    [pscustomobject]@{
        App                 = $app.name
        Slug                = $app.slug
        KeyPath             = $KeyPath
        InstallationId      = $installation.id
        RepositorySelection = $installation.repository_selection
        Permissions         = ($token.permissions.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', '
        TokenExpires        = $token.expires_at
        BotName             = $botLogin
        BotEmail            = "$($bot.id)+$botLogin@users.noreply.github.com"
    }
    return
}

$token.token
