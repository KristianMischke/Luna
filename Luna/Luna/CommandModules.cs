using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Luna.Sentiment;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

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

            if (MimicCommandHandler._instance.GetConsentualUser(userInfo.Id, out CustomUserData userData))
            {
                await ReplyAsync(userData.wordChain.GenerateSequence(r, r.Next(25, 180)));
            }
            else
            {
                await ReplyAsync($"Oh no, I could not mimic {user?.Username ?? "them"}");
            }
        }

        [Command("mimic2")]
        [Summary("Mimicry is the best form of flattery")]
        //[Alias("user", "whois")]
        public async Task Mimic2UserAsync(
            [Summary("The (optional) user to mimic")]
            SocketUser user = null)
        {
            var userInfo = user ?? Context.Client.CurrentUser;

            if (MimicCommandHandler._instance.GetConsentualUser(userInfo.Id, out CustomUserData userData))
            {
                await ReplyAsync(userData.nGramChain.GenerateSequence(r, r.Next(25, 180)));
            }
            else
            {
                await ReplyAsync($"Oh no, I could not mimic {user?.Username ?? "them"}");
            }
        }

        [Command("mimic3")]
        [Summary("Mimicry is the best form of flattery")]
        //[Alias("user", "whois")]
        public async Task Mimic3UserAsync(
            [Summary("The (optional) user to mimic")]
            SocketUser user = null)
        {
            var userInfo = user ?? Context.Client.CurrentUser;

            if (MimicCommandHandler._instance.GetConsentualUser(userInfo.Id, out CustomUserData userData))
            {
                await ReplyAsync(userData.doubleWordChain.GenerateSequence(r, r.Next(25, 180)));
            }
            else
            {
                await ReplyAsync($"Oh no, I could not mimic {user?.Username ?? "them"}");
            }
        }

        [Command("topic")]
        public async Task TopicTalkAsync(string word,
            [Summary("The (optional) user to mimic")]
            SocketUser user = null)
        {
            var userInfo = user ?? Context.Client.CurrentUser;

            MarkovChain markov = MimicCommandHandler._instance.movieScriptMarkov;
            if (MimicCommandHandler._instance.GetConsentualUser(userInfo.Id, out CustomUserData userData))
            {
                markov = userData.wordChain;
            }

            string message = markov.GenerateSequenceMiddleOut(word, r, r.Next(25, 180));
            if (string.IsNullOrEmpty(message))
            {
                await ReplyAsync($"Oh no, I could not generate with: {word}");
            }
            else
            {
                message = markov.ReplaceVariables(message, r, new List<string> {Context.User.Mention});
                await ReplyAsync(message);
            }
        }

        [Command("topic3")]
        public async Task Topic3TalkAsync(string word,
            [Summary("The (optional) user to mimic")]
            SocketUser user = null)
        {
            var userInfo = user ?? Context.Client.CurrentUser;

            MarkovChain markov = MimicCommandHandler._instance.movieScriptMarkov;
            if (MimicCommandHandler._instance.GetConsentualUser(userInfo.Id, out CustomUserData userData))
            {
                markov = userData.doubleWordChain;
            }

            string message = markov.GenerateSequenceMiddleOut(word, r, r.Next(25, 180));
            if (string.IsNullOrEmpty(message))
            {
                await ReplyAsync($"Oh no, I could not generate with: {word}");
            }
            else
            {
                message = markov.ReplaceVariables(message, r, new List<string> { Context.User.Mention });
                await ReplyAsync(message);
            }
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
                userData.nGramChain.ClearData();
                userData.wordChain.ClearData();
                userData.doubleWordChain.ClearData();
                await ReplyAsync("Done!");
            }
            else
            {
                await ReplyAsync($"{Context.User.Mention}, you are not in my system");
            }
        }

        [Command("specialCommand", true)]
        public async Task SpecialCommand()
        {
            if (Context.User.Id == 295009962709614593ul)
            {
                await MimicCommandHandler._instance.InputOldMessages(Context);
                //await MimicCommandHandler._instance.SaveOldMessages(Context);
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
