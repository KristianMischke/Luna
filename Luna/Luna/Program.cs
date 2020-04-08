using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace Luna
{
    class Program
    {
        private DiscordSocketClient _client;

        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient();

            _client.Log += Log;

            // Some alternative options would be to keep your token in an Environment Variable or a standalone file.
            string token = Environment.GetEnvironmentVariable("DISCORD_KBOT_TOKEN");

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
