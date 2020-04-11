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

namespace Luna
{
    class CommandHandler
    {
        public static CommandHandler _instance;

        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;

        private static Mutex _mutex = new Mutex();
        private Dictionary<ulong, PlayerMarkovData> _markovData;
        public Dictionary<ulong, PlayerMarkovData> MarkovData { get { return _markovData; } }

        Random r = new Random();

        // Retrieve client and CommandService instance via ctor
        public CommandHandler(DiscordSocketClient client, CommandService commands)
        {
            _commands = commands;
            _client = client;

            _markovData = new Dictionary<ulong, PlayerMarkovData>();

            _instance = this;
        }

        public async Task SetupAsync()
        {
            _ = Task.Run(() => LoadMimicData());

            // Hook the execution event
            _commands.CommandExecuted += OnCommandExecutedAsync;
            // Hook the MessageReceived event into our command handler
            _client.MessageReceived += HandleCommandAsync;

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

        private async Task HandleCommandAsync(SocketMessage messageParam)
        {
            // Don't process the command if it was a system message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            if (message.Author.Id != _client.CurrentUser.Id
                && !message.Author.IsBot
                && !string.IsNullOrEmpty(message.Content)
                && !message.Content.StartsWith('~')
                && !message.Content.StartsWith('!')
                && (!message.Content.StartsWith('?') || message.Content.StartsWith("?roll")))
            {
                if (message.Channel is IDMChannel || message.MentionedUsers.Select(x => x.Id).Contains(_client.CurrentUser.Id))
                {
                    KeyValuePair<ulong, PlayerMarkovData> kvp = _markovData.ElementAt(r.Next(_markovData.Count));

                    bool useNGram = r.NextDouble() > 0.65;
                    MarkovChain markov = useNGram ? kvp.Value.nGramChain : kvp.Value.wordChain;
                    string newMessageText = markov.GenerateSequence(r, r.Next(5, 120), !useNGram);

                    var context2 = new SocketCommandContext(_client, message);
                    await context2.Channel.SendMessageAsync(newMessageText);
                }

                string mimicString = message.Content;
                foreach (SocketUser u in message.MentionedUsers)
                {
                    Regex userIDRegex = new Regex($"<@(|!|&){u.Id}>");
                    mimicString = userIDRegex.Replace(mimicString, u.Username);
                }
                _ = Task.Run(() => LogMimicData(message.Author.Id, mimicString));
            }

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command based on the prefix and make sure no bots trigger commands
            if (!(message.HasCharPrefix('!', ref argPos) ||
                message.HasMentionPrefix(_client.CurrentUser, ref argPos)) ||
                message.Author.IsBot)
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

        private void LoadMimicData()
        {
            _mutex.WaitOne();
            string mimicDataPath = Environment.GetEnvironmentVariable("KBOT_MIMIC_DATA_PATH", EnvironmentVariableTarget.User);

            string[] mimicFiles = Directory.GetFiles(mimicDataPath);

            foreach (string file in mimicFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.StartsWith(PlayerMarkovData.wordMarkovPrefix))
                {
                    ulong id = ulong.Parse(fileName.Substring(PlayerMarkovData.wordMarkovPrefix.Length));
                    if (!_markovData.TryGetValue(id, out PlayerMarkovData playerData))
                    {
                        playerData = _markovData[id] = new PlayerMarkovData(id);
                    }

                    try { playerData.wordChain.LoadFromSave(file); }
                    catch (FileNotFoundException e) { }
                }
                else if (fileName.StartsWith(PlayerMarkovData.gramMarkovPrefix))
                {
                    ulong id = ulong.Parse(fileName.Substring(PlayerMarkovData.gramMarkovPrefix.Length));
                    if (!_markovData.TryGetValue(id, out PlayerMarkovData playerData))
                    {
                        playerData = _markovData[id] = new PlayerMarkovData(id);
                    }

                    try { playerData.nGramChain.LoadFromSave(file); }
                    catch (FileNotFoundException e) { }
                }
            }
            _mutex.ReleaseMutex();
        }

        private void LogMimicData(ulong id, string message)
        {
            _mutex.WaitOne();

            if (!_markovData.TryGetValue(id, out PlayerMarkovData playerData))
            {
                playerData = _markovData[id] = new PlayerMarkovData(id);

                string mimicDataPath = Environment.GetEnvironmentVariable("KBOT_MIMIC_DATA_PATH", EnvironmentVariableTarget.User);

                try { playerData.wordChain.LoadFromSave(mimicDataPath + "/" + playerData.MarkovWordPath); }
                catch (FileNotFoundException e) { }
                try { playerData.nGramChain.LoadFromSave(mimicDataPath + "/" + playerData.MarkovGramPath); }
                catch (FileNotFoundException e) { }
            }

            _mutex.ReleaseMutex();

            Console.WriteLine($"{id} {message}");
            playerData.nGramChain.LoadNGrams(message, 6);
            playerData.wordChain.LoadGramsDelimeter(message, " ");
        }

        public void SaveMimicData()
        {
            string mimicDataPath = Environment.GetEnvironmentVariable("KBOT_MIMIC_DATA_PATH", EnvironmentVariableTarget.User);
            foreach (KeyValuePair<ulong, PlayerMarkovData> kvp in _markovData)
            {
                kvp.Value.wordChain.Save(mimicDataPath + "/" + kvp.Value.MarkovWordPath);
                kvp.Value.nGramChain.Save(mimicDataPath + "/" + kvp.Value.MarkovGramPath);
            }
        }
    }
}
