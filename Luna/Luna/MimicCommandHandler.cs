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

        public const string USER_TRACK_FILE = "trackUsers.txt";
        public const string CONSENT_MESSAGE = "Hello, I am a consentual Bot. You can use `!ignoreMe` and `!trackMe` to toggle your privacy. Or you can react to this message with ❌ or ✅";

        public const string CORNELL_MOVIE_SCRIPTS_FILE = "/cornell_movie_quotes_corpus/moviequotes.scripts.txt";
        public const string MOVIE_QUOTE_MARKOV_SAVE = "/moviequote_markov.json";

        //---------------
        public enum AndbrainColumn
        {
            WORD = 0,
            DISGUST,
            SURPRISE,
            NEUTRAL,
            ANGER,
            SADNESS,
            HAPPINESS,
            FEAR,

            COUNT
        }
        public struct WordEmotion
        {
            public float disgust, surprise, neutral, anger, sadness, happiness, fear;
            public string word;

            public static WordEmotion operator +(WordEmotion e0, WordEmotion e1)
            {
                return new WordEmotion
                {
                    word        = e0.word,
                    disgust     = e0.disgust   + e1.disgust,
                    surprise    = e0.surprise  + e1.surprise,
                    neutral     = e0.neutral   + e1.neutral,
                    anger       = e0.anger     + e1.anger,
                    sadness     = e0.sadness   + e1.sadness,
                    happiness   = e0.happiness + e1.happiness,
                    fear        = e0.fear      + e1.fear,
                };
            }

            public static WordEmotion operator /(WordEmotion e0, float f)
            {
                return new WordEmotion
                {
                    word = e0.word,
                    disgust = e0.disgust / f,
                    surprise = e0.surprise / f,
                    neutral = e0.neutral / f,
                    anger = e0.anger / f,
                    sadness = e0.sadness / f,
                    happiness = e0.happiness / f,
                    fear = e0.fear / f,
                };
            }
        }

        public Dictionary<string, WordEmotion> andbrainWordDB = new Dictionary<string, WordEmotion>();
        //---------------

        //---------------
        public enum HedonometerColumn
        {
            WORD = 1,
            HAPPINESS = 3,
            STANDARD_DEVIATION = 4,

            COUNT = 5
        }
        public struct HedonometerEntry
        {
            public string word;
            public float happiness, sd;
        }
        public Dictionary<string, HedonometerEntry> hedonometerDB = new Dictionary<string, HedonometerEntry>();
        //---------------

        public Dictionary<string, float> militaryWordDB = new Dictionary<string, float>();

        private readonly DiscordSocketClient _client;

        private static Semaphore _semaphore = new Semaphore(1, 1);
        private Random r = new Random();

        private MarkovChain movieScriptMarkov = new MarkovChain();

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
            _semaphore.WaitOne();

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

            { // emotional word dataset
                if (File.Exists(MimicDirectory + "/Andbrain/Andbrain_DataSet.csv"))
                {
                    using (StreamReader sr = new StreamReader(MimicDirectory + "/Andbrain/Andbrain_DataSet.csv"))
                    {
                        int i = 0;
                        string line;
                        do
                        {
                            line = await sr.ReadLineAsync();
                            string[] entry = line?.Split(',');
                            if (i > 0 && entry != null && entry.Length == (int)AndbrainColumn.COUNT)
                            {
                                string word = entry[(int)AndbrainColumn.WORD].Trim();

                                andbrainWordDB.Add(word, new WordEmotion
                                {
                                    word = word,
                                    disgust = float.Parse(entry[(int)AndbrainColumn.DISGUST]),
                                    surprise = float.Parse(entry[(int)AndbrainColumn.SURPRISE]),
                                    neutral = float.Parse(entry[(int)AndbrainColumn.NEUTRAL]),
                                    anger = float.Parse(entry[(int)AndbrainColumn.ANGER]),
                                    sadness = float.Parse(entry[(int)AndbrainColumn.SADNESS]),
                                    happiness = float.Parse(entry[(int)AndbrainColumn.HAPPINESS]),
                                    fear = float.Parse(entry[(int)AndbrainColumn.FEAR]),
                                });
                            }
                            i++;
                        } while (line != null);
                    }
                }
            }

            { // hedonometer word sentiment dataset
                if (File.Exists(MimicDirectory + "/hedonometer/Hedonometer.csv"))
                {
                    using (StreamReader sr = new StreamReader(MimicDirectory + "/hedonometer/Hedonometer.csv"))
                    {
                        string line;
                        int i = 0;
                        do
                        {
                            line = await sr.ReadLineAsync();
                            string[] entry = line?.Split(',');
                            if (i > 0 && entry != null && entry.Length == (int)HedonometerColumn.COUNT)
                            {
                                string word = entry[(int)HedonometerColumn.WORD].Trim('\"');

                                hedonometerDB.Add(word, new HedonometerEntry
                                {
                                    word = word,
                                    happiness = float.Parse(entry[(int)HedonometerColumn.HAPPINESS].Trim('\"')),
                                    sd = float.Parse(entry[(int)HedonometerColumn.STANDARD_DEVIATION].Trim('\"'))
                                });
                            }
                            i++;
                        } while (line != null);
                    }
                }
            }

            { // military terms
                if (File.Exists(MimicDirectory + "/military_terms.txt"))
                {
                    using (StreamReader sr = new StreamReader(MimicDirectory + "/military_terms.txt"))
                    {
                        string line;
                        do
                        {
                            line = await sr.ReadLineAsync();
                            string[] entry = line?.Split(':');
                            if (entry != null)
                            {
                                if (entry.Length == 2)
                                {
                                    militaryWordDB.TryAdd(entry[0], float.Parse(entry[1]));
                                }
                                else
                                {
                                    militaryWordDB.TryAdd(line, 0.05f);
                                }
                            }
                        } while (line != null);
                    }
                }
            }

            { // movie script markov
                if (!File.Exists(MimicDirectory + MOVIE_QUOTE_MARKOV_SAVE))
                {
                    if (File.Exists(MimicDirectory + CORNELL_MOVIE_SCRIPTS_FILE))
                    {
                        using (StreamReader sr = new StreamReader(MimicDirectory + CORNELL_MOVIE_SCRIPTS_FILE))
                        {
                            int count = 0;
                            string line;
                            do
                            {
                                line = await sr.ReadLineAsync();

                                string[] arr = line.Split(" +++$+++ ");

                                movieScriptMarkov.LoadNGrams(arr[arr.Length - 1], 6);

                                count++;
                            } while (line != null && count < 100000);
                        }
                    }
                }
                else
                {
                    try { movieScriptMarkov.LoadFromSave(MimicDirectory + MOVIE_QUOTE_MARKOV_SAVE); }
                    catch (FileNotFoundException e) { }
                    catch (Exception e) { Console.WriteLine($"Path: {MimicDirectory + MOVIE_QUOTE_MARKOV_SAVE}\nException: {e.Message}\nStack: {e.StackTrace}"); }
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
                    catch (Exception e) { Console.WriteLine($"Path: {file}\nException: {e.Message}\nStack: {e.StackTrace}"); }
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
                    catch (Exception e) { Console.WriteLine($"Path: {file}\nException: {e.Message}\nStack: {e.StackTrace}"); }
                }
            }

            if (File.Exists(MimicDirectory + "/" + USER_TRACK_FILE))
            {
                using (StreamReader sr = new StreamReader(MimicDirectory + "/" + USER_TRACK_FILE))
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
            _semaphore.Release();
        }

        public void Cleanup()
        {
            SaveMimicData();
        }

        public void SaveMimicData()
        {
            movieScriptMarkov.Save(MimicDirectory + MOVIE_QUOTE_MARKOV_SAVE);

            foreach (KeyValuePair<ulong, CustomUserData> kvp in AllUserData)
            {
                kvp.Value.wordChain.Save(MimicDirectory + "/" + kvp.Value.MarkovWordPath);
                kvp.Value.nGramChain.Save(MimicDirectory + "/" + kvp.Value.MarkovGramPath);
            }

            using (StreamWriter sw = new StreamWriter(MimicDirectory + "/" + USER_TRACK_FILE))
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
                && !string.IsNullOrEmpty(message.Content)
                && !message.Content.StartsWith('~')
                && !message.Content.StartsWith('!')
                && !message.Content.StartsWith('#')
                && (!message.Content.StartsWith('?') || message.Content.StartsWith("?roll")))
            {
                if (message.Content.Contains("say hello", StringComparison.OrdinalIgnoreCase) && iAmMentioned)
                {
                    var context2 = new SocketCommandContext(_client, message);
                    RestUserMessage rm = await context2.Channel.SendMessageAsync(CONSENT_MESSAGE);

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

                        bool useMovieQuote = r.NextDouble() > 0.8f;
                        bool useNGram = r.NextDouble() > 0.65;
                        MarkovChain markov = useNGram ? kvp.Value.nGramChain : kvp.Value.wordChain;
                        if (useMovieQuote)
                            markov = movieScriptMarkov;
                        string newMessageText = markov.GenerateSequence(r, r.Next(25, 180), !useNGram && !useMovieQuote);

                        if(_symSpell != null)
                        {
                            List<SymSpell.SuggestItem> suggestions = _symSpell.LookupCompound(newMessageText, 2);
                            foreach(SymSpell.SuggestItem s in suggestions)
                            {
                                Console.WriteLine($"{s.term} | {s.distance} | {s.count}");
                            }
                        }

                        Console.WriteLine($"{kvp.Key} {(useMovieQuote ? "quote" : (useNGram ? "nGram" : "wordGram"))} | {newMessageText}");

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
                        if (!u.IsBot)
                        {
                            Regex userIDRegex = new Regex($"<@(|!|&){u.Id}>");
                            mimicString = userIDRegex.Replace(mimicString, u.Username);
                        }
                    }
                    mimicString = mimicString.Replace("@everyone", "everyone");
                    _ = Task.Run(() => LogMimicData(message.Author.Id, mimicString));

                    Console.WriteLine(GetEmotion(message.Content));
                }
            }
        }

        private string GetEmotion(string sentence)
        {
            string debugResponse = "";

            int emotionCount = 0, hedonometerCount = 0;
            float nukeyness = 0;
            WordEmotion totalEmotion = new WordEmotion();
            HedonometerEntry totalHappiness = new HedonometerEntry();
            string[] words = sentence.Split(' ');
            for(int i = 0; i < words.Length; i++)
            {
                string word = words[i].ToLower().Trim().Trim('.').Trim('\"').Trim(',').Trim('?').Trim('!');
                if (andbrainWordDB.TryGetValue(word, out WordEmotion emotion))
                {
                    totalEmotion += emotion;
                    emotionCount++;
                }
                if (hedonometerDB.TryGetValue(word, out HedonometerEntry hEntry))
                {
                    totalHappiness.happiness += hEntry.happiness;
                    hedonometerCount++;
                }
                if (militaryWordDB.TryGetValue(word, out float value))
                {
                    nukeyness += value;
                }
            }

            totalEmotion /= emotionCount;
            totalHappiness.happiness /= hedonometerCount;

            debugResponse += $"happy: {totalEmotion.happiness}, neutral: {totalEmotion.neutral}, surprise: {totalEmotion.surprise}, fear: {totalEmotion.fear}, anger: {totalEmotion.anger}, sadness:{totalEmotion.sadness}";
            debugResponse += $"\nhedonometer: {totalHappiness.happiness}";
            debugResponse += $"\ntotal: {emotionCount}, {hedonometerCount}";
            debugResponse += $"\nnukeyness: {nukeyness}";
            return debugResponse;
        }

        private void LogMimicData(ulong id, string message)
        {
            _semaphore.WaitOne();

            if (!AllUserData.TryGetValue(id, out CustomUserData userData))
            {
                userData = AllUserData[id] = new CustomUserData(id);

                try { userData.wordChain.LoadFromSave(MimicDirectory + "/" + userData.MarkovWordPath); }
                catch (FileNotFoundException e) { }
                try { userData.nGramChain.LoadFromSave(MimicDirectory + "/" + userData.MarkovGramPath); }
                catch (FileNotFoundException e) { }
            }

            _semaphore.Release();

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
            if (message.Author.Id == _client.CurrentUser.Id && message.Content == CONSENT_MESSAGE && reaction.UserId != _client.CurrentUser.Id)
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
