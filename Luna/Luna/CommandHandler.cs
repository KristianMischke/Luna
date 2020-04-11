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
    class CommandHandler
    {
        public const string userTrackFile = "trackUsers.txt";

        public static CommandHandler _instance;

        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;

        private static Mutex _mutex = new Mutex();
        private Dictionary<ulong, CustomUserData> _allUserData;
        public Dictionary<ulong, CustomUserData> AllUserData { get { return _allUserData; } }
        public bool GetConsentualUser(ulong id, out CustomUserData userData)
        {
            if (_allUserData.TryGetValue(id, out userData))
            {
                userData = userData.TrackMe ? userData : null;
                return userData.TrackMe;
            }
            return false;
        }

        const string consentMessage = "Hello, I am a consentual Bot. You can use `!ignoreMe` and `!trackMe` to toggle your privacy. Or you can react to this message with ❌ or ✅";

        Random r = new Random();

        // Retrieve client and CommandService instance via ctor
        public CommandHandler(DiscordSocketClient client, CommandService commands)
        {
            _commands = commands;
            _client = client;

            _allUserData = new Dictionary<ulong, CustomUserData>();

            _instance = this;
        }

        public async Task SetupAsync()
        {
            _ = Task.Run(() => LoadMimicData());

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
            var message = await before.GetOrDownloadAsync();
            if (message.Author.Id == _client.CurrentUser.Id && message.Content == consentMessage && reaction.UserId != _client.CurrentUser.Id)
            {
                if (!AllUserData.TryGetValue(reaction.UserId, out CustomUserData userData))
                {
                    userData = _allUserData[reaction.UserId] = new CustomUserData(reaction.UserId);
                }

                var checkEmoji = new Emoji("\u2705"); //✅
                var exEmoji = new Emoji("\u274C"); //❌
                IEmote oppositeEmoji = null;
                if (reaction.Emote.Equals(checkEmoji))
                {
                    Console.WriteLine($"{reaction.UserId} ✅");
                    userData.TrackMe = true;
                    oppositeEmoji = exEmoji;
                }
                else if (reaction.Emote.Equals(exEmoji))
                {
                    Console.WriteLine($"{reaction.UserId} ❌");
                    userData.TrackMe = false;
                    oppositeEmoji = checkEmoji;
                }

                // if the user reacted with both emojis, toggle the opposite one
                if(oppositeEmoji != null)
                {
                    var test = message.GetReactionUsersAsync(oppositeEmoji, 100);
                    var enumerator = test.GetEnumerator();
                    while (await enumerator.MoveNext())
                    {
                        if (enumerator.Current.Select(x => x.Id).Contains(reaction.UserId))
                        {
                            await message.RemoveReactionAsync(oppositeEmoji, reaction.User.Value);
                            break;
                        }
                    }
                }
            }
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
                if (message.Content.Contains("say hello", StringComparison.OrdinalIgnoreCase) && message.MentionedUsers.Select(x => x.Id).Contains(_client.CurrentUser.Id))
                {
                    var context2 = new SocketCommandContext(_client, message);
                    RestUserMessage rm = await context2.Channel.SendMessageAsync(consentMessage);
                    
                    var checkEmoji = new Emoji("\u2705"); //✅
                    var exEmoji = new Emoji("\u274C"); //❌
                    await rm.AddReactionAsync(checkEmoji);
                    await rm.AddReactionAsync(exEmoji);
                    return;
                }

                if (message.Channel is IDMChannel || message.MentionedUsers.Select(x => x.Id).Contains(_client.CurrentUser.Id))
                {
                    var validUsers = _allUserData.Where(x => x.Value.TrackMe);

                    if (validUsers.Any())
                    {
                        var kvp = validUsers.ElementAt(r.Next(validUsers.Count()));

                        bool useNGram = r.NextDouble() > 0.65;
                        MarkovChain markov = useNGram ? kvp.Value.nGramChain : kvp.Value.wordChain;
                        string newMessageText = markov.GenerateSequence(r, r.Next(5, 120), !useNGram);

                        var context2 = new SocketCommandContext(_client, message);
                        await context2.Channel.SendMessageAsync(newMessageText);
                    }
                }

                string mimicString = message.Content;
                foreach (SocketUser u in message.MentionedUsers)
                {
                    Regex userIDRegex = new Regex($"<@(|!|&){u.Id}>");
                    mimicString = userIDRegex.Replace(mimicString, u.Username);
                }
                mimicString.Replace("@everyone", "everyone");
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
                if (fileName.StartsWith(CustomUserData.wordMarkovPrefix))
                {
                    ulong id = ulong.Parse(fileName.Substring(CustomUserData.wordMarkovPrefix.Length));
                    if (!_allUserData.TryGetValue(id, out CustomUserData playerData))
                    {
                        playerData = _allUserData[id] = new CustomUserData(id);
                    }

                    try { playerData.wordChain.LoadFromSave(file); }
                    catch (FileNotFoundException e) { }
                }
                else if (fileName.StartsWith(CustomUserData.gramMarkovPrefix))
                {
                    ulong id = ulong.Parse(fileName.Substring(CustomUserData.gramMarkovPrefix.Length));
                    if (!_allUserData.TryGetValue(id, out CustomUserData playerData))
                    {
                        playerData = _allUserData[id] = new CustomUserData(id);
                    }

                    try { playerData.nGramChain.LoadFromSave(file); }
                    catch (FileNotFoundException e) { }
                }
            }
            _mutex.ReleaseMutex();

            if (File.Exists(mimicDataPath + "/" + userTrackFile))
            {
                using (StreamReader sr = new StreamReader(mimicDataPath + "/" + userTrackFile))
                {
                    if (ulong.TryParse(sr.ReadLine(), out ulong id))
                    {
                        if (!_allUserData.TryGetValue(id, out CustomUserData playerData))
                        {
                            playerData = _allUserData[id] = new CustomUserData(id);
                        }

                        playerData.TrackMe = true;
                    }
                }
            }
        }

        private void LogMimicData(ulong id, string message)
        {
            _mutex.WaitOne();

            if (!_allUserData.TryGetValue(id, out CustomUserData userData))
            {
                userData = _allUserData[id] = new CustomUserData(id);

                string mimicDataPath = Environment.GetEnvironmentVariable("KBOT_MIMIC_DATA_PATH", EnvironmentVariableTarget.User);

                try { userData.wordChain.LoadFromSave(mimicDataPath + "/" + userData.MarkovWordPath); }
                catch (FileNotFoundException e) { }
                try { userData.nGramChain.LoadFromSave(mimicDataPath + "/" + userData.MarkovGramPath); }
                catch (FileNotFoundException e) { }
            }

            _mutex.ReleaseMutex();

            if (userData.TrackMe)
            {
                Console.WriteLine($"{id} {message}");
                userData.nGramChain.LoadNGrams(message, 6);
                userData.wordChain.LoadGramsDelimeter(message, " ");
            }
        }

        public void SaveMimicData()
        {
            string mimicDataPath = Environment.GetEnvironmentVariable("KBOT_MIMIC_DATA_PATH", EnvironmentVariableTarget.User);
            foreach (KeyValuePair<ulong, CustomUserData> kvp in _allUserData)
            {
                kvp.Value.wordChain.Save(mimicDataPath + "/" + kvp.Value.MarkovWordPath);
                kvp.Value.nGramChain.Save(mimicDataPath + "/" + kvp.Value.MarkovGramPath);
            }

            using (StreamWriter sw = new StreamWriter(mimicDataPath + "/" + userTrackFile))
            {
                foreach (KeyValuePair<ulong, CustomUserData> kvp in _allUserData)
                {
                    if (kvp.Value.TrackMe)
                    {
                        sw.WriteLine(kvp.Key);
                    }
                }
            }
        }
    }
}
