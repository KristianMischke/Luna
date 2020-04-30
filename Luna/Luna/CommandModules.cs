using Discord.Commands;
using Discord.WebSocket;
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
                await ReplyAsync(userData.wordChain.GenerateSequence(r, r.Next(10, 80), true));
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
                await ReplyAsync(userData.nGramChain.GenerateSequence(r, r.Next(10, 80), false));
            }
            else
            {
                await ReplyAsync($"Oh no, I could not mimic {user?.Username ?? "them"}");
            }
        }

        [Command("saveMimics", true)]
        [Summary("Save me now")]
        public async Task SaveMimicDataAsync()
        {
            MimicCommandHandler._instance.SaveMimicData();
            await ReplyAsync("Saved!");
        }

        [Command("clearMe", true)]
        [Summary("Bye bye data")]
        public async Task ClearMimicDataAsync()
        {
            if (CommandManager._instance.AllUserData.TryGetValue(Context.User.Id, out CustomUserData userData))
            {
                userData.nGramChain.ClearData();
                userData.wordChain.ClearData();
                await ReplyAsync("Done!");
            }
            else
            {
                await ReplyAsync($"{Context.User.Mention}, you are not in my system");
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
