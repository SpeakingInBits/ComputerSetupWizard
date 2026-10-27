using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComputerSetupWizard.Core;

namespace ComputerSetupWizard.Core.Tests;

public class BotTokenServiceTests
{
    [Theory]
    [InlineData(false)] // PKCS#1, which is what GitHub hands out
    [InlineData(true)]  // PKCS#8
    public void CreateAppJwt_IsSignedWithTheKey(bool pkcs8)
    {
        using var rsa = RSA.Create(2048);
        var pem = pkcs8 ? rsa.ExportPkcs8PrivateKeyPem() : rsa.ExportRSAPrivateKeyPem();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        var jwt = BotTokenService.CreateAppJwt(pem, "123", now);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.True(rsa.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url.DecodeFromChars(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        Assert.Equal("123", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal(1_800_000_000 - 60, payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(1_800_000_000 + 540, payload.RootElement.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void FindNewestKey_PicksTheMostRecentKey()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var older = Path.Combine(dir, "speakinginbits-bot.2026-01-01.private-key.pem");
            var newer = Path.Combine(dir, "speakinginbits-bot.2026-06-01.private-key.pem");
            File.WriteAllText(older, "");
            File.WriteAllText(newer, "");
            File.WriteAllText(Path.Combine(dir, "other.pem"), "");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-10));

            Assert.Equal(newer, BotConfig.FindNewestKey(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindNewestKey_ReturnsNullWithoutAKey() =>
        Assert.Null(BotConfig.FindNewestKey(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
}
