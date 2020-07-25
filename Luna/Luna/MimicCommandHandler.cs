using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Discord.Rest;
using Discord.Commands;
using System.Text.RegularExpressions;
using Luna.Sentiment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Luna
{
    class MimicCommandHandler : ICustomCommandHandler
    {
        public static MimicCommandHandler _instance;

        public const string USER_TRACK_FILE = "trackUsers.txt";
        public const string CONSENT_MESSAGE = "Hello, I am a consentual Bot. You can use `!ignoreMe` and `!trackMe` to toggle your privacy. Or you can react to this message with ❌ or ✅";

        public const string CORNELL_MOVIE_SCRIPTS_FILE = "/cornell_movie_quotes_corpus/moviequotes.scripts.txt";
        public const string MOVIE_QUOTE_MARKOV_SAVE = "/moviequote_markov.json";

        // mood & sentiment
        public Dictionary<string, WordEmotion> andbrainWordDB = new Dictionary<string, WordEmotion>();
        public Dictionary<string, WordEmotion> textEmotionDB = new Dictionary<string, WordEmotion>();
        public Dictionary<string, HedonometerEntry> hedonometerDB = new Dictionary<string, HedonometerEntry>();
        public Dictionary<string, float> militaryWordDB = new Dictionary<string, float>();
        public MoodProfile moodProfile = new MoodProfile();
        public bool markMood = false;

        public Dictionary<string, List<(string status, ActivityType activity)>> statusByMood = new Dictionary<string, List<(string status, ActivityType activity)>>();
        public Dictionary<string, List<IEmote>> moodEmoji = new Dictionary<string, List<IEmote>>();

        private readonly DiscordSocketClient _client;
        private static HttpClient _httpClient = new HttpClient();

        private static Semaphore _semaphore = new Semaphore(1, 1);
        private Random r = new Random();

        public MarkovChain movieScriptMarkov = new MarkovChain();

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

            _client.Disconnected += e => Task.Factory.StartNew(SaveMimicData);
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

            {
                using (StreamReader sr = new StreamReader(MimicDirectory + "/emotion_statuses.csv"))
                {
                    int i = 0;
                    string line;
                    do
                    {
                        line = await sr.ReadLineAsync();
                        string[] entry = line?.Split(',');
                        if (i > 0 && entry != null && entry.Length == 4)////
                        {
                            string mood = entry[0].Trim('\"');
                            string emoji = entry[1].Trim('\"');
                            string quip = entry[2].Trim('\"');
                            string activity = entry[3].Trim('\"');

                            if (!string.IsNullOrEmpty(emoji))
                            {
                                if (!moodEmoji.TryGetValue(mood, out List<IEmote> emojiList))
                                {
                                    emojiList = moodEmoji[mood] = new List<IEmote>();
                                }
                                emojiList.Add(new Emoji(emoji));
                            }
                            else
                            {
                                if(!statusByMood.TryGetValue(mood, out List<(string status, ActivityType activity)> quips))
                                {
                                    quips = statusByMood[mood] = new List<(string status, ActivityType activity)>();
                                }
                                quips.Add((quip, Enum.Parse<ActivityType>(activity)));
                            }
                        }
                        i++;
                    } while (line != null);
                }
            }

            // emotional word dataset
            WordEmotion.LoadAsync(MimicDirectory + "/Andbrain/Andbrain_DataSet.csv", andbrainWordDB);

            // hedonometer word sentiment dataset
            HedonometerEntry.LoadAsync(MimicDirectory + "/hedonometer/Hedonometer.csv", hedonometerDB);

            {
                if (File.Exists(MimicDirectory + "/SentimentAnalysisInText/text_emotion_processed.csv"))
                {
                    WordEmotion.LoadAsync(MimicDirectory + "/SentimentAnalysisInText/text_emotion_processed.csv", textEmotionDB);
                }
                else if (!File.Exists(MimicDirectory + "/SentimentAnalysisInText/text_emotion_processed.csv") && File.Exists(MimicDirectory + "/SentimentAnalysisInText/text_emotion.csv"))
                {
                    Regex cleanup = new Regex("(https?://[\\w./]+|@[\\w]+|[^a-z- '*])+");
                    HashSet<string> emotions = new HashSet<string>();
                    Dictionary<string, int> wordCounts = new Dictionary<string, int>();
                    Dictionary<string, Dictionary<string, float>> wordVectors = new Dictionary<string, Dictionary<string, float>>();
                    using (StreamReader sr = new StreamReader(MimicDirectory + "/SentimentAnalysisInText/text_emotion.csv"))
                    {
                        int i = 0;
                        string line;
                        do
                        {
                            line = await sr.ReadLineAsync();
                            string[] entry = line?.Split(',');
                            if (i > 0 && entry != null && entry.Length == 4)////
                            {
                                string emotion = entry[1].Trim('\"');
                                if (emotion == "happiness") emotion = "happy";
                                if (emotion == "sadness") emotion = "sad";

                                string sentence = entry[3].Trim('\"');
                                if (_symSpell != null)
                                {
                                    List<SymSpell.SuggestItem> suggestions = _symSpell.LookupCompound(sentence, 2);
                                    sentence = suggestions[0].term;
                                }
                                string[] words = cleanup.Replace(sentence, " ").Replace("'", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

                                emotions.Add(emotion);

                                foreach (string word in words)
                                {
                                    if (wordCounts.ContainsKey(word))
                                    {
                                        wordCounts[word]++;
                                    }
                                    else
                                    {
                                        wordCounts[word] = 1;
                                    }
                                    if (!wordVectors.TryGetValue(word, out Dictionary<string, float> vector))
                                    {
                                        vector = wordVectors[word] = new Dictionary<string, float>();
                                    }
                                    if (vector.ContainsKey(emotion))
                                    {
                                        vector[emotion]++;
                                    }
                                    else
                                    {
                                        vector[emotion] = 1;
                                    }
                                }
                            }
                            i++;
                        } while (line != null);
                    }
                    using (StreamWriter sw = new StreamWriter(MimicDirectory + "/SentimentAnalysisInText/text_emotion_processed.csv"))
                    {
                        List<string> emotion_ordered = emotions.ToList();

                        // header
                        await sw.WriteAsync("word");
                        foreach (string emotion in emotion_ordered)
                        {
                            await sw.WriteAsync($",{emotion}");
                        }
                        await sw.WriteAsync('\n');

                        foreach (KeyValuePair<string, Dictionary<string, float>> kvp in wordVectors)
                        {
                            if (wordCounts[kvp.Key] > 10)
                            {
                                await sw.WriteAsync(kvp.Key);
                                foreach (string emotion in emotion_ordered)
                                {
                                    if (kvp.Value.ContainsKey(emotion))
                                    {
                                        kvp.Value[emotion] /= wordCounts[kvp.Key];
                                    }
                                    else
                                    {
                                        kvp.Value[emotion] = 0;
                                    }
                                    await sw.WriteAsync($",{kvp.Value[emotion]}");
                                }
                                await sw.WriteAsync('\n');
                            }
                        }
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

            if (File.Exists(MimicDirectory + "/bot_data.json"))
            {
                using (StreamReader reader = new StreamReader(MimicDirectory + "/bot_data.json"))
                {
                    JObject data = await JObject.LoadAsync(new JsonTextReader(reader));
                    moodProfile.LoadJson((JObject)data["mood"]);
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
                else if (fileName.StartsWith("data_"))
                {
                    ulong id = ulong.Parse(fileName.Substring("data_".Length));
                    if (!AllUserData.TryGetValue(id, out CustomUserData playerData))
                    {
                        playerData = AllUserData[id] = new CustomUserData(id);
                    }

                    try { await playerData.LoadDataAsync(file); }
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

            using (StreamWriter writer = new StreamWriter(MimicDirectory + "/bot_data.json"))
            {
                JsonTextWriter jwriter = new JsonTextWriter(writer);
                jwriter.WriteStartObject();

                jwriter.WritePropertyName("mood");
                moodProfile.WriteToJson(jwriter);

                jwriter.WriteEndObject();
            }

            foreach (KeyValuePair<ulong, CustomUserData> kvp in AllUserData)
            {
                kvp.Value.wordChain.Save(MimicDirectory + "/" + kvp.Value.MarkovWordPath);
                kvp.Value.nGramChain.Save(MimicDirectory + "/" + kvp.Value.MarkovGramPath);
                kvp.Value.SaveData(MimicDirectory + "/data_" + kvp.Key + ".json");
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
            bool messageContainsLuna = message.Content.ToLowerInvariant().Contains("luna");
            MoodProfile lastMoodProfile = new MoodProfile(moodProfile);
            string[] messageWords = message.Content.Split(' ');

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

                if (message.Content.Contains("<:aokoping:623298389865660425>") || (message.Content.Contains("🏓") && r.NextDouble() > 0.5))
                {
                    var context2 = new SocketCommandContext(_client, message);
                    await context2.Channel.SendMessageAsync("🏓");
                }

                if (messageContainsLuna || r.NextDouble() < 0.02) // add reaction if luna mentioned OR 2%
                {
                    if (r.NextDouble() < 0.5)
                        await message.AddReactionAsync(new Emoji(char.ConvertFromUtf32(r.Next(0x1F600, 0x1F64F))));
                    else
                        await message.AddReactionAsync(new Emoji(char.ConvertFromUtf32(r.Next(0x1F90D, 0x1F978))));
                }

                if (lastMoodProfile.GetPrimaryMood() == "nukey" && r.NextDouble() < 0.5)
                {
                    var context2 = new SocketCommandContext(_client, message);
                    await context2.Channel.SendMessageAsync(await GetGIFLink("nuke"));
                }
                
                if (message.Channel is IDMChannel || iAmMentioned || (messageContainsLuna && r.NextDouble() > 0.5))
                {
                    var validUsers = AllUserData.Where(x => x.Value.TrackMe);

                    if (validUsers.Any())
                    {
                        var kvp = validUsers.ElementAt(r.Next(validUsers.Count()));

                        bool useMovieQuote = r.NextDouble() < 0.3;
                        bool useNGram = r.NextDouble() < 0.3;
                        bool useTopic = r.NextDouble() < 0.5;

                        MarkovChain markov = useNGram ? kvp.Value.nGramChain : kvp.Value.wordChain;
                        if (useMovieQuote)
                            markov = movieScriptMarkov;

                        MoodProfile generateMood = moodProfile;
                        if (AllUserData.TryGetValue(message.Author.Id, out CustomUserData userData) && r.NextDouble() > 0.5)
                        {
                            generateMood = userData.mood;
                        }

                        string newMessageText = null;
                        if (useTopic)
                        {
                            string topic = messageWords[r.Next(messageWords.Length)];
                            if (markov.Order > 0)
                            {
                                int rIndex = r.Next(message.Content.Length);
                                topic = message.Content.Substring(rIndex, markov.Order);
                            }
                            newMessageText = markov.GenerateSequenceMiddleOut(topic, r, r.Next(25, 180), !useNGram && !useMovieQuote);
                            if (string.IsNullOrEmpty(newMessageText) && r.NextDouble() < 0.3)
                            {
                                newMessageText = await GetGIFLink(topic);
                            }
                        }
                        if (string.IsNullOrEmpty(newMessageText))
                        {
                            newMessageText = markov.GenerateSequence(moodProfile, r, r.Next(25, 180), (x) => GetMood(x, false), !useNGram && !useMovieQuote);
                        }

                        Console.WriteLine();
                        if(_symSpell != null)
                        {
                            List<SymSpell.SuggestItem> suggestions = _symSpell.LookupCompound(newMessageText, 2);
                            foreach(SymSpell.SuggestItem s in suggestions)
                            {
                                Console.WriteLine($"{s.term} | {s.distance} | {s.count}");
                            }
                        }

                        Console.WriteLine($"{(useMovieQuote ? "" : kvp.Key.ToString())} {(useMovieQuote ? "quote" : (useNGram ? "nGram" : "wordGram"))} | {newMessageText}");

                        if (!string.IsNullOrEmpty(newMessageText))
                        {
                            MoodProfile newMessageMood = GetMood(newMessageText, true);
                            moodProfile.Mix(newMessageMood, (float)r.NextDouble());

                            var context2 = new SocketCommandContext(_client, message);
                            await context2.Channel.SendMessageAsync(newMessageText);
                        }
                        Console.WriteLine();
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
                    await Task.Factory.StartNew(() => LogMimicData(message.Author.Id, mimicString));
                }

                {
                    MoodProfile messageMood = GetMood(message.Content, true);

                    // update moods
                    moodProfile.Mix(messageMood, (float)r.NextDouble());
                    if (r.NextDouble() < 0.02)
                    {
                        moodProfile = moodProfile.Opposite(r.NextDouble() < 0.75);
                    }

                    if (AllUserData.TryGetValue(message.Author.Id, out CustomUserData userData))
                    {
                        userData.mood.Mix(messageMood, (float)r.NextDouble());
                    }
                    
                    string newMood = moodProfile.GetPrimaryMood();
                    if (newMood != lastMoodProfile.GetPrimaryMood() && statusByMood.TryGetValue(newMood, out List<(string status, ActivityType activity)> statuses))
                    {
                        Console.WriteLine("New Mood: " + newMood);
                        var newStatus = statuses[r.Next(statuses.Count)];

                        await _client.SetGameAsync(newStatus.status, null, newStatus.activity);
                    }

                    if (markMood && moodEmoji.TryGetValue(messageMood.GetPrimaryMood(), out List<IEmote> emoji))
                    {
                        await message.AddReactionAsync(emoji[r.Next(emoji.Count)]);
                    }
                }
            }
        }

        private MoodProfile GetMood(string sentence, bool debug)
        {
            int emotionCount = 0, hedonometerCount = 0;
            float nukeyness = 0;
            WordEmotion totalEmotion = new WordEmotion();
            HedonometerEntry totalHappiness = new HedonometerEntry();
            string[] words = sentence.Split(' ');
            for(int i = 0; i < words.Length; i++)
            {
                string word = words[i].ToLower().Trim().Trim('.').Trim('\"').Trim(',').Trim('?').Trim('!').Trim(':').Trim(';');
                if (andbrainWordDB.TryGetValue(word, out WordEmotion emotion))
                {
                    //totalEmotion.CopyMax(emotion);
                    totalEmotion.Add(emotion);
                    emotionCount++;
                }
                if (textEmotionDB.TryGetValue(word, out emotion))
                {
                    //totalEmotion.CopyMax(emotion);
                    totalEmotion.Add(emotion);
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
            (string maxEmotion, float maxEmotionValue) = totalEmotion.Max();
            totalEmotion.Divide(maxEmotionValue);
            totalHappiness.happiness /= hedonometerCount;

            MoodProfile result = new MoodProfile() { emotion = totalEmotion, hedonometer = totalHappiness, nukeyness = nukeyness };

            if (debug)
            {
                Console.WriteLine(result.ToString());
                Console.WriteLine($"total: {emotionCount}, {hedonometerCount}");
            }
            return result;
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


        private async Task<string> GetGIFLink(string searchTerm)
        {
            int limit = 40;
            string url = "https://" + $"api.tenor.com/v1/search?q={searchTerm}&key={Environment.GetEnvironmentVariable("TENOR_GIF_API_KEY", EnvironmentVariableTarget.User)}&limit={limit}";

            HttpResponseMessage response = await _httpClient.GetAsync(url);

            Stream s = await response.Content.ReadAsStreamAsync();

            JObject jObject = await JObject.LoadAsync(new JsonTextReader(new StreamReader(s)));
            //Console.WriteLine(jObject.ToString());

            JArray results = jObject?["results"] as JArray;
            if (results.Count == 0) return null;
            JObject rand = results?[r.Next(results.Count)] as JObject;

            return (string)rand?["url"];
        }
    }
}
