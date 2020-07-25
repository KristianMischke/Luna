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
    interface IReactionGameHandler
    {
        string GameName { get; }
        IEmote[] GetReactions();

        /// <summary>
        ///     Submit the action to the game handler, returns true if need to re-render the board
        /// </summary>
        /// <param name="emote"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        bool SubitMove(IEmote emote, ulong userId);
        string GetBoard();
    }

    class GameCommandHandler : ICustomCommandHandler
    {
        public static GameCommandHandler _instance;

        public const string userTrackFile = "trackUsers.txt";

        private readonly DiscordSocketClient _client;

        private Dictionary<ulong, IReactionGameHandler> _reactionGames;

        TicTacToe tictactoe;

        public GameCommandHandler(DiscordSocketClient client)
        {
            _client = client;
            _instance = this;

            tictactoe = new TicTacToe();
            _reactionGames = new Dictionary<ulong, IReactionGameHandler>();
        }

        public async Task SetupAsync()
        {
            return;
        }

        public void Cleanup()
        {
            
        }

        public async Task HandleMessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage message, ISocketMessageChannel channel) { }

        public async Task HandleUserMessageAsync(SocketUserMessage message)
        {
            if (message.Author.Id != _client.CurrentUser.Id
                && !string.IsNullOrEmpty(message.Content)
                && message.Content.StartsWith('#'))
            {
                if (message.Content.Contains("play") || message.Content.Contains("start"))
                {
                    if (message.Content.Contains("tictactoe"))
                    {
                        SocketUser other = message.MentionedUsers.Count > 0 ? message.MentionedUsers.First() : null;
                        bool singlePlayerFirst = message.Content.Contains(" X");

                        TicTacToeHandler newGame = other != null ? new TicTacToeHandler(message.Author.Id, other.Id, message.Author.Mention, other.Mention) : new TicTacToeHandler(message.Author.Id, message.Author.Mention, singlePlayerFirst);

                        var context = new SocketCommandContext(_client, message);
                        RestUserMessage gameStateMsg = await context.Channel.SendMessageAsync(newGame.GetBoard());
                        _reactionGames.Add(gameStateMsg.Id, newGame);
                        Console.WriteLine(gameStateMsg.Id);
                        _ = Task.Run(() => gameStateMsg.AddReactionsAsync(newGame.GetReactions()));
                    }
                    else if (message.Content.Contains("connect4"))
                    {
                        SocketUser other = message.MentionedUsers.Count > 0 ? message.MentionedUsers.First() : null;
                        bool singlePlayerFirst = message.Content.Contains(" X");

                        Connect4Handler newGame = other != null ? new Connect4Handler(message.Author.Id, other.Id, message.Author.Mention, other.Mention) : new Connect4Handler(message.Author.Id, message.Author.Mention, singlePlayerFirst);

                        var context = new SocketCommandContext(_client, message);
                        RestUserMessage gameStateMsg = await context.Channel.SendMessageAsync(newGame.GetBoard());
                        _reactionGames.Add(gameStateMsg.Id, newGame);
                        Console.WriteLine(gameStateMsg.Id);
                        _ = Task.Run(() => gameStateMsg.AddReactionsAsync(newGame.GetReactions()));
                    }
                }
                /*
                string cmd = message.Content.EndsWith("\n") ? message.Content : message.Content + "\n";
                cmd = cmd.Substring(1);
                tictactoe.WriteMessage(cmd);

                var context = new SocketCommandContext(_client, message);
                await context.Channel.SendMessageAsync(tictactoe.ReadMessage());
                */
            }
        }

        public async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> before, ISocketMessageChannel channel, SocketReaction reaction)
        {
            var message = await before.GetOrDownloadAsync();
            if (_reactionGames.TryGetValue(message.Id, out IReactionGameHandler game) && reaction.UserId != _client.CurrentUser.Id)
            {
                Console.WriteLine(message.Id);
                if (game.SubitMove(reaction.Emote, reaction.UserId))
                {
                    try { await message.RemoveAllReactionsAsync(); } catch { };
                    await message.ModifyAsync(x => x.Content = game.GetBoard());
                    try { await message.AddReactionsAsync(game.GetReactions()); } catch { };
                }
            }
        }
    }
}
