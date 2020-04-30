using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Discord.Rest;

namespace Luna
{
    interface ICustomCommandHandler
    {
        Task SetupAsync();
        Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> before, ISocketMessageChannel channel, SocketReaction reaction);
        Task HandleUserMessageAsync(SocketUserMessage message);
        void Cleanup();
    }

    class CommandManager
    {
        public static CommandManager _instance;

        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private List<ICustomCommandHandler> _commandHandlers;

        private Dictionary<ulong, CustomUserData> _allUserData;
        public Dictionary<ulong, CustomUserData> AllUserData { get { return _allUserData; } set { _allUserData = value; } }

        // Retrieve client and CommandService instance via ctor
        public CommandManager(DiscordSocketClient client, CommandService commands)
        {
            _commands = commands;
            _client = client;

            _allUserData = new Dictionary<ulong, CustomUserData>();
            _commandHandlers = new List<ICustomCommandHandler>();

            _instance = this;
        }

        public void AddCustomCommandHandler(ICustomCommandHandler cmdHandler) => _commandHandlers.Add(cmdHandler);
        public bool RemoveCustomCommandHandler(ICustomCommandHandler cmdHandler) => _commandHandlers.Remove(cmdHandler);

        public async Task SetupAsync()
        {
            foreach (ICustomCommandHandler cmdHandler in _commandHandlers)
            {
                await cmdHandler.SetupAsync();
            }

            // Hook the execution event
            _commands.CommandExecuted += OnCommandExecutedAsync;
            // Hook the MessageReceived event into our command handler
            _client.MessageReceived += HandleCommandAsync;

            _client.ReactionAdded += HandleReactionAddedAsync;

            // Here we discover all of the command modules in the entry 
            // assembly and load them. Starting from Discord.NET 2.0, a
            // service provider is required to be passed into the
            // module registration method to inject the 
            // required dependencies.
            //
            // If you do not use Dependency Injection, pass null.
            // See Dependency Injection guide for more information.
            await _commands.AddModulesAsync(assembly: Assembly.GetEntryAssembly(),
                                            services: null);
        }

        public void Cleanup()
        {
            foreach (ICustomCommandHandler cmdHandler in _commandHandlers)
            {
                cmdHandler.Cleanup();
            }
        }

        public async Task OnCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            // We have access to the information of the command executed,
            // the context of the command, and the result returned from the
            // execution in this event.

            // We can tell the user what went wrong
            if (!string.IsNullOrEmpty(result?.ErrorReason))
            {
                await context.Channel.SendMessageAsync(result.ErrorReason);
            }

            // ...or even log the result (the method used should fit into
            // your existing log handler)
            var commandName = command.IsSpecified ? command.Value.Name : "A command";
            Console.WriteLine($"{commandName} was executed at {DateTime.UtcNow}.");
        }

        private async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> before, ISocketMessageChannel channel, SocketReaction reaction)
        {
            foreach (ICustomCommandHandler cmdHandler in _commandHandlers)
            {
                await cmdHandler.HandleReactionAddedAsync(before, channel, reaction);
            }
        }

        private async Task HandleCommandAsync(SocketMessage messageParam)
        {
            // Don't process the command if it was a system message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            foreach (ICustomCommandHandler cmdHandler in _commandHandlers)
            {
                await cmdHandler.HandleUserMessageAsync(message);
            }

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command based on the prefix and make sure no bots trigger commands
            if (!(message.HasCharPrefix('!', ref argPos)) || message.Author.IsBot)
                return;

            // Create a WebSocket-based command context based on the message
            var context = new SocketCommandContext(_client, message);

            // Execute the command with the command context we just
            // created, along with the service provider for precondition checks.
            await _commands.ExecuteAsync(
                context: context,
                argPos: argPos,
                services: null);
        }
    }
}
