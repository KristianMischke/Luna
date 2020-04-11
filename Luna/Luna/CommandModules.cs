﻿using Discord.Commands;
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

            if (CommandHandler._instance.PlayerMarkovData.TryGetValue(userInfo.Id, out PlayerMarkovData playerData))
            {
                await ReplyAsync(playerData.wordChain.GenerateSequence(r, r.Next(10, 80), true));
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

            if (CommandHandler._instance.PlayerMarkovData.TryGetValue(userInfo.Id, out PlayerMarkovData playerData))
            {
                await ReplyAsync(playerData.nGramChain.GenerateSequence(r, r.Next(10, 80), true));
            }
            else
            {
                await ReplyAsync($"Oh no, I could not mimic {user?.Username ?? "them"}");
            }
        }
    }
}
