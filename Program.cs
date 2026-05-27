using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace LeagueTracker
{
    class Program
    {
        // ==========================================
        // 1. SETĂRILE TALE (MODIFICĂ AICI)
        // ==========================================
        static readonly string apiKey = "RGAPI-********-****-****-****-************";

        static readonly string gameName = "username";         // Ex: Faker
        static readonly string tagLine = "EUW";               // Ex: KR1, EUNE, NA1

        static readonly string routingMare = "europe";         // europe, americas sau asia
        static readonly string routingMic = "euw1";            // eun1, euw1, na1 etc.
        // ==========================================

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("=================================");
            Console.WriteLine("   LEAGUE OF LEGENDS TRACKER");
            Console.WriteLine("=================================\n");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Riot-Token", apiKey);

            Console.WriteLine($"Pasul 1: Căutăm PUUID-ul pentru {gameName}#{tagLine}...");
            string puuid = await GetPuuidAsync(client, gameName, tagLine, routingMare);

            if (string.IsNullOrEmpty(puuid))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Eroare: Nu am putut găsi jucătorul. Verifică Numele, Tag-ul sau API Key-ul.");
                Console.ResetColor();
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"PUUID găsit cu succes!");
            await GetSummonerDataAsync(client, puuid, routingMic);
            Console.WriteLine("Pasul 2: Începem monitorizarea meciurilor...\n");

            string GetMesajIntrare() => $"🎮 {gameName} a intrat într-un meci! ⏰ Ora: {DateTime.Now:HH:mm:ss}";
            string GetMesajIesire() => $"🏆 {gameName} a ieșit din meci! ⏰ Ora: {DateTime.Now:HH:mm:ss}";

            bool wasInGame = false;

            string urlSpectatorInitial = $"https://{routingMic}.api.riotgames.com/lol/spectator/v5/active-games/by-summoner/{puuid}";
            HttpResponseMessage responseInitial = await client.GetAsync(urlSpectatorInitial);
            bool esteInMeci = responseInitial.IsSuccessStatusCode;
            wasInGame = esteInMeci;

            await DiscordNotifier.TrimiteTestAsync(gameName, esteInMeci);

            if (esteInMeci)
            {
                TrimiteNotificare($"🎮 Am detectat că {gameName} este deja într-un meci la pornirea programului!");
                await AfiseazaEchipeleAsync(client, puuid, routingMic);
            }

            while (true)
            {
                await Task.Delay(30000);

                string urlSpectator = $"https://{routingMic}.api.riotgames.com/lol/spectator/v5/active-games/by-summoner/{puuid}";
                HttpResponseMessage response = await client.GetAsync(urlSpectator);

                if (response.IsSuccessStatusCode)
                {
                    if (!wasInGame)
                    {
                        wasInGame = true;
                        string mesajIntrare = GetMesajIntrare();

                        await DiscordNotifier.TrimiteNotificareAsync(mesajIntrare);
                        TrimiteNotificare(mesajIntrare);
                        await AfiseazaEchipeleAsync(client, puuid, routingMic);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Încă în meci... Verificăm din nou în 30s.");
                        Console.ResetColor();
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    if (wasInGame)
                    {
                        wasInGame = false;
                        string mesajIesire = GetMesajIesire();

                        await DiscordNotifier.TrimiteNotificareAsync(mesajIesire);

                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine("\n==================================================");
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MECIUL S-A TERMINAT (sau s-a dat FF)!");
                        Console.WriteLine("==================================================\n");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Nu este în meci. Așteptăm 30 secunde...");
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Eroare API: {response.StatusCode}. Reîncercăm într-un minut...");
                    Console.ResetColor();
                    await Task.Delay(60000);
                }
            }
        }

        static async Task<string> GetPuuidAsync(HttpClient client, string name, string tag, string region)
        {
            string url = $"https://{region}.api.riotgames.com/riot/account/v1/accounts/by-riot-id/{name}/{tag}";
            try
            {
                HttpResponseMessage response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                    return doc.RootElement.GetProperty("puuid").GetString();
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[DEBUG] Riot a respins cererea! Status: {response.StatusCode}\nDetalii: {errorContent}\n");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"A apărut o problemă la conexiune: {ex.Message}");
            }
            return null;
        }

        static void TrimiteNotificare(string mesaj)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n==================================================");
            Console.WriteLine($"  !!! NOTIFICARE: {mesaj} !!!");
            Console.WriteLine("==================================================\n");
            Console.ResetColor();
        }

        static async Task GetSummonerDataAsync(HttpClient client, string puuid, string region)
        {
            string url = $"https://{region}.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/{puuid}";
            HttpResponseMessage response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                int summonerLevel = doc.RootElement.GetProperty("summonerLevel").GetInt32();
                int profileIconId = doc.RootElement.GetProperty("profileIconId").GetInt32();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine($"[DATE PROFIL] Nivel Cont: {summonerLevel}");
                Console.WriteLine($"[DATE PROFIL] ID Iconiță Profil: {profileIconId}");
                Console.WriteLine("--------------------------------------------------\n");
                Console.ResetColor();
            }
        }

        static async Task AfiseazaEchipeleAsync(HttpClient client, string puuid, string region)
        {
            string url = $"https://{region}.api.riotgames.com/lol/spectator/v5/active-games/by-summoner/{puuid}";
            HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode) return;

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            Dictionary<int, string> championNames = await GetChampionNamesAsync(client);
            var participants = doc.RootElement.GetProperty("participants");

            var echipa1 = new List<string>();
            var echipa2 = new List<string>();

            foreach (var p in participants.EnumerateArray())
            {
                int teamId = p.GetProperty("teamId").GetInt32();
                string riotId = p.GetProperty("riotId").GetString() ?? "Unknown";
                int champId = p.GetProperty("championId").GetInt32();
                string champName = championNames.ContainsKey(champId) ? championNames[champId] : $"ID:{champId}";

                string linie = $"  🎮 {riotId,-25} | 🗡️  {champName}";

                if (teamId == 100)
                    echipa1.Add(linie);
                else
                    echipa2.Add(linie);
            }

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\n========== ECHIPA ALBASTRĂ ==========");
            echipa1.ForEach(Console.WriteLine);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n========== ECHIPA ROȘIE ==========");
            echipa2.ForEach(Console.WriteLine);
            Console.ResetColor();
            Console.WriteLine("=====================================\n");

            var sb = new StringBuilder();
            sb.AppendLine("**🔵 ECHIPA ALBASTRĂ**");
            foreach (var j in echipa1) sb.AppendLine(j);

            sb.AppendLine();
            sb.AppendLine("**🔴 ECHIPA ROȘIE**");
            foreach (var j in echipa2) sb.AppendLine(j);

            await DiscordNotifier.TrimiteNotificareAsync(sb.ToString());
        }

        static async Task<Dictionary<int, string>> GetChampionNamesAsync(HttpClient client)
        {
            var result = new Dictionary<int, string>();
            try
            {
                string versionJson = await client.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
                using JsonDocument vDoc = JsonDocument.Parse(versionJson);
                string version = vDoc.RootElement[0].GetString();

                string champJson = await client.GetStringAsync($"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/champion.json");
                using JsonDocument cDoc = JsonDocument.Parse(champJson);
                var data = cDoc.RootElement.GetProperty("data");

                foreach (var champ in data.EnumerateObject())
                {
                    int id = champ.Value.GetProperty("key").GetString() is string keyStr ? int.Parse(keyStr) : 0;
                    string name = champ.Value.GetProperty("name").GetString() ?? champ.Name;
                    result[id] = name;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nu am putut lua numele campionilor: {ex.Message}");
            }
            return result;
        }
    }

    // Am mutat Clasa DiscordNotifier direct aici ca să nu mai ceară fișiere separate!
    public class DiscordNotifier
    {
        // ==========================================
        // PUNE AICI LINK-UL WEBHOOK-ULUI DE LA DISCORD
        // ==========================================
        private static readonly string WebhookUrl = "webhookUrl";

        public static async Task TrimiteNotificareAsync(string mesaj)
        {
            if (string.IsNullOrEmpty(WebhookUrl) || WebhookUrl == "LIPEȘTE_LINKUL_AICI") return;

            using HttpClient client = new HttpClient();
            var payload = new { content = mesaj };
            string jsonPayload = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                await client.PostAsync(WebhookUrl, httpContent);
                Console.WriteLine("[Discord] Notificare trimisă cu succes pe telefon!");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Discord Eroare] Nu am putut trimite mesajul: {ex.Message}");
                Console.ResetColor();
            }
        }

        public static async Task TrimiteTestAsync(string gameName, bool esteInMeci)
        {
            string mesajTest = esteInMeci
                ? $"✅ Bot League Tracker a pornit! {gameName} este DEJA în meci!"
                : $"✅ Bot League Tracker a pornit! {gameName} NU este în meci. Aștept intrarea în joc...";

            await TrimiteNotificareAsync(mesajTest);
        }
    }
}