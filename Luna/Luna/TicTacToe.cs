using System;
using System.Collections.Generic;
using System.Text;
using Discord;

namespace Luna
{
    class TicTacToeHandler : IReactionGameHandler
    {
        private string OH = char.ConvertFromUtf32(0x2B55);
        private string EX = char.ConvertFromUtf32(0x274C);
        private string BLANK = char.ConvertFromUtf32(0x2B1C);

        private TicTacToe game;
        private IEmote[] actions = new IEmote[]
        {
            new Emoji("\u0030\uFE0F\u20E3"), // 0
            new Emoji("\u0031\uFE0F\u20E3"), // 1
            new Emoji("\u0032\uFE0F\u20E3"), // 2

            new Emoji("\u0033\uFE0F\u20E3"), // 3
            new Emoji("\u0034\uFE0F\u20E3"), // 4
            new Emoji("\u0035\uFE0F\u20E3"), // 5

            new Emoji("\u0036\uFE0F\u20E3"), // 6
            new Emoji("\u0037\uFE0F\u20E3"), // 7
            new Emoji("\u0038\uFE0F\u20E3"), // 8
        };

        ulong player1;
        ulong player2;

        string p1Name;
        string p2Name;

        bool cpuGame;

        bool isP1Turn;

        public TicTacToeHandler(ulong player1, string p1Name, bool playerFirst)
        {
            game = new TicTacToe();

            this.player1 = player1;
            this.player2 = 0;

            this.p1Name = p1Name;
            this.p2Name = "CPU";

            cpuGame = true;
            game.WriteMessage($"00 {(playerFirst ? "X" : "O")}\n");

            if (!playerFirst)
            {
                game.WriteMessage("03\n");
            }
            isP1Turn = true;
        }

        public TicTacToeHandler(ulong player1, ulong player2, string p1Name, string p2Name)
        {
            game = new TicTacToe();

            this.player1 = player1;
            this.player2 = player2;

            this.p1Name = p1Name;
            this.p2Name = p2Name;

            cpuGame = false;
            game.WriteMessage("04\n");

            isP1Turn = true;
        }

        public string GameName => "tictactoe";

        public string GetBoard()
        {
            string lastMsg = game.ReadMessage();
            game.WriteMessage("01\n");
            string board = game.ReadMessage();
            string message = $"turn: {(isP1Turn ? p1Name : p2Name)}\ncmd: {lastMsg}";

            for (int i = 0; i < TicTacToe.BOARD_SIZE; i++)
            {
                if (i % 3 == 0)
                {
                    message += "\n";
                }
                if (board[i] == '*')
                {
                    message += BLANK;
                }
                if (board[i] == 'X')
                {
                    message += EX;
                }
                if (board[i] == 'O')
                {
                    message += OH;
                }
            }
            return message;
        }

        public IEmote[] GetReactions()
        {
            return actions;
        }

        public bool SubitMove(IEmote emote, ulong userId)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                if (emote.Equals(actions[i]))
                {
                    int x = i % 3;
                    int y = i / 3;

                    if (userId == player1)
                    {
                        game.WriteMessage($"02 {x} {y}\n");
                        if (game.ReadMessage() == TicTacToe.OK) isP1Turn = false;
                    }
                    if (userId == player2)
                    {
                        game.WriteMessage($"05 {x} {y}\n");
                        if (game.ReadMessage() == TicTacToe.OK) isP1Turn = true;
                    }

                    if(game.ReadMessage() == TicTacToe.OK && cpuGame)
                    {
                        game.WriteMessage("03\n");
                        if (game.ReadMessage() == TicTacToe.OK) isP1Turn = true;
                    }
                    return true;
                }
            }
            return true;
        }
    }

    class TicTacToe
    {
        public const string DEVICE_NAME = "tictactoe";
        public const int BOARD_SIZE = 9;

        // messages
        public const int NEW_GAME   = 0;
        public const int GET_BOARD  = 1;
        public const int PLRMOVE    = 2;
        public const int CPUMOVE    = 3;
        public const int NUM_CMDS   = 4;

        // responses
        public const string OK         = "OK\n";
        public const string UNKCMD     = "UNKCMD\n";
        public const string INVFMT     = "INVFMT\n";
        public const string ILLMOVE    = "ILLMOVE\n";
        public const string OOT        = "OOT\n";
        public const string WINGAME    = "WIN\n";
        public const string TIEGAME    = "TIE\n";
        public const string NOGAME     = "NOGAME\n";

        
        // message information
        string msg;

        // game information
        char[] game_board = new char[BOARD_SIZE];
        char curr_turn = '*';
        char p1_turn = '*';
        char p2_turn = '*';
        char cpu_turn = '*';

        public TicTacToe()
        {
            ClearBoard();
        }

        public string ReadMessage()
        {
            return msg;
        }

        public void WriteMessage(string message)
        {
            ParseCommand(message);
        }

        /* resets the board to empty marks (i.e. *)*/
        private void ClearBoard()
        {
            int i;
            for (i = 0; i < BOARD_SIZE; i++)
            {
                game_board[i] = '*';
            }
        }

        /*
            Creates a new game. If pl_turn_choice is 'X' or 'O'
            then creates a game with the computer as the opposite mark.
            If pl_turn_choice is '*' then creates a two player game without
            a computer player.
        */
        private void NewGame(char pl_turn_choice)
        {
            ClearBoard();
            curr_turn = 'X';

            if (pl_turn_choice == '*')
            {
                p1_turn = 'X';
                p2_turn = 'O';
                cpu_turn = '*';
            }
            else
            {
                p1_turn = pl_turn_choice;
                p2_turn = '*';
                cpu_turn = (p1_turn == 'X') ? 'O' : 'X';
            }
        }

        /*
            Checks to see if a player won the game for the given
            board state. returns the winning player's mark ('X' or 'O'),
            or '*' for no win, or 'T' for tie
        */
        private char CheckWin(char[] game_board)
        {
            int x, y, count;

            // check columns
            for (x = 0; x < 3; x++)
            {
                if (game_board[x] != '*' && game_board[x] == game_board[x + 3] && game_board[x] == game_board[x + 6])
                {
                    return game_board[x];
                }
            }

            // check rows
            for (y = 0; y < 3; y++)
            {
                if (game_board[y * 3] != '*' && game_board[y * 3] == game_board[1 + y * 3] && game_board[y * 3] == game_board[2 + y * 3])
                {
                    return game_board[y * 3];
                }
            }

            // check diagonals
            if (game_board[0] != '*' && game_board[0] == game_board[4] && game_board[0] == game_board[8])
            {
                return game_board[0];
            }
            if (game_board[2] != '*' && game_board[2] == game_board[4] && game_board[2] == game_board[6])
            {
                return game_board[2];
            }

            // check for tie
            count = 9;
            for (x = 0; x < 3; x++)
            {
                for (y = 0; y < 3; y++)
                {
                    if (game_board[x + y * 3] != '*')
                    {
                        count--;
                    }
                }
            }

            return count == 0 ? 'T' : '*';
        }

        /*
            tries to make a move at x, y with "turn" mark
            stores the response in msg
        */
        private void MakeMove(char turn, int x, int y)
        {
            char check;

            if (x < 0 || x > 2 || y < 0 || y > 2) // out of bounds coordinates
            {
                msg = ILLMOVE;
            }
            else if (curr_turn == '*') // turn has not been initialized, so no game is started
            {
                msg = NOGAME;
            }
            else if (turn != curr_turn) // not their turn
            {
                msg = OOT;
            }
            else if (game_board[x + y * 3] != '*') // board space already marked
            {
                msg = ILLMOVE;
            }
            else
            {
                game_board[x + y * 3] = turn; // make move
                check = CheckWin(game_board);
                if (check == turn) // they wins
                {
                    curr_turn = '*';
                    msg = WINGAME;
                }
                else if (check == 'T') // game tied
                {
                    curr_turn = '*';
                    msg = TIEGAME;
                }
                else // move okay, switch turns
                {
                    curr_turn = (turn == 'X') ? 'O' : 'X';
                    msg = OK;
                }
            }
        }

        /*
            Attempts to make a move for the computer player.
            Stores the result in msg
        */
        private void DoCPUTurn()
        {
            int x = 0, y = 0;

            if (curr_turn == '*') // turn has not been initialized, so no game is started
            {
                msg = NOGAME;
                return;
            }
            else if (cpu_turn != curr_turn) // not cpu's turn
            {
                msg = OOT;
                return;
            }

            MinMaxEval(game_board, cpu_turn, cpu_turn, out x, out y);
            MakeMove(cpu_turn, x, y);
        }

        /*
            evaluates the board using the min-max algorithm
            game_board: the current board state
            eval_turn: the turn we are evaluating for (at the top of the tree)
            this_turn: the current turn for this node in the tree
            x, y: best x and y move
            returns eval score for minmax calculations
        */
        private int MinMaxEval(char[] game_board, char eval_turn, char this_turn, out int resultX, out int resultY)
        {
            int bestX, bestY, bestValue, testX, testY, testValue, x, y;
            char test_win;
            char[] copy_board = new char[BOARD_SIZE];
            game_board.CopyTo(copy_board, 0);

            resultX = resultY = 0;

            test_win = CheckWin(copy_board);
            if (test_win == eval_turn)
            {
                return 100; // 100 points for winning
            }
            else if (test_win == 'T')
            {
                return 0; // 0 points for tie
            }
            else if (test_win != '*')
            {
                return -100; // -100 points for losing
            }

            bestX = bestY = 0;
            bestValue = eval_turn == this_turn ? -10000 : 10000;
            for (x = 0; x < 3; x++)
            {
                for (y = 0; y < 3; y++)
                {
                    if (copy_board[x + y * 3] == '*')
                    {
                        copy_board[x + y * 3] = this_turn;
                        testValue = MinMaxEval(copy_board, eval_turn, this_turn == 'X' ? 'O' : 'X', out testX, out testY);
                        if ((testValue > bestValue && eval_turn == this_turn)
                        || (testValue < bestValue && eval_turn != this_turn))
                        {
                            bestValue = testValue;
                            bestX = x;
                            bestY = y;
                        }
                        copy_board[x + y * 3] = '*';
                    }
                }
            }

            resultX = bestX;
            resultY = bestY;
            return bestValue;
        }

        /*
            parses the command, executes it if possible,
            and stores the result in msg
        */
        private void ParseCommand(string message)
        {
            int x, y;
            char turn;

            msg = INVFMT; // default message
            if (message != null)
            {
                if (message.StartsWith("00") && message.Length == 5) // BEGIN NEW GAME
                {
                    turn = message[3];
                    if (turn == 'X' || turn == 'O')
                    {
                        NewGame(turn); // begin game with player's mark choice
                        msg = OK;
                    }
                }
                else if (message == "01\n") // READ BOARD
                {
                    msg = string.Join(string.Empty, game_board) + "\n";
                }
                else if (message.StartsWith("02") && message.Length == 7) // PLAYER TURN
                {
                    string[] args = message.Split(' ');
                    x = int.Parse(args[1]);
                    y = int.Parse(args[2]);
                    MakeMove(p1_turn, x, y); // make the player's move
                }
                else if (message == "03\n") // CPU TURN
                {
                    DoCPUTurn(); // do cpu turn
                }
                else if (message == "04\n") // TWO PLAYER GAME
                {
                    NewGame('*');
                    msg = OK;
                }
                else if (message.StartsWith("05")) // PLAYER 2 TURN
                {
                    string[] args = message.Split(' ');
                    x = int.Parse(args[1]);
                    y = int.Parse(args[2]);
                    MakeMove(p2_turn, x, y); // make the player's move
                }
                else
                {
                    msg = UNKCMD;
                }
            }
        }
    }
}
