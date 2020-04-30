using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Discord.Rest;
using Discord.Commands;
using System.Text.RegularExpressions;

namespace Luna
{
    class MimicCommandHandler : ICustomCommandHandler
    {
        public static MimicCommandHandler _instance;

        public const string userTrackFile = "trackUsers.txt";
        public const string consentMessage = "Hello, I am a consentual Bot. You can use `!ignoreMe` and `!trackMe` to toggle your privacy. Or you can react to this message with ❌ or ✅";

        private readonly DiscordSocketClient _client;

        private static Mutex _mutex = new Mutex();
        private Random r = new Random();

        Dictionary<ulong, CustomUserData> AllUserData => CommandManager._instance.AllUserData;

        private string _mimicDirectory;
        public string MimicDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_mimicDirectory))
                    _mimicDirectory = Environment.GetEnvironmentVariable("KBOT_MIMIC_DATA_PATH", EnvironmentVariableTarget.User);
                return _mimicDirectory;
            }
        }

        SymSpell _symSpell;

        public MimicCommandHandler(DiscordSocketClient client)
        {
            _client = client;
            _instance = this;
        }

        public bool GetConsentualUser(ulong id, out CustomUserData userData)
        {
            if (AllUserData.TryGetValue(id, out userData))
            {
                userData = userData.TrackMe ? userData : null;
                return userData.TrackMe;
            }
            return false;
        }

        public async Task SetupAsync()
        {
            _mutex.WaitOne();

            { // load dictionary
                int initialCapacity = 82765;
                int maxEditDistanceDictionary = 2;
                _symSpell = new SymSpell(initialCapacity, maxEditDistanceDictionary);

                string baseDirectory = Path.GetDirectoryName(AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == "SymSpell").Location);
                string dictionaryPath = baseDirectory + "/frequency_dictionary_en_82_765.txt";
                int termIndex = 0; //column of the term in the dictionary text file
                int countIndex = 1; //column of the term frequency in the dictionary text file
                if (!_symSpell.LoadDictionary(dictionaryPath, termIndex, countIndex))
                {
                    Console.WriteLine("File not found!");
                }

                //load bigram dictionary
                dictionaryPath = baseDirectory + "/frequency_bigramdictionary_en_243_342.txt";
                termIndex = 0; //column of the term in the dictionary text file
                countIndex = 2; //column of the term frequency in the dictionary text file
                if (!_symSpell.LoadBigramDictionary(dictionaryPath, termIndex, countIndex))
                {
                    Console.WriteLine("File not found!");
                }
            }


            string[] mimicFiles = Directory.GetFiles(MimicDirectory);

            foreach (string file in mimicFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.StartsWith(CustomUserData.wordMarkovPrefix))
                {
                    ulong id = ulong.Parse(fileName.Substring(CustomUserData.wordMarkovPrefix.Length));
                    if (!AllUserData.TryGetValue(id, out CustomUserData playerData))
                    {
                        playerData = AllUserData[id] = new CustomUserData(id);
                    }

                    try { playerData.wordChain.LoadFromSave(file); }
                    catch (FileNotFoundException e) { }
                }
                else if (fileName.StartsWith(CustomUserData.gramMarkovPrefix))
                {
                    ulong id = ulong.Parse(fileName.Substring(CustomUserData.gramMarkovPrefix.Length));
                    if (!AllUserData.TryGetValue(id, out CustomUserData playerData))
                    {
                        playerData = AllUserData[id] = new CustomUserData(id);
                    }

                    try { playerData.nGramChain.LoadFromSave(file); }
                    catch (FileNotFoundException e) { }
                }
            }
            _mutex.ReleaseMutex();

            if (File.Exists(MimicDirectory + "/" + userTrackFile))
            {
                using (StreamReader sr = new StreamReader(MimicDirectory + "/" + userTrackFile))
                {
                    string line;
                    do
                    {
                        line = await sr.ReadLineAsync();
                        if (ulong.TryParse(line, out ulong id))
                        {
                            if (!AllUserData.TryGetValue(id, out CustomUserData playerData))
                            {
                                playerData = AllUserData[id] = new CustomUserData(id);
                            }

                            playerData.TrackMe = true;
                        }
                    } while (line != null);
                }
            }
        }

        public void Cleanup()
        {
            SaveMimicData();
        }

        public void SaveMimicData()
        {
            foreach (KeyValuePair<ulong, CustomUserData> kvp in AllUserData)
            {
                kvp.Value.wordChain.Save(MimicDirectory + "/" + kvp.Value.MarkovWordPath);
                kvp.Value.nGramChain.Save(MimicDirectory + "/" + kvp.Value.MarkovGramPath);
            }

            using (StreamWriter sw = new StreamWriter(MimicDirectory + "/" + userTrackFile))
            {
                foreach (KeyValuePair<ulong, CustomUserData> kvp in AllUserData)
                {
                    if (kvp.Value.TrackMe)
                    {
                        sw.WriteLine(kvp.Key);
                    }
                }
            }
        }

        public async Task HandleUserMessageAsync(SocketUserMessage message)
        {
            bool iAmMentioned = message.MentionedUsers.Select(x => x.Id).Contains(_client.CurrentUser.Id);

            if (message.Author.Id != _client.CurrentUser.Id
                && !message.Author.IsBot
                && !string.IsNullOrEmpty(message.Content)
                && !message.Content.StartsWith('~')
                && !message.Content.StartsWith('!')
                && !message.Content.StartsWith('#')
                && (!message.Content.StartsWith('?') || message.Content.StartsWith("?roll")))
            {
                if (message.Content.Contains("say hello", StringComparison.OrdinalIgnoreCase) && iAmMentioned)
                {
                    var context2 = new SocketCommandContext(_client, message);
                    RestUserMessage rm = await context2.Channel.SendMessageAsync(consentMessage);

                    var checkEmoji = new Emoji("\u2705"); //✅
                    var exEmoji = new Emoji("\u274C"); //❌
                    await rm.AddReactionAsync(checkEmoji);
                    await rm.AddReactionAsync(exEmoji);
                    return;
                }

                if (message.Channel is IDMChannel || iAmMentioned)
                {
                    var validUsers = AllUserData.Where(x => x.Value.TrackMe);

                    if (validUsers.Any())
                    {
                        var kvp = validUsers.ElementAt(r.Next(validUsers.Count()));

                        bool useNGram = r.NextDouble() > 0.65;
                        MarkovChain markov = useNGram ? kvp.Value.nGramChain : kvp.Value.wordChain;
                        string newMessageText = markov.GenerateSequence(r, r.Next(25, 180), !useNGram);

                        if(_symSpell != null)
                        {
                            List<SymSpell.SuggestItem> suggestions = _symSpell.LookupCompound(newMessageText, 2);
                            foreach(SymSpell.SuggestItem s in suggestions)
                            {
                                Console.WriteLine($"{s.term} | {s.distance} | {s.count}");
                            }
                        }

                        Console.WriteLine($"{kvp.Key} {(useNGram ? "nGram" : "wordGram")} | {newMessageText}");

                        if (!string.IsNullOrEmpty(newMessageText))
                        {
                            var context2 = new SocketCommandContext(_client, message);
                            await context2.Channel.SendMessageAsync(newMessageText);
                        }
                    }
                }

                if (!iAmMentioned || message.Content.Trim().Split(' ').Length > 1) // don't record in mimic data if text is only the bot's mention
                {
                    string mimicString = message.Content;
                    foreach (SocketUser u in message.MentionedUsers)
                    {
                        Regex userIDRegex = new Regex($"<@(|!|&){u.Id}>");
                        mimicString = userIDRegex.Replace(mimicString, u.Username);
                    }
                    mimicString = mimicString.Replace("@everyone", "everyone");
                    _ = Task.Run(() => LogMimicData(message.Author.Id, mimicString));
                }
            }
        }

        private void LogMimicData(ulong id, string message)
        {
            _mutex.WaitOne();

            if (!AllUserData.TryGetValue(id, out CustomUserData userData))
            {
                userData = AllUserData[id] = new CustomUserData(id);

                try { userData.wordChain.LoadFromSave(MimicDirectory + "/" + userData.MarkovWordPath); }
                catch (FileNotFoundException e) { }
                try { userData.nGramChain.LoadFromSave(MimicDirectory + "/" + userData.MarkovGramPath); }
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

        public async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> before, ISocketMessageChannel channel, SocketReaction reaction)
        {
            var message = await before.GetOrDownloadAsync();
            if (message.Author.Id == _client.CurrentUser.Id && message.Content == consentMessage && reaction.UserId != _client.CurrentUser.Id)
            {
                if (!CommandManager._instance.AllUserData.TryGetValue(reaction.UserId, out CustomUserData userData))
                {
                    userData = CommandManager._instance.AllUserData[reaction.UserId] = new CustomUserData(reaction.UserId);
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
                if (oppositeEmoji != null)
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
    }
}
