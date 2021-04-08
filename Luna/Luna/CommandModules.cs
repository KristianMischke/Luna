using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Luna.Sentiment;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LingK;

namespace Luna
{
    
    public class MimicModule : ModuleBase<SocketCommandContext>
    {
        Random r = new Random();

        [Command("mimic")]
        [Summary("Mimicry is the best form of flattery")]
        //[Alias("user", "whois")]
        public async Task MimicUserAsync(
            [Summary("The (optional) user to mimic")]
            SocketUser user = null)
        {
            var userInfo = user ?? Context.Client.CurrentUser;

            if (!MimicCommandHandler._instance.GetConsentualUser(userInfo.Id, out CustomUserData userData))
            {
                userData = MimicCommandHandler._instance.LunasUser;
            }

            string response = MarkovGenerator.GenerateWithBackoff(r, userData.unigramMatrix, userData.bigramMatrix, userData.trigramMatrix, MarkovGenerator.StupidBackoff);
            if (!string.IsNullOrEmpty(response))
            {
                await ReplyAsync();
            }
        }

        [Command("math")]
        public async Task WordMath([Remainder] string input)
        {
            await ReplyAsync(await MimicCommandHandler._instance.CalculateWordMath(Context.Message, input));
        }

        [Command("color")]
        public async Task RandomColor()
        {
            string[] items = "red green blue orange white yellow".Split(' ');
            await ReplyAsync(items[r.Next(items.Length)]);
        }

        [Command("lookup")]
        public async Task Lookup([Remainder]string lookup)
        {
            List<WikiMarkupParser> results = await MimicCommandHandler.WikiLookup(lookup);

            EmbedBuilder embedBuilder = new EmbedBuilder();
            foreach (var result in results)
            {
                if (result.ContentCount > 0)
                {
                    StringBuilder builder = new StringBuilder();
                    for (int i = 0; i < result.ContentCount; i++)
                    {
                        builder.AppendLine(WikiMarkupParser.Format(result[i], "https://en.wikipedia.org/wiki/", "[{{TEXT}}]({{LINK}})")).AppendLine();
                    }
                    string description = builder.ToString();
                    if (description.Length > 1024)
                    {
                        description = description.Substring(0, 1021) + "...";
                    }
                    embedBuilder.AddField("Wikipedia", $"[link](https://en.wikipedia.org/wiki/{lookup.Replace(" ", "_")})");
                    embedBuilder.AddField(lookup, description);
                }
            }

            await ReplyAsync(embed: embedBuilder.Build());
        }

        //[Command("topic")]
        //public async Task TopicTalkAsync(string word,
        //    [Summary("The (optional) user to mimic")]
        //    SocketUser user = null)
        //{
        //    var userInfo = user ?? Context.Client.CurrentUser;
        //
        //    MarkovChain markov = MimicCommandHandler._instance.movieScriptMarkov;
        //    if (MimicCommandHandler._instance.GetConsentualUser(userInfo.Id, out CustomUserData userData))
        //    {
        //        markov = userData.wordChain;
        //    }
        //
        //    string message = markov.GenerateSequenceMiddleOut(word, r, r.Next(25, 180));
        //    if (string.IsNullOrEmpty(message))
        //    {
        //        await ReplyAsync($"Oh no, I could not generate with: {word}");
        //    }
        //    else
        //    {
        //        message = markov.ReplaceVariables(message, r, new List<string> {Context.User.Mention});
        //        await ReplyAsync(message);
        //    }
        //}

        [Command("pmi")]
        public async Task PMI(string w1, string w2, float lambda = 0)
        {
            PMICalculator pmiCalc = MimicCommandHandler._instance.LunasUser.pmiCalc;
            await ReplyAsync(pmiCalc.PMILambda(w1, w2, lambda).ToString());
            await ReplyAsync($"Math.Log({pmiCalc.PLambda(w1, w2, lambda)} / ({pmiCalc.PLambda(w1, lambda)} * {pmiCalc.PLambda(w2, lambda)}), 2)");
        }

        [Command("important")]
        public async Task FindImportantMessages(int window = 50, int num = 3)
        {
            if (num > 10 || (window > 1000 && Context.User.Id != 295009962709614593ul))
            {
                await Context.Message.AddReactionAsync(new Emoji("❌"));
                return;
            }

            CustomUserData userData = MimicCommandHandler._instance.LunasUser;

            List<(float, string, IMessage)> pmiMessages = new List<(float, string, IMessage)>();

            var asyncEnumerator = Context.Channel.GetMessagesAsync(window).GetAsyncEnumerator();
            while (await asyncEnumerator.MoveNextAsync())
            {
                foreach (IMessage message in asyncEnumerator.Current)
                {
                    if (!string.IsNullOrEmpty(message.Content) && !message.Author.IsBot
                        && !message.Content.StartsWith('~')
                        && !message.Content.StartsWith('!')
                        && !message.Content.StartsWith('#')
                        && !message.Content.StartsWith('?'))
                    {
                        List<IUser> mentionedUsers = new List<IUser>();
                        foreach (ulong uID in message.MentionedUserIds)
                        {
                            mentionedUsers.Add(await Context.Channel.GetUserAsync(uID));
                        }

                        string messageContent = MimicCommandHandler._instance.PreProcessUserMessage(message.Content, mentionedUsers);
                        
                        List<string> tokens = BasicTokenizer.Tokenize(messageContent);
                        List<string> words = tokens.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

                        float highestPMI = float.NegativeInfinity;
                        string w1 = "";
                        string w2 = "";
                        for (int i = 0; i < words.Count - 1; i++)
                        {
                            for (int j = i + 1; j < words.Count; j++)
                            {
                                float pmi = MimicCommandHandler._instance.LunasUser.pmiCalc.PMI(words[i], words[j]);
                                if (pmi > highestPMI)
                                {
                                    highestPMI = pmi;
                                    w1 = words[i];
                                    w2 = words[j];
                                }
                            }
                        }

                        if (highestPMI > float.NegativeInfinity)
                        {
                            pmiMessages.Add((highestPMI, messageContent.Replace(w1, "**" + w1 + "**").Replace(w2, "**" + w2 + "**"), message));
                        }
                    }
                }
            }
            
            List<(float, string, IMessage)> sortedList = pmiMessages.OrderByDescending(x => x.Item1).ToList();
            StringBuilder responseBuilder = new StringBuilder();
            EmbedBuilder embedBuilder = new EmbedBuilder();
            for (int i = 0; i < num && i < sortedList.Count; i++)
            {
                responseBuilder.Append(sortedList[i].Item1.ToString()).Append(" - ").Append(sortedList[i].Item3).AppendLine();
                responseBuilder.Append("```").Append(sortedList[i].Item2).Append("```").AppendLine().AppendLine();

                embedBuilder.AddField("pmi", sortedList[i].Item1.ToString(), true);
                embedBuilder.AddField("link", $"[goto;]({sortedList[i].Item3.GetJumpUrl()})", true);
                embedBuilder.AddField("msg", sortedList[i].Item2);
            }

            //await ReplyAsync(responseBuilder.ToString());
            await ReplyAsync(embed: embedBuilder.Build());
        }

        [Command("count")]
        public async Task CountGrams(SocketUser user, [Remainder] string input)
        {
            StringBuilder responseBuilder = new StringBuilder();

            CustomUserData userData = MimicCommandHandler._instance.LunasUser;
            if (user != null && user.Id != Context.Client.CurrentUser.Id && !MimicCommandHandler._instance.GetConsentualUser(user.Id, out userData))
            {
                responseBuilder.Append($"Oh no, I count not find {user?.Username + "'s" ?? "their"} stats");
            }
            else
            {
                string[] split = input.Split(' ');

                if (split.Length == 3)
                {
                    responseBuilder.Append(userData.trigramMatrix[(split[0], split[1], split[2]), "all"]);
                }
                else if (split.Length == 2)
                {
                    responseBuilder.Append(userData.bigramMatrix[(split[0], split[1]), "all"]);
                }
                else if (split.Length == 1)
                {
                    responseBuilder.Append(userData.unigramMatrix[split[0], "all"]);
                }
                else
                {
                    responseBuilder.Append($"Unable to find data for {input}");
                }
            }

            await ReplyAsync(responseBuilder.ToString());
        }
        

        [Command("stats")]
        public async Task UserStats(SocketUser user = null, string type = null, int num = 5, float lambda = 0f, float max = float.PositiveInfinity)
        {
            StringBuilder responseBuilder = new StringBuilder();

            CustomUserData userData = MimicCommandHandler._instance.LunasUser;
            if (user != null && user.Id != Context.Client.CurrentUser.Id && !MimicCommandHandler._instance.GetConsentualUser(user.Id, out userData))
            {
                responseBuilder.Append($"Oh no, I count not find {user?.Username + "'s" ?? "their"} stats");
            }
            else
            {
                if (type == "tri")
                {
                    var e = userData.trigramMatrix.GetColumn("all").OrderByDescending(kvp => kvp.Value).GetEnumerator();
                    int i = 0;
                    while (i++ < num && e.MoveNext())
                    {
                        responseBuilder.Append(e.Current.Key).Append(" - ").Append(e.Current.Value);
                        responseBuilder.AppendLine();
                    }
                }
                else if (type == "bi")
                {
                    var e = userData.bigramMatrix.GetColumn("all").OrderByDescending(kvp => kvp.Value).GetEnumerator();
                    int i = 0;
                    while (i++ < num && e.MoveNext())
                    {
                        responseBuilder.Append(e.Current.Key).Append(" - ").Append(e.Current.Value);
                        responseBuilder.AppendLine();
                    }
                }
                else if (type == "pmi")
                {
                    var e = userData.bigramMatrix.GetColumn("pmi_sentence").Select(kvp => (kvp.Key, userData.pmiCalc.PMILambda(kvp.Key.Item1, kvp.Key.Item2, lambda))).Where(kvp => kvp.Item2 < max).OrderByDescending(x => x.Item2).GetEnumerator();
                    int i = 0;
                    while (i++ < num && e.MoveNext())
                    {
                        responseBuilder.Append(e.Current.Key).Append(" - ").Append(e.Current.Item2);
                        responseBuilder.AppendLine();
                    }
                }
                else
                {
                    var e = userData.unigramMatrix.GetColumn("all").OrderByDescending(kvp => kvp.Value).GetEnumerator();
                    int i = 0;
                    while (i++ < num && e.MoveNext())
                    {
                        responseBuilder.Append(e.Current.Key).Append(" - ").Append(e.Current.Value);
                        responseBuilder.AppendLine();
                    }
                }
            }

            await ReplyAsync(responseBuilder.ToString());
        }

        [Command("saveMimics", true)]
        [Summary("Save me now")]
        public async Task SaveMimicDataAsync()
        {
            MimicCommandHandler._instance.SaveMimicData();
            await ReplyAsync("Saved!");
        }

        [Command("markMood", true)]
        [Summary("toggle marking mood")]
        public async Task StartMarkingMoodAsync()
        {
            MimicCommandHandler._instance.markMood = !MimicCommandHandler._instance.markMood;
            await Context.Message.AddReactionAsync(new Emoji("👍"));
        }

        [Command("debugMood", true)]
        [Summary("")]
        public async Task DebugMoodAsync(SocketUser user)
        {
            MoodProfile selectedMood;
            if (CommandManager._instance.AllUserData.TryGetValue(user.Id, out CustomUserData userData))
            {
                selectedMood = userData.mood;
            }
            else if (user.Id == CommandManager._instance.ClientID)
            {
                selectedMood = MimicCommandHandler._instance.moodProfile;
            }
            else
            {
                await ReplyAsync($"{user.Username} is not in my system");
                return;
            }

            IUserMessage reply = await ReplyAsync($"{selectedMood.ToString()}");

            string primaryMood = selectedMood.GetPrimaryMood();
            List<IEmote> emotes = MimicCommandHandler._instance.moodEmoji[primaryMood];
            await reply.AddReactionAsync(emotes[r.Next(emotes.Count)]);
        }

        [Command("clearMe", true)]
        [Summary("Bye bye data")]
        public async Task ClearMimicDataAsync()
        {
            if (CommandManager._instance.AllUserData.TryGetValue(Context.User.Id, out CustomUserData userData))
            {
                userData.ClearData();
                await ReplyAsync("Done!");
            }
            else
            {
                await ReplyAsync($"{Context.User.Mention}, you are not in my system");
            }
        }

        [Command("avatar_num", true)]
        public async Task AvatarNum()
        {
            await ReplyAsync(MimicCommandHandler._instance.GetNumHoursForAvatar().ToString());
        }
        [Command("avatar", true)]
        public async Task Avatar()
        {
            await Context.Channel.SendFileAsync(MimicCommandHandler._instance.GetAvatar(MimicCommandHandler._instance.GetNumHoursForAvatar()));
        }

        [Command("succCorpses", true)]
        public async Task SpecialCommand1()
        {
            if (Context.User.Id == 295009962709614593ul)
            {
                await MimicCommandHandler._instance.SaveOldMessages(Context);
            }
            else
            {
                await ReplyAsync($"{Context.User.Mention}, you are not granted access to this command");
            }
        }
        [Command("absorbSoles", true)]
        public async Task SpecialCommand2()
        {
            if (Context.User.Id == 295009962709614593ul)
            {
                await MimicCommandHandler._instance.InputOldMessages(Context);
            }
            else
            {
                await ReplyAsync($"{Context.User.Mention}, you are not granted access to this command");
            }
        }

        [Command("ignoreMe", true)]
        [Summary("ignore my messages for now")]
        public async Task IgnoreMimicDataAsync()
        {
            if (CommandManager._instance.AllUserData.TryGetValue(Context.User.Id, out CustomUserData userData))
            {
                userData.TrackMe = false;
                await ReplyAsync($"Ingoring {Context.User.Mention}");
            }
            else
            {
                await ReplyAsync($"{Context.User.Mention}, you are not in my system");
            }
        }

        [Command("trackMe", true)]
        [Summary("track my messages for now")]
        public async Task TrackMimicDataAsync()
        {
            if (CommandManager._instance.AllUserData.TryGetValue(Context.User.Id, out CustomUserData userData))
            {
                userData.TrackMe = true;
                await ReplyAsync($"Tracking {Context.User.Mention}");
            }
            else
            {
                userData = new CustomUserData(Context.User.Id);
                userData.TrackMe = true;
                CommandManager._instance.AllUserData.Add(Context.User.Id, userData);

                await ReplyAsync($"{Context.User.Mention}, added you to my system");
            }
        }
    }
}
