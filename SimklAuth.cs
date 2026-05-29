using System.Text.Json;

namespace SimklJellyseerrSync;

public static class SimklAuth
{
    private const string BaseUrl = "https://api.simkl.com";

    public static async Task<string> AuthenticateAsync(string clientId, string clientSecret)
    {
        using var http = new HttpClient();

        var pinResp = await http.GetStringAsync($"{BaseUrl}/oauth/pin?client_id={clientId}&redirect={Uri.EscapeDataString("urn:ietf:wg:oauth:2.0:oob")}");
        var pinDoc  = JsonDocument.Parse(pinResp).RootElement;
        var pin         = pinDoc.GetProperty("user_code").GetString()!;
        var deviceCode  = pinDoc.GetProperty("device_code").GetString()!;
        var expiresSecs = pinDoc.GetProperty("expires_in").GetInt32();

        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════");
        Console.WriteLine($"  SIMKL PIN: {pin}");
        Console.WriteLine($"  Visit: https://simkl.com/pin");
        Console.WriteLine("══════════════════════════════════════════");
        Console.WriteLine();

        var deadline = DateTime.UtcNow.AddSeconds(expiresSecs);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(5000);
            try
            {
                var pollResp = await http.GetStringAsync($"{BaseUrl}/oauth/pin/{deviceCode}?client_id={clientId}");
                var pollDoc  = JsonDocument.Parse(pollResp).RootElement;
                if (pollDoc.TryGetProperty("access_token", out var tokenProp))
                {
                    var token = tokenProp.GetString()!;
                    Console.WriteLine("SIMKL authentication successful.");
                    return token;
                }
            }
            catch { /* not yet authorized — keep polling */ }
        }

        throw new Exception("PIN expired. Restart the app to try again.");
    }
}
