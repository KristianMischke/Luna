using Discord;
using Discord.WebSocket;
using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Luna
{
    class Program
    {

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler _onExitHandler;

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private DiscordSocketClient _client;
        private CommandService _commandService;

        private CommandManager _commandHandler;
        
        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient();
            _commandService = new CommandService();

            _client.Log += Log;

            // Some alternative options would be to keep your token in an Environment Variable or a standalone file.
            string token = Environment.GetEnvironmentVariable("DISCORD_KBOT_TOKEN", EnvironmentVariableTarget.User);

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            _commandHandler = new CommandManager(_client, _commandService);
            _commandHandler.AddCustomCommandHandler(new MimicCommandHandler(_client));
            _commandHandler.AddCustomCommandHandler(new GameCommandHandler(_client));
            await _commandHandler.SetupAsync();

            // Some biolerplate to react to close window event
            _onExitHandler += new EventHandler(ExitHandler);
            SetConsoleCtrlHandler(_onExitHandler, true);

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private static bool ExitHandler(CtrlType sig)
        {
            CommandManager._instance.Cleanup();
            return false;
        }
    }
}
