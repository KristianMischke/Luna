using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
using LingK;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.LinearAlgebra.Single;

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
        public UnigramMatrix<float> moodSentimentMatrix = new UnigramMatrix<float>(BasicTokenizer.Identity, BasicTokenizer.Identity, BasicTokenizer.Identity, BasicTokenizer.Identity);
        
        public UnigramMatrix<int> unigramEmotion = new UnigramMatrix<int>(BasicTokenizer.Identity, BasicTokenizer.Identity, BasicTokenizer.Identity, BasicTokenizer.Identity);
        public BigramMatrix<int> bigramEmotion = new BigramMatrix<int>(row => row.ToString(), BasicTokenizer.ParseBigram, BasicTokenizer.Identity, BasicTokenizer.Identity);
        public TrigramMatrix<int> trigramEmotion = new TrigramMatrix<int>(row => row.ToString(), BasicTokenizer.ParseTrigram, BasicTokenizer.Identity, BasicTokenizer.Identity);
        public NaiveBayesClassifier emotionClassifier = new NaiveBayesClassifier(); //TODO: backoff classifier?
        public MoodProfile moodProfile = new MoodProfile();
        public bool markMood = false;

        public Dictionary<string, DenseVector> gloveEmbeddings = new Dictionary<string, DenseVector>();
        public const int GLOVE_DIMS = 200;

        public Dictionary<string, ulong> nicknameMap = new Dictionary<string, ulong>();
        public List<List<string>> nicknameMathLHS = new List<List<string>>();
        public List<List<string>> nicknameMathRHS = new List<List<string>>();

        public Dictionary<string, List<(string status, ActivityType activity)>> statusByMood = new Dictionary<string, List<(string status, ActivityType activity)>>();
        public Dictionary<string, List<IEmote>> moodEmoji = new Dictionary<string, List<IEmote>>();
        public List<string> errorMessages = new List<string>();

        private ConcurrentDictionary<ulong, string> usernameCache = new ConcurrentDictionary<ulong, string>();
        private ConcurrentDictionary<ulong, (ulong userID, string content, DateTimeOffset? time)> messageCache = new ConcurrentDictionary<ulong, (ulong userID, string content, DateTimeOffset? time)>();
        private Queue<(ulong userID, ulong messageID, string content, DateTimeOffset? time)> messageEditQueue = new Queue<(ulong userID, ulong messageID, string content, DateTimeOffset? time)>();

        private static CancellationTokenSource bgTaskCancellationToken;

        private readonly DiscordSocketClient _client;
        private static HttpClient _httpClient = new HttpClient();

        private ulong lastGuildID = 0;

        private static Semaphore _rwSemaphore = new Semaphore(1, 1);
        private static Semaphore _markovSemaphore = new Semaphore(1, 1);
        private static bool readyToSave = false;
        private Random r = new Random();

        [ThreadStatic]private bool printLogMessage;

        public MarkovChain movieScriptMarkov = new MarkovChain();

        Dictionary<ulong, CustomUserData> AllUserData => CommandManager._instance.AllUserData;
        public CustomUserData LunasUser = new CustomUserData(0);

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

        public string BackupDirectory => MimicDirectory + "/backup";
        public string UsersDirectory => MimicDirectory + "/users";

        SymSpell _symSpell;

        public void CacheUsername(ulong id, string name = null)
        {
            SocketUser discordUser = _client.GetUser(id);
            if (discordUser != null)
            {
                name = discordUser.Username;
            }

            if (name != null && AllUserData.TryGetValue(id, out var customUserData))
            {
                usernameCache[id] = name;
                customUserData.discordUsername = name;
            }
        }

        public MimicCommandHandler(DiscordSocketClient client)
        {
            _client = client;
            _instance = this;

            _client.Disconnected += e => Task.Factory.StartNew(() => SaveMimicData());
            printLogMessage = true;
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

        readonly Regex msgRegex = new Regex(@"^(?<uid>\d+)\[(?<uname>.+)\] (?<content>.+)?");
        public async Task InputOldMessages(SocketCommandContext context)
        {
            string[] mimicFiles = Directory.GetFiles(BackupDirectory);
            ulong tempMessageId = 0;

            foreach (var kvp in AllUserData)
            {
                kvp.Value.ClearData();
            }

            foreach (string file in mimicFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.StartsWith("100k_"))
                {
                    using (StreamReader sr = new StreamReader(file))
                    {
                        string authorName = null;
                        ulong authorId = 0;
                        string fullMessageContent = null;

                        string line = "";
                        do
                        {
                            line = await sr.ReadLineAsync();

                            Match m = msgRegex.Match(line);
                            if (!m.Success)
                            {
                                fullMessageContent += "\n" + line;
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(fullMessageContent))
                                {
                                    bool iAmMentioned = fullMessageContent.Contains(_client.CurrentUser.Id.ToString());
                                    bool messageContainsLuna = fullMessageContent.ToLowerInvariant().Contains("luna");

                                    string[] words = fullMessageContent.Split(' ');

                                    try
                                    {
                                        if ((fullMessageContent.StartsWith('!') || fullMessageContent.StartsWith('?') || fullMessageContent.StartsWith('~') || fullMessageContent.StartsWith('-')))
                                        {
                                            context.Message.Content.ToCharArray().Distinct().Single();
                                            // message contains only !!! or ??? so is valid and not a command, so keep on keeping on
                                        }
                                    }
                                    catch (InvalidOperationException e)
                                    {
                                        fullMessageContent = null;
                                        continue;
                                    }

                                    if ((iAmMentioned || messageContainsLuna)
                                        && fullMessageContent.ToLowerInvariant().Contains("join")
                                        && (words.Length == 2 || words.Length == 3)
                                    )
                                    {
                                        fullMessageContent = null;
                                        continue;
                                    }

                                    if ((iAmMentioned || messageContainsLuna)
                                        && fullMessageContent.ToLowerInvariant().Contains("stop")
                                        && (words.Length == 2)
                                    )
                                    {
                                        fullMessageContent = null;
                                        continue;
                                    }

                                    if ((iAmMentioned || messageContainsLuna)
                                        && fullMessageContent.ToLowerInvariant().Contains("stop")
                                        && (words.Length == 2))
                                    {
                                        fullMessageContent = null;
                                        continue;
                                    }

                                    if (!iAmMentioned || fullMessageContent.Trim().Split(' ').Length > 1) // don't record in mimic data if text is only the bot's mention
                                    {
                                        string mimicString = fullMessageContent;

                                        CacheUsername(authorId, authorName);

                                        Regex uidRegex = new Regex(@"<@&71(?<uid>\d+)>");
                                        foreach (Match match in uidRegex.Matches(fullMessageContent))
                                        {
                                            if (match.Success)
                                            {
                                                try
                                                {
                                                    ulong uid = ulong.Parse(match.Groups["uid"].Value);
                                                    CacheUsername(uid);

                                                    Regex userIDRegex = new Regex($"<@(|!|&){uid}>");
                                                    mimicString = userIDRegex.Replace(mimicString, MarkovChain.USER_GRAM);
                                                }
                                                catch (Exception e)
                                                {
                                                }
                                            }
                                        }

                                        uidRegex = new Regex(@"<@(|!|&)\d+>");
                                        mimicString = uidRegex.Replace(mimicString, MarkovChain.USER_GRAM);

                                        mimicString = mimicString.Replace("@everyone", "everyone");
                                        bool prev = printLogMessage;
                                        printLogMessage = false;
                                        LogMimicData(authorId, tempMessageId++, mimicString, DateTimeOffset.Now.Subtract(new TimeSpan(0, 0, 1, 0)));
                                        printLogMessage = prev;
                                    }
                                }

                                authorId = ulong.Parse(m.Groups["uid"].Value);
                                authorName = m.Groups["uname"].Value;
                                fullMessageContent = m.Groups["content"].Value;
                            }
                        } while (!sr.EndOfStream);
                    }

                    Console.WriteLine($"\n---DONE {fileName}---\n");
                    bgTaskCancellationToken.Cancel();
                    StartBGThread();
                }
            }

            Console.WriteLine("DONE ALL");
            SaveMimicData();
        }

        public async Task SaveOldMessages(SocketCommandContext context)
        {
            foreach (SocketTextChannel channel in context.Guild.TextChannels)
            {
                await using (StreamWriter sr = new StreamWriter($"{BackupDirectory}/100k_{channel.Name}.txt"))
                {
                    var asyncEnumerator = channel.GetMessagesAsync(context.Message, Direction.Before, 100_000).GetEnumerator();
                    while (await asyncEnumerator.MoveNext())
                    {
                        foreach (IMessage message in asyncEnumerator.Current)
                        {
                            bool iAmMentioned = message.MentionedUserIds.Contains(_client.CurrentUser.Id);
                            bool messageContainsLuna = message.Content.ToLowerInvariant().Contains("luna");

                            string[] words = message.Content.Split(' ');

                            try
                            {
                                if ((message.Content.StartsWith('!') || message.Content.StartsWith('?')))
                                {
                                    context.Message.Content.ToCharArray().Distinct().Single();
                                    // message contains only !!! or ??? so is valid and not a command, so keep on keeping on
                                }
                            }
                            catch (InvalidOperationException e)
                            {
                                continue;
                            }

                            if ((iAmMentioned || messageContainsLuna)
                                && message.Content.ToLowerInvariant().Contains("join")
                                && (words.Length == 2 || words.Length == 3)
                            )
                            {
                                continue;
                            }

                            if ((iAmMentioned || messageContainsLuna)
                                && message.Content.ToLowerInvariant().Contains("stop")
                                && (words.Length == 2)
                            )
                            {
                                continue;
                            }

                            if ((iAmMentioned || messageContainsLuna)
                                && message.Content.ToLowerInvariant().Contains("stop")
                                && (words.Length == 2))
                            {
                                continue;
                            }

                            if (!iAmMentioned || message.Content.Trim().Split(' ').Length > 1) // don't record in mimic data if text is only the bot's mention
                            {
                                string mimicString = message.Content;

                                CacheUsername(message.Author.Id, message.Author.Username);
                                foreach (ulong uid in message.MentionedUserIds)
                                {
                                    SocketUser u = context.Guild.GetUser(uid);
                                    if (u != null)
                                    {
                                        CacheUsername(u.Id, u.Username);
                                        if (!u.IsBot)
                                        {
                                            //Regex userIDRegex = new Regex($"<@(|!|&){u.Id}>");
                                            //mimicString = userIDRegex.Replace(mimicString, u.Username);
                                        }
                                    }
                                }

                                mimicString = mimicString.Replace("@everyone", "everyone");
                                Console.WriteLine(mimicString);
                                await sr.WriteLineAsync($"{message.Author.Id}[{message.Author.Username}] {mimicString}");
                                //_ = Task.Factory.StartNew(() =>
                                //    LogMimicData(message.Author.Id, message.Id, mimicString, message.EditedTimestamp));
                            }
                        }
                    }
                }

                Console.WriteLine($"\n\n\n---END CHANNEL {channel.Name}---\n\n\n");
                Thread.Sleep(2000);
            }
            Console.WriteLine("DONE ALL");
        }

        public async Task SetupAsync()
        {
            await _client.SetGameAsync("Setup", null, ActivityType.Playing);

            _rwSemaphore.WaitOne();

            if (!Directory.Exists(UsersDirectory))
            {
                Directory.CreateDirectory(UsersDirectory);
            }

            if (Directory.Exists(Path.Combine(UsersDirectory, "luna")))
            {
                await LunasUser.LoadDataAsync(Path.Combine(UsersDirectory, "luna"));
            }
            if (File.Exists(MimicDirectory + "/bot_data.json"))
            {
                using (StreamReader reader = new StreamReader(MimicDirectory + "/bot_data.json"))
                {
                    JObject data = await JObject.LoadAsync(new JsonTextReader(reader));
                    moodProfile.LoadJson((JObject)data["mood"]);

                    if (data.Property("lastAvatarUpdate") != null)
                    {
                        lastAvatarSetDate = DateTime.Parse((string)data["lastAvatarUpdate"]);
                    }
                }
            }

            string[] userDirs = Directory.GetDirectories(UsersDirectory);
            foreach (string file in userDirs)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (ulong.TryParse(fileName, out ulong id))
                {
                    if (!AllUserData.TryGetValue(id, out CustomUserData playerData))
                    {
                        playerData = AllUserData[id] = new CustomUserData(id);
                    }

                    try
                    {
                        await playerData.LoadDataAsync(file);
                    }
                    catch (FileNotFoundException) { }
                    catch (Exception e) { Console.WriteLine($"Path: {file}\nException: {e.Message}\nStack: {e.StackTrace}"); }

                    CacheUsername(id);
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

            {
                using (StreamReader sr = new StreamReader(MimicDirectory + "/error_messages.txt"))
                {
                    string line;
                    do
                    {
                        line = await sr.ReadLineAsync();
                        errorMessages.Add(line);
                    } while (line != null);
                }
            }

            { // load GloVe embeddings
                using (StreamReader sr = new StreamReader(MimicDirectory + $"/glove.6B/glove.6B.{GLOVE_DIMS}d.txt"))
                {
                    string line;
                    do
                    {
                        line = sr.ReadLine();//await sr.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line))
                        {
                            string[] entry = line.Split(' ');

                            if (entry.Length == GLOVE_DIMS + 1)
                            {
                                float[] values = new float[GLOVE_DIMS];
                                for (int i = 0; i < GLOVE_DIMS; i++)
                                {
                                    values[i] = float.Parse(entry[i + 1]);
                                }

                                DenseVector vector = new DenseVector(values);
                                gloveEmbeddings.Add(entry[0], vector);
                            }
                        }
                    } while (line != null);
                }
            }

            { // load nickname math
                nicknameMap.Add("luna", _client.CurrentUser.Id);
                using (StreamReader sr = new StreamReader(MimicDirectory + "/foodMath.txt"))
                {
                    bool doneNicknames = false;
                    string line;
                    do
                    {
                        line = sr.ReadLine();//await sr.ReadLineAsync();

                        if (!string.IsNullOrEmpty(line))
                        {
                            if (!doneNicknames)
                            {
                                string[] entry = line.Split(", ", StringSplitOptions.RemoveEmptyEntries);
                                ulong id = ulong.Parse(entry[0]);
                                for (int i = 1; i < entry.Length; i++)
                                {
                                    nicknameMap.Add(entry[i].ToLowerInvariant(), id);
                                }
                            }
                            else
                            {
                                string[] equation = line.Split("= ", StringSplitOptions.RemoveEmptyEntries);
                                char[] splitChars = " \t".ToCharArray();

                                List<string> lhs = new List<string>();
                                foreach (string item in equation[0].Split(splitChars, StringSplitOptions.RemoveEmptyEntries)) // lhs
                                {
                                    if (nicknameMap.TryGetValue(item.ToLowerInvariant(), out var id))
                                    {
                                        lhs.Add(id.ToString());
                                    }
                                    else
                                    {
                                        lhs.Add(item.ToLowerInvariant());
                                    }
                                }
                                List<string> rhs = new List<string>(equation[1].Split(" || ", StringSplitOptions.RemoveEmptyEntries));

                                nicknameMathLHS.Add(lhs);
                                nicknameMathRHS.Add(rhs);
                            }
                        }
                        else
                        {
                            doneNicknames = true;
                        }
                    } while (line != null);
                }
            }

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


            { // load mood files
                moodSentimentMatrix.Clear();

                // emotional word dataset
                moodSentimentMatrix.Load(MimicDirectory + "/Andbrain/Andbrain_DataSet.csv", delimeter: ',', modifyHeader: (x) => $"andbrain_{x}");
                //WordEmotion.LoadAsync(MimicDirectory + "/Andbrain/Andbrain_DataSet.csv", andbrainWordDB);

                // hedonometer word sentiment dataset
                moodSentimentMatrix.Load(MimicDirectory + "/hedonometer/Hedonometer.csv", delimeter: ',', modifyHeader: (x) => $"hedonometer_{x}", rowKeyColumn:"Word");
                //HedonometerEntry.LoadAsync(MimicDirectory + "/hedonometer/Hedonometer.csv", hedonometerDB);

                 // military terms
                if (File.Exists(MimicDirectory + "/military_terms.txt"))
                {
                    moodSentimentMatrix.Load(MimicDirectory + "/military_terms.txt", delimeter: ':', overrideHeader: new List<string>() { "word", "military" }, defaultValue: 0.05f);
                }
            }

            { // load or generate emotion n-gram model
                string unigramEmotionFile = MimicDirectory + "/SentimentAnalysisInText/unigram.tsv";
                string bigramEmotionFile = MimicDirectory + "/SentimentAnalysisInText/bigram.tsv";
                string trigramEmotionFile = MimicDirectory + "/SentimentAnalysisInText/trigram.tsv";

                if (File.Exists(unigramEmotionFile) && File.Exists(bigramEmotionFile) && File.Exists(trigramEmotionFile))
                {
                    unigramEmotion.Load(unigramEmotionFile);
                    bigramEmotion.Load(bigramEmotionFile);
                    trigramEmotion.Load(trigramEmotionFile);
                }
                else if (File.Exists(MimicDirectory + "/SentimentAnalysisInText/text_emotion.csv"))
                {
                    Regex twitterMention = new Regex("@[\\w]+");

                    using (StreamReader reader = new StreamReader(MimicDirectory + "/SentimentAnalysisInText/text_emotion.csv"))
                    {
                        CSVReader csvReader = new CSVReader(reader, ',');

                        List<string> header = csvReader.ReadRow();
                        int sentimentCol = header.IndexOf("sentiment");
                        int textCol = header.IndexOf("content");

                        List<string> row;
                        while ((row = csvReader.ReadRow()).Count > 0)
                        {
                            string message = PreProcessUserMessage(row[textCol], null);
                            message = twitterMention.Replace(message, BasicTokenizer.USER_GRAM);

                            List<string> tokens = BasicTokenizer.Tokenize(message);
                            BasicTokenizer.LoadGramHelper(tokens, row[sentimentCol], false, unigramEmotion, bigramEmotion, trigramEmotion);
                        }
                    }

                    unigramEmotion.Save(unigramEmotionFile);
                    bigramEmotion.Save(bigramEmotionFile);
                    trigramEmotion.Save(trigramEmotionFile);
                }

                emotionClassifier = new NaiveBayesClassifier();
                int totalSentences = 0;
                foreach (var sentiment in trigramEmotion.GetColumnKeys())
                {
                    totalSentences += unigramEmotion[BasicTokenizer.END_GRAM, sentiment];
                }
                foreach (var sentiment in trigramEmotion.GetColumnKeys())
                {
                    emotionClassifier.AddLanguageModel(sentiment, new TrigramModel(unigramEmotion.GetColumn(sentiment), bigramEmotion.GetColumn(sentiment), trigramEmotion.GetColumn(sentiment), 5), unigramEmotion[BasicTokenizer.END_GRAM, sentiment] / (float)totalSentences);
                }
            }

            _markovSemaphore.WaitOne();
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
                                string text = arr[arr.Length - 1];
                                text = PreProcessUserMessage(text, null);
                                movieScriptMarkov.LoadWordGramsFancy(text);

                                count++;
                            } while (line != null && count < 100000);
                        }
                    }
                }
                else
                {
                    try { movieScriptMarkov.LoadFromSave(MimicDirectory + MOVIE_QUOTE_MARKOV_SAVE); }
                    catch (FileNotFoundException) { }
                    catch (Exception e) { Console.WriteLine($"Path: {MimicDirectory + MOVIE_QUOTE_MARKOV_SAVE}\nException: {e.Message}\nStack: {e.StackTrace}"); }
                }
            }
            _markovSemaphore.Release();
            _rwSemaphore.Release();

            StartBGThread();
            UpdateStatus(null);
        }

        private void StartBGThread()
        {
            readyToSave = false;
            bgTaskCancellationToken = new CancellationTokenSource();
            _ = Task.Factory.StartNew(BackgroundUpdate, bgTaskCancellationToken.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Current);
        }

        static Dictionary<ulong, DateTimeOffset> myLastMessageTime = new Dictionary<ulong, DateTimeOffset>();

        static DateTimeOffset lastMessageTime = DateTimeOffset.UtcNow;
        static int lonelyMinutes = -1;
        private async Task BackgroundUpdate()
        {
            while (true)
            {
                bool commitAll = bgTaskCancellationToken.Token.IsCancellationRequested;

                if (lonelyMinutes == -1)
                    lonelyMinutes = r.Next(90, 120);

                await CheckAndUpdateAvatar();

                while (messageEditQueue.Count > 0)
                {
                    var nextItem = messageEditQueue.Dequeue();
                    if (messageCache.TryGetValue(nextItem.messageID, out var beforeItem))
                    {
                        //TODO: Luna can respond to edits/diffs here
                        messageCache[nextItem.messageID] = (nextItem.userID, nextItem.content, nextItem.time);
                        if (nextItem.time.HasValue && nextItem.time > lastMessageTime)
                            lastMessageTime = nextItem.time.Value;
                    }
                }

                var keys = messageCache.Keys.ToList();
                var items = messageCache.Values.ToList();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (commitAll || item.time == null ||
                        DateTimeOffset.UtcNow.Subtract(item.time.Value).TotalSeconds > 60)
                    {
                        if (messageCache.TryRemove(keys[i], out item))
                        {
                            if (AllUserData.TryGetValue(item.userID, out CustomUserData userData) && userData.TrackMe)
                            {
                                _markovSemaphore.WaitOne();
                                //Console.WriteLine($"{item.userID}[{usernameCache[item.userID] ?? ""}] {item.content} [COMMITTED]");

                                // TODO: include channel and other distinctions in loading the matrix
                                List<string> links = new List<string>();
                                List<string> tokens = BasicTokenizer.Tokenize(item.content, links);
                                List<string> words = tokens.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                                BasicTokenizer.LoadGramHelper(tokens, "all", true, userData.unigramMatrix, userData.bigramMatrix, userData.trigramMatrix);
                                BasicTokenizer.LoadGramHelper(tokens, "all", true, LunasUser.unigramMatrix, LunasUser.bigramMatrix, LunasUser.trigramMatrix);
                                userData.pmiCalc.AddSentence(words, false);
                                LunasUser.pmiCalc.AddSentence(words, false);
                                userData.linkList.AddRange(links);

                                _markovSemaphore.Release();
                            }
                        }
                    }
                }

                if (commitAll)
                {
                    readyToSave = true;
                    break;
                }

                if (DateTimeOffset.UtcNow.Subtract(lastMessageTime).Minutes > lonelyMinutes &&
                    DateTimeOffset.Now.Hour >= 10 && DateTimeOffset.Now.Hour <= 23 && lastGuildID != 0)
                {
                    Console.WriteLine("_____SHOULD HAPPEN RN_____"); //TODO: refactor, change
                    SocketGuild guild = _client.GetGuild(lastGuildID);
                    List<SocketTextChannel> channels = guild.TextChannels.ToList<SocketTextChannel>();
                    if (channels.Count > 0)
                    {
                        SocketTextChannel channel = channels[r.Next(channels.Count)];


                        string[] lonelyWords = new string[]
                        {
                            "lonely",
                            "empty",
                            "alone",
                            "where",
                            "why",
                            "hmm",
                            "gone",
                            "left",
                            "sad",
                            "meaning",
                            "home",
                            "friend",
                            "miss"
                        };

                        var validUsers = AllUserData.Where(x => x.Value.TrackMe).ToList();
                        if (validUsers.Any())
                        {
                            _markovSemaphore.WaitOne();
                            var kvp = validUsers[r.Next(validUsers.Count)];

                            MarkovChain markov = movieScriptMarkov;
                            string newMessageText = null;
                            string topic = lonelyWords[r.Next(lonelyWords.Length)];
                            newMessageText = markov.GenerateSequenceMiddleOut(topic, r, r.Next(25, 180));
                            if (string.IsNullOrEmpty(newMessageText))
                            {
                                newMessageText = await GetGIFLink(topic);
                            }
                            else
                            {
                                newMessageText = markov.ReplaceVariables(newMessageText, r, guild.Users.Where(x => x.Id != _client.CurrentUser.Id).Select(x => x.Mention).ToList());
                            }

                            if (newMessageText != null)
                            {
                                await channel.SendMessageAsync(newMessageText);
                            }

                            lastMessageTime = DateTimeOffset.UtcNow;
                            lonelyMinutes = -1;
                            _markovSemaphore.Release();
                        }
                    }
                }

                Thread.Sleep(1000);
            }
            bgTaskCancellationToken.Dispose();
        }


        readonly DateTime avatarStartDate = new DateTime(2020, 9, 8, 8, 0, 0);
        DateTime? lastAvatarSetDate = null;
        private async Task CheckAndUpdateAvatar()
        {
            if (lastAvatarSetDate == null || lastAvatarSetDate.Value.Date != DateTime.Now.Date || lastAvatarSetDate.Value.Hour != DateTime.Now.Hour)
            {
                int numHours = GetNumHoursForAvatar();
                lastAvatarSetDate = DateTime.Now;

                await _client.CurrentUser.ModifyAsync(x => x.Avatar = new Image(GetAvatar(numHours)));
            }
        }

        public string GetAvatar(int hour) => $"{MimicDirectory}/avatar/{hour:D4}.png";
        public int GetNumHoursForAvatar() => (int)DateTime.Now.Subtract(avatarStartDate).TotalHours;

        public void Cleanup()
        {
            SaveMimicData(true);
        }

        public void SaveMimicData(bool isExit = false)
        {
            try
            {
                bgTaskCancellationToken.Cancel();
                //while (!readyToSave) Thread.Sleep(100);
            }
            catch
            {
                Console.WriteLine("REMEMBER TO FIX THIS MULTITHREADING ISSUE!");
            }
            //StartBGThread();

            _markovSemaphore.WaitOne();
            movieScriptMarkov.Save(MimicDirectory + MOVIE_QUOTE_MARKOV_SAVE);

            LunasUser.SaveData(Path.Combine(UsersDirectory, "luna"));
            using (StreamWriter writer = new StreamWriter(MimicDirectory + "/bot_data.json"))
            {
                JsonTextWriter jwriter = new JsonTextWriter(writer);
                jwriter.WriteStartObject();

                jwriter.WritePropertyName("mood");
                moodProfile.WriteToJson(jwriter);

                if (lastAvatarSetDate.HasValue)
                {
                    jwriter.WritePropertyName("lastAvatarUpdate");
                    jwriter.WriteValue(lastAvatarSetDate.Value);
                }

                jwriter.WriteEndObject();
            }

            foreach (KeyValuePair<ulong, CustomUserData> kvp in AllUserData)
            {
                CacheUsername(kvp.Key);
                kvp.Value.SaveData(UsersDirectory + "/" + kvp.Key);
            }
            _markovSemaphore.Release();

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

            //if (!isExit)
                StartBGThread();
        }

        public async Task HandleUserMessageAsync(SocketUserMessage message)
        {
            try
            {
                bool iAmMentioned = message.MentionedUsers.Select(x => x.Id).Contains(_client.CurrentUser.Id);
                bool messageContainsLuna = message.Content.ToLowerInvariant().Contains("luna");
                MoodProfile lastMoodProfile = new MoodProfile(moodProfile);
                double secondsSinceMyLastMessage = double.MaxValue;
                lock (myLastMessageTime)
                {
                    if (myLastMessageTime.TryGetValue(message.Channel.Id, out DateTimeOffset lastMessageTime))
                    {
                        secondsSinceMyLastMessage = DateTimeOffset.UtcNow.Subtract(lastMessageTime).TotalSeconds;
                    }
                }

                var messageContext = new SocketCommandContext(_client, message);
                if (messageContext.Guild != null)
                    lastGuildID = messageContext.Guild.Id;

                string[] words = message.Content.Split(' ');

                if ((iAmMentioned || messageContainsLuna)
                    && message.Content.ToLowerInvariant().Contains("join")
                    && (words.Length == 2 || words.Length == 3)
                )
                {
                    var context = new SocketCommandContext(_client, message);

                    // Get the audio channel
                    var channel = message.MentionedChannels.Any()
                        ? message.MentionedChannels.First() as IAudioChannel
                        : null;
                    channel = channel ?? (message.Author as IGuildUser)?.VoiceChannel;
                    if (channel == null)
                    {
                        await context.Channel.SendMessageAsync(
                            "User must be in a voice channel, or a voice channel must be passed as an argument.");
                        return;
                    }

                    // For the next step with transmitting audio, you would want to pass this Audio Client in to a service.
                    _ = Task.Factory.StartNew(async () =>
                    {
                        var audioClient = await channel.ConnectAsync();
                        VoiceChannelCommandHandler._instance.OnJoinChannel(audioClient, channel);
                    });

                    return;
                }

                if ((iAmMentioned || messageContainsLuna)
                    && message.Content.ToLowerInvariant().Contains("stop")
                    && (words.Length == 2)
                )
                {
                    lock (myLastMessageTime)
                    {
                        myLastMessageTime.Remove(message.Channel.Id);
                    }

                    await message.AddReactionAsync(new Emoji("👍"));
                    return;
                }

                if (message.Author.Id != _client.CurrentUser.Id
                    && !string.IsNullOrEmpty(message.Content)
                    && !message.Content.StartsWith('~')
                    && !message.Content.StartsWith('!')
                    && !message.Content.StartsWith('#')
                    && (!message.Content.StartsWith('?') || message.Content.StartsWith("?roll")))
                {
                    if (message.Content.ToLowerInvariant().Contains("say hello") && iAmMentioned)
                    {
                        var context2 = new SocketCommandContext(_client, message);
                        RestUserMessage rm = await context2.Channel.SendMessageAsync(CONSENT_MESSAGE);

                        var checkEmoji = new Emoji("\u2705"); //✅
                        var exEmoji = new Emoji("\u274C"); //❌
                        await rm.AddReactionAsync(checkEmoji);
                        await rm.AddReactionAsync(exEmoji);
                        return;
                    }

                    // HI response
                    Regex hiRegex = new Regex(@"^(h+i+|h+e+y+|h+e+l+o+) (?<predicate>([^\s]+( [^\s]+)?))$");
                    Match hiMatch = hiRegex.Match(message.Content.ToLowerInvariant());
                    if (hiMatch.Success && hiMatch.Groups["predicate"].Success && r.NextDouble() < 0.8)
                    {
                        var context = new SocketCommandContext(_client, message);
                        await context.Channel.SendMessageAsync($"I'M LUNA");
                    }

                    // Phrasing 1.0 response
                    Regex phrasingRegex = new Regex(@"((i'?m|he'?s|she'?s|they'?re|i am|he is|she is|they are) c+o+m+i+n+g+|c+o+m+e+ o+n+)");
                    Match phrasingMatch = phrasingRegex.Match(message.Content.ToLowerInvariant());
                    if (phrasingMatch.Success && r.NextDouble() < 0.8)
                    {
                        await message.AddReactionAsync(Emote.Parse("<:phrasing:758047809432977549>"));
                    }

                    // Dad Joke
                    Regex imDadRegex = new Regex(@"^([^\s]+ )?(i'?m|i am) (?<predicate>([^\s]+( [^\s]+)?))$");
                    Match dadMatch = imDadRegex.Match(message.Content.ToLowerInvariant());
                    if (dadMatch.Success && dadMatch.Groups["predicate"].Success && r.NextDouble() < 0.8)
                    {
                        string predicate = message.Content.Substring(dadMatch.Groups["predicate"].Index,
                            dadMatch.Groups["predicate"].Length);

                        var context = new SocketCommandContext(_client, message);
                        await context.Channel.SendMessageAsync($"Hi {predicate}, I'm Luna");
                    }

                    // Your Mom joke
                    Regex yourMomRegex = new Regex(@"^([^\s]+ )?(you'?re|you are) (?<predicate>([^\s]+( [^\s]+)?))$");
                    Match momMatch = yourMomRegex.Match(message.Content.ToLowerInvariant());
                    if (momMatch.Success && momMatch.Groups["predicate"].Success && r.NextDouble() < 0.65)
                    {
                        string predicate = message.Content.Substring(momMatch.Groups["predicate"].Index,
                            momMatch.Groups["predicate"].Length);

                        var context = new SocketCommandContext(_client, message);
                        await context.Channel.SendMessageAsync($"Your mom is {predicate}");
                    }

                    // What's XXX => Wikipedia search
                    string wikiLookupTerm = WikiLookupTerm(message);
                    // Chicken Butt
                    Regex chickenButWhatsUpRegex = new Regex(@"^((what'?s|what is) up\??)$");
                    if (chickenButWhatsUpRegex.IsMatch(message.Content.ToLowerInvariant()) && r.NextDouble() < 0.70)
                    {
                        var context = new SocketCommandContext(_client, message);
                        await context.Channel.SendMessageAsync("Chicken Butt");
                    }
                    else if (!string.IsNullOrEmpty(wikiLookupTerm))
                    {
                        await message.AddReactionAsync(new Emoji("\u2754"));// ❔
                    }


                    if (message.Content.Contains("<:aokoping:623298389865660425>") ||
                        (message.Content.Contains("🏓") && r.NextDouble() > 0.5))
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
                        if (r.NextDouble() < 0.05)
                            await context2.Channel.SendMessageAsync(await GetGIFLink("hacking"));
                        await context2.Channel.SendMessageAsync(await GetGIFLink("nuke"));
                    }

                    if (message.Channel is IDMChannel
                        || iAmMentioned
                        || (messageContainsLuna && r.NextDouble() < 0.5)
                        || (secondsSinceMyLastMessage > r.NextDouble() * 3 && secondsSinceMyLastMessage < r.Next(30) &&
                            r.NextDouble() < 0.75)
                    )
                    {
                        double threshold = 0;
                        do
                        {
                            string newMessageText = await GetMimicMessage(message);
                            if (!string.IsNullOrWhiteSpace(newMessageText))
                            {
                                MoodProfile newMessageMood = GetMood(newMessageText, true);
                                moodProfile.Mix(newMessageMood, (float) r.NextDouble());

                                var context2 = new SocketCommandContext(_client, message);
                                await context2.Channel.SendMessageAsync(newMessageText);

                                lock (myLastMessageTime)
                                {
                                    myLastMessageTime[message.Channel.Id] = DateTimeOffset.UtcNow;
                                }
                            }

                            threshold += r.NextDouble();
                        } while (threshold < 0.10);
                    }

                    CacheUsername(message.Author.Id, message.Author.Username);
                    if (!iAmMentioned || message.Content.Trim().Split(' ').Length > 1) // don't record in mimic data if text is only the bot's mention
                    {
                        string mimicString = PreProcessUserMessage(message.Content, message.MentionedUsers);
                        _ = Task.Factory.StartNew(() =>
                            LogMimicData(message.Author.Id, message.Id, mimicString, message.Timestamp));
                    }

                    {
                        MoodProfile messageMood = GetMood(message.Content, true);

                        // update moods
                        moodProfile.Mix(messageMood, (float) r.NextDouble());
                        if (r.NextDouble() < 0.02)
                        {
                            moodProfile = moodProfile.Opposite(r.NextDouble() < 0.85);
                        }

                        if (AllUserData.TryGetValue(message.Author.Id, out CustomUserData userData))
                        {
                            userData.mood.Mix(messageMood, (float) r.NextDouble());
                        }

                        UpdateStatus(lastMoodProfile);

                        if (markMood && moodEmoji.TryGetValue(messageMood.GetPrimaryMood(), out List<IEmote> emoji))
                        {
                            await message.AddReactionAsync(emoji[r.Next(emoji.Count)]);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());

                /*var err_context = new SocketCommandContext(_client, message);
                if (r.NextDouble() < 0.2)
                {
                    await err_context.Channel.SendMessageAsync(await GetGIFLink("error"));
                }
                else
                {
                    string errorQuip = errorMessages[r.Next(errorMessages.Count)];
                    if (!string.IsNullOrEmpty(errorQuip))
                        await err_context.Channel.SendMessageAsync(errorQuip);
                }

                string err_msg = e.ToString();
                await err_context.Channel.SendMessageAsync(err_msg.Substring(0, Math.Min(1000, err_msg.Length)));
                */
            }
        }

        public string WikiLookupTerm(IMessage message)
        {
            Regex whatIsRegex = new Regex(@"^(?<luna>luna |[^\s]+ )?((what'?s|what is|what'?re|what are|who is|who are)( a)? (?<lookup>[^?]+)\??)$");
            Match whatIsMatch = whatIsRegex.Match(message.Content.ToLowerInvariant());
            if (whatIsMatch.Success && (!whatIsMatch.Groups["luna"].Success || whatIsMatch.Groups["luna"].Value.Contains(_client.CurrentUser.Id.ToString())))
            {
                return whatIsMatch.Groups["lookup"].Value;
            }

            return null;
        }

        private async void UpdateStatus(MoodProfile lastMoodProfile)
        {
            string newMood = moodProfile.GetPrimaryMood();
            if ((lastMoodProfile == null || newMood != lastMoodProfile.GetPrimaryMood())
                && statusByMood.TryGetValue(newMood, out List<(string status, ActivityType activity)> statuses))
            {
                Console.WriteLine("New Mood: " + newMood);
                var newStatus = statuses[r.Next(statuses.Count)];

                await _client.SetGameAsync(newStatus.status, null, newStatus.activity);
            }
        }

        static Regex profanityRegex = new Regex($"(n+ *i+ *(g *)+(a+|e+ *r+|r+))", RegexOptions.IgnoreCase);
        public string PreProcessUserMessage(string message, IEnumerable<IUser> mentionedUsers)
        {
            if (mentionedUsers != null)
            {
                foreach (IUser u in mentionedUsers)
                {
                    if (u != null)
                    {
                        CacheUsername(u.Id, u.Username);
                        if (!u.IsBot)
                        {
                            Regex userIDRegex = new Regex($"<@(|!|&){u.Id}>");
                            message = userIDRegex.Replace(message, MarkovChain.USER_GRAM);
                        }
                    }
                }
            }

            message = profanityRegex.Replace(message, "n****");

            message = message.Replace("@everyone", "everyone");
            return message;
        }

        public async Task<string> GetMimicMessage(SocketUserMessage message, bool allowGIF = true, bool logToConsole = true)
        {
            string newMessageText = null;

            string[] messageWords = message?.Content?.Split(' ');
            var validUsers = AllUserData.Where(x => x.Value.TrackMe && x.Value.unigramMatrix.GetColumn("all").Sum() > 100);

            if (validUsers.Any())
            {
                _markovSemaphore.WaitOne();
                var kvp = validUsers.ElementAt(r.Next(validUsers.Count()));

                CustomUserData userToGenerate = kvp.Value;

                bool useLunasModel = r.NextDouble() < 0.5;
                bool useTopic = r.NextDouble() < 0.70;
                bool didUseGIF = false;

                if (useLunasModel)
                    userToGenerate = LunasUser;

                MoodProfile generateMood = moodProfile;
                if (message != null && AllUserData.TryGetValue(message.Author.Id, out CustomUserData userData) && r.NextDouble() < 0.5)
                {
                    generateMood = userData.mood;
                }

                //if (useTopic && message != null && messageWords != null && messageWords.Length > 0)
                //{
                //    string topic = messageWords[r.Next(messageWords.Length)];
                //    if (markov.Order > 0 && markov.Order < message.Content.Length)
                //    {
                //        int rIndex = r.Next(message.Content.Length - markov.Order);
                //        topic = message.Content.Substring(rIndex, markov.Order);
                //    }
                //    newMessageText = markov.GenerateSequenceMiddleOut(topic, r, /*r.Next(25, 180)*/1000);
                //    if (allowGIF && ((string.IsNullOrWhiteSpace(newMessageText) && r.NextDouble() < 0.5) || r.NextDouble() < 0.02))
                //    {
                //        newMessageText = await GetGIFLink(topic);
                //        didUseGIF = true;
                //    }
                //}
                //if (string.IsNullOrWhiteSpace(newMessageText))
                //{
                //    didUseGIF = false;
                //    newMessageText = markov.GenerateSequence(generateMood, r, /*r.Next(25, 180)*/1000, (x) => GetMood(x, false));
                //}
                if (string.IsNullOrWhiteSpace(newMessageText)) // mood failed ?!
                {
                    didUseGIF = false;
                    newMessageText = MarkovGenerator.GenerateWithBackoff(r, userToGenerate.unigramMatrix, userToGenerate.bigramMatrix, userToGenerate.trigramMatrix, MarkovGenerator.StupidBackoff);
                    //newMessageText = markov.GenerateSequence(r, /*r.Next(25, 180)*/1000);
                }

                if (!string.IsNullOrWhiteSpace(newMessageText) && !didUseGIF)
                {
                    newMessageText = BasicTokenizer.ReplaceVariables(newMessageText, r, new List<string>{message.Author.Mention}, userToGenerate.linkList);
                }

                if (logToConsole)
                {
                    Console.WriteLine();
                    if (_symSpell != null)
                    {
                        List<SymSpell.SuggestItem> suggestions = _symSpell.LookupCompound(newMessageText, 2);
                        foreach (SymSpell.SuggestItem s in suggestions)
                        {
                            Console.WriteLine($"{s.term} | {s.distance} | {s.count}");
                        }
                    }

                    CacheUsername(kvp.Key);
                    string msgType = (didUseGIF ? "GIF" : (useLunasModel ? "luna" : "stupidBackoff"));
                    Console.WriteLine($"{(useLunasModel ? "" : kvp.Key.ToString() + $"[{usernameCache.GetValueOrDefault(kvp.Key, "")}]")} {msgType} | {newMessageText}");
                    Console.WriteLine();
                }
                _markovSemaphore.Release();
            }

            return newMessageText;
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
                var matrixRow = moodSentimentMatrix.GetRow(word);
                if (matrixRow != null)
                {
                    foreach (string header in matrixRow.GetColumnKeys())
                    {
                        float value = matrixRow[header];
                        if (header.StartsWith("andbrain_"))
                        {
                            totalEmotion[header.Substring("andbrain_".Length)] += value;
                            emotionCount++;
                        }
                        else if (header == "hedonometer_Happiness Score")
                        {
                            totalHappiness.happiness += value;
                            hedonometerCount++;
                        }
                        else if (header == "military")
                        {
                            nukeyness += value;
                        }
                    }
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
                Console.WriteLine($"prediction: {emotionClassifier.Predict(BasicTokenizer.Tokenize(sentence), 2)}");
            }
            return result;
        }

        private void LogMimicData(ulong userID, ulong messageID, string message, DateTimeOffset? time)
        {
            _rwSemaphore.WaitOne();

            if (!AllUserData.TryGetValue(userID, out CustomUserData userData))
            {
                userData = AllUserData[userID] = new CustomUserData(userID);
                userData.LoadDataAsync(Path.Combine(UsersDirectory, userID.ToString())).Wait();
            }

            _rwSemaphore.Release();

            if (userData.TrackMe)
            {
                messageCache.TryAdd(messageID, (userID, message, time));
                if (printLogMessage)
                {
                    Console.WriteLine($"{userID}[{usernameCache[userID]}] {message}");
                }
            }
        }

        public async Task HandleMessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage message, ISocketMessageChannel channel)
        {
            if (AllUserData.TryGetValue(message.Author.Id, out CustomUserData userData) && userData.TrackMe)
            {
                string processedMessage = PreProcessUserMessage(message.Content, message.MentionedUsers);
                messageEditQueue.Enqueue((message.Author.Id, message.Id, processedMessage, message.EditedTimestamp));
                usernameCache.TryGetValue(message.Author.Id, out string username);
                Console.WriteLine($"{message.Author.Id}[{username ?? ""}] {processedMessage} [EDITED]");
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
            else
            {

                if (message.Author.Id != _client.CurrentUser.Id && reaction.UserId != _client.CurrentUser.Id && reaction.Emote.Equals(new Emoji("\u2754"))) // ❔
                {
                    // WIKI LOOKUP
                    string wikiLookupTerm = WikiLookupTerm(message);
                    if (!string.IsNullOrEmpty(wikiLookupTerm))
                    {
                        List<WikiMarkupParser> results = await WikiLookup(wikiLookupTerm);

                        foreach (var result in results)
                        {
                            string description = result.GetHeader("short description")?.headerValue;

                            if (string.IsNullOrEmpty(description) && result.ContentCount > 0)
                            {
                                description = WikiMarkupParser.Format(result[0]);
                            }

                            if (!string.IsNullOrEmpty(description) && !description.StartsWith('#'))
                            {
                                await channel.SendMessageAsync(description);
                            }
                        }
                    }
                }

            }
        }


        private async Task<string> GetGIFLink(string searchTerm)
        {
            int limit = 40;
            string url = "https://" + $"api.tenor.com/v1/search?q={searchTerm}&key={Environment.GetEnvironmentVariable("TENOR_GIF_API_KEY", EnvironmentVariableTarget.User)}&limit={limit}";

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);

                Stream s = await response.Content.ReadAsStreamAsync();

                JObject jObject = await JObject.LoadAsync(new JsonTextReader(new StreamReader(s)));
                //Console.WriteLine(jObject.ToString());

                JArray results = jObject?["results"] as JArray;
                if (results.Count == 0) return null;
                JObject rand = results?[r.Next(results.Count)] as JObject;

                return (string) rand?["url"];
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine(e.Message);
                return null;
            }
        }
        public static async Task<List<WikiMarkupParser>> WikiLookup(string lookup)
        {
            List<WikiMarkupParser> results = new List<WikiMarkupParser>();

            string url = "https://" + $"en.wikipedia.org/w/api.php?action=query&prop=revisions&rvprop=content&rvsection=0&rvslots=*&format=json&titles={lookup}";
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);

                ILoggerFactory loggerFactory = new LoggerFactory();
                ILogger logger = loggerFactory.CreateLogger("wiki_log");
                CancellationToken cancellationToken = new CancellationToken();

                JToken token = await MediaWikiJsonResponseParser.Default.ParseResponseAsync(response, new WikiResponseParsingContext(logger, cancellationToken));
                Console.WriteLine(token.ToString());

                JToken pages = token["query"]?["pages"];
                foreach (JProperty page in pages)
                {
                    JArray revisions = (page.Value["revisions"] as JArray);
                    if (revisions != null)
                    {
                        foreach (JToken rev in revisions)
                        {
                            JToken main = rev["slots"]?["main"];
                            Console.WriteLine(main["contentformat"]);
                            Console.WriteLine(main["*"]);

                            WikiMarkupParser wikiMarkupParser = new WikiMarkupParser();
                            wikiMarkupParser.Load(main["*"].Value<string>());
                            results.Add(wikiMarkupParser);
                        }
                    }
                }
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine(e.Message);
            }

            return results;
        }

        public async Task<string> CalculateWordMath(SocketMessage message, string input)
        {
            string processedInput = input;

            if (message.MentionedUsers != null)
            {
                foreach (IUser u in message.MentionedUsers)
                {
                    if (u != null)
                    {
                        CacheUsername(u.Id, u.Username);
                        if (!u.IsBot)
                        {
                            Regex userIDRegex = new Regex($"<@(|!|&){u.Id}>");
                            processedInput = userIDRegex.Replace(processedInput, u.Id.ToString());
                        }
                    }
                }
            }

            processedInput = PreProcessUserMessage(input, null).ToLowerInvariant();

            Regex splitRegex = new Regex(@"(\(|\))| ");
            string[] tokens = splitRegex.Split(processedInput);//processedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            Console.WriteLine(string.Join(" ", tokens));

            // check for special math
            {
                int matchIndex = -1;
                for (int i = 0; i < nicknameMathLHS.Count; i++)
                {
                    var lhs = nicknameMathLHS[i];

                    bool matchForward = true;
                    bool matchBackward = true;
                    for (int j = 0; j < lhs.Count; j++)
                    {
                        if (tokens.Length != lhs.Count)
                        {
                            matchForward = false;
                            matchBackward = false;
                            break;
                        }
                        if (tokens[j] != lhs[j] && (!nicknameMap.TryGetValue(tokens[j], out ulong id) || id.ToString() != lhs[j]))
                        {
                            matchForward = false;
                        }
                        if (tokens[j] != lhs[lhs.Count-1-j] && (!nicknameMap.TryGetValue(tokens[j], out id) || id.ToString() != lhs[lhs.Count - 1 - j]))
                        {
                            matchBackward = false;
                        }
                    }

                    if (matchForward || matchBackward)
                    {
                        matchIndex = i;
                        break;
                    }
                }

                if (matchIndex != -1)
                {
                    string randomResult = nicknameMathRHS[matchIndex][r.Next(nicknameMathRHS[matchIndex].Count)];
                    if (!randomResult.Contains("::/") && r.NextDouble() < 0.08)
                    {
                        randomResult = await GetGIFLink(randomResult);
                    }

                    return randomResult;
                }
            }

            Stack<string> operationStack = new Stack<string>();
            Stack<(float, DenseVector)> valueStack = new Stack<(float, DenseVector)>();
            int parensDepth = 0;

            void DoTopOperation()
            {
                if (valueStack.Count == operationStack.Count + 1 && valueStack.Count > 1 + parensDepth)
                {
                    // use operation with this vector and the top of the stack
                    var (topValue, topVector) = valueStack.Pop();
                    var (secondValue, secondVector) = valueStack.Pop();
                    string op = operationStack.Pop();

                    if (op == "+" || op == "-")
                    {
                        if (op == "+")
                        {
                            if (secondVector != null && topVector != null)
                            {
                                secondVector += topVector;
                            }
                            else
                            {
                                secondVector = null;
                            }
                            secondValue += topValue;
                        }
                        else if (op == "-")
                        {
                            if (secondVector != null && topVector != null)
                            {
                                secondVector -= topVector;
                            }
                            else
                            {
                                secondVector = null;
                            }
                            secondValue -= topValue;
                        }
                    }
                    else if (op == "*" || op == "/")
                    {
                        if (op == "*")
                        {
                            if (secondVector != null) secondVector *= topValue;
                            secondValue *= topValue;
                        }
                        else if (op == "/")
                        {
                            if (secondVector != null) secondVector /= topValue;
                            secondValue /= topValue;
                        }
                    }

                    valueStack.Push((secondValue, secondVector));
                }
            }

            foreach (string token in tokens)
            {
                if (token == "+" || token == "-" || token == "*" || token == "/")
                {
                    if (valueStack.Count <= operationStack.Count)
                    {
                        return $"invalid format, word-vector must appear before {token} operation";
                    }
                    else
                    {
                        operationStack.Push(token);
                    }
                }
                else if (token == "(")
                {
                    parensDepth++;
                }
                else if (token == ")")
                {
                    if (parensDepth == 0)
                    {
                        return "too many closing parenthesis!!";
                    }
                    parensDepth--;
                    DoTopOperation();
                }
                else if (float.TryParse(token, out float floatValue))
                {
                    // push scalar then try to do operation
                    valueStack.Push((floatValue, null));
                    DoTopOperation();
                }
                else if (gloveEmbeddings.TryGetValue(token, out var vector))
                {
                    // push vector then try to do operation
                    valueStack.Push((float.NaN, DenseVector.OfVector(vector)));
                    DoTopOperation();
                }
            }

            if (parensDepth != 0 || valueStack.Count != 1)
            {
                return "too many opening parenthesis!";
            }

            var (resultValue, resultVector) = valueStack.Pop();

            if (resultVector == null)
            {
                return resultValue.ToString();
            }
            else
            {
                string nearestGram = "";
                DenseVector nearestVector = null;
                float nearestDist = float.PositiveInfinity;
                foreach (var (gram, vector) in gloveEmbeddings)
                {
                    if (!tokens.Contains(gram)) // avoid words in the original equation
                    {
                        float dist = MathNet.Numerics.Distance.SSD(resultVector.Values, vector.Values);
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            nearestGram = gram;
                            nearestVector = vector;
                        }
                    }
                }

                Console.WriteLine($"{nearestGram} {nearestDist}\n{(nearestVector?.ToString() ?? "null")}");
                return nearestGram;
            }
        }
    }
}
