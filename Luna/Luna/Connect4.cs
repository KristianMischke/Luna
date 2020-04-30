using Discord;
using System;
using System.Collections.Generic;
using System.Text;

namespace Luna
{
    class Connect4Handler : IReactionGameHandler
    {
        Connect4 game;

        private string RED = char.ConvertFromUtf32(0x1F534);
        private string YELLOW = char.ConvertFromUtf32(0x1F7E1);
        private string WHITE = char.ConvertFromUtf32(0x26AA);

        private IEmote[] actions = new IEmote[]
        {
            new Emoji("\u0031\uFE0F\u20E3"), // 1
            new Emoji("\u0032\uFE0F\u20E3"), // 2
            new Emoji("\u0033\uFE0F\u20E3"), // 3
            new Emoji("\u0034\uFE0F\u20E3"), // 4
            new Emoji("\u0035\uFE0F\u20E3"), // 5
            new Emoji("\u0036\uFE0F\u20E3"), // 6
            new Emoji("\u0037\uFE0F\u20E3"), // 7
        };

        ulong player1;
        ulong player2;

        string p1Name;
        string p2Name;

        bool cpuGame;

        bool isP1Turn;

        public Connect4Handler(ulong player1, string p1Name, bool playerFirst)
        {
            game = new Connect4();

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

        public Connect4Handler(ulong player1, ulong player2, string p1Name, string p2Name)
        {
            game = new Connect4();

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

            for (int i = 0; i < Connect4.BOARD_SIZE; i++)
            {
                if (i % Connect4.WIDTH == 0)
                {
                    message += "\n";
                }
                if (board[i] == '*')
                {
                    message += WHITE;
                }
                if (board[i] == 'X')
                {
                    message += RED;
                }
                if (board[i] == 'O')
                {
                    message += YELLOW;
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
                    if (userId == player1)
                    {
                        game.WriteMessage($"02 {i}\n");
                        if (game.ReadMessage() == TicTacToe.OK) isP1Turn = false;
                    }
                    if (userId == player2)
                    {
                        game.WriteMessage($"05 {i}\n");
                        if (game.ReadMessage() == TicTacToe.OK) isP1Turn = true;
                    }

                    if (game.ReadMessage() == TicTacToe.OK && cpuGame)
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

    class Connect4
    {
        public const string DEVICE_NAME = "connect4";
        public const int WIDTH = 7;
        public const int HEIGHT = 6;
        public const int BOARD_SIZE = WIDTH * HEIGHT;

        // responses
        public const string OK = "OK\n";
        public const string UNKCMD = "UNKCMD\n";
        public const string INVFMT = "INVFMT\n";
        public const string ILLMOVE = "ILLMOVE\n";
        public const string OOT = "OOT\n";
        public const string WINGAME = "WIN\n";
        public const string TIEGAME = "TIE\n";
        public const string NOGAME = "NOGAME\n";

        // message information
        string msg;

        // game information
        char[] game_board = new char[BOARD_SIZE];
        char curr_turn = '*';
        char p1_turn = '*';
        char p2_turn = '*';
        char cpu_turn = '*';

        public Connect4()
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
            for (x = 0; x < WIDTH; x++)
            {
                int countX = 0;
                int countO = 0;
                for (y = 0; y < HEIGHT; y++)
                {
                    if (game_board[x + y * WIDTH] == 'X')
                    {
                        countO = 0;
                        countX++;
                    }
                    else if (game_board[x + y * WIDTH] == 'O')
                    {
                        countX = 0;
                        countO++;
                    }

                    if (countX >= 4) return 'X';
                    if (countO >= 4) return 'O';
                }
            }

            // check rows
            for (y = 0; y < HEIGHT; y++)
            {
                int countX = 0;
                int countO = 0;
                for (x = 0; x < WIDTH; x++)
                {
                    if (game_board[x + y * WIDTH] == 'X')
                    {
                        countO = 0;
                        countX++;
                    }
                    else if (game_board[x + y * WIDTH] == 'O')
                    {
                        countX = 0;
                        countO++;
                    }
                    else
                    {
                        countX = 0;
                        countO = 0;
                    }

                    if (countX >= 4) return 'X';
                    if (countO >= 4) return 'O';
                }
            }

            //check for diagonals
            for (y = 0; y < HEIGHT-3; y++)
            {
                for (x = 0; x < WIDTH; x++)
                {
                    int countX = 0;
                    int countO = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int test = (x + i) + (y + i) * WIDTH;
                        if (test >= 0 && test < BOARD_SIZE && game_board[test] == 'X')
                        {
                            countO = 0;
                            countX++;
                        }
                        else if (test >= 0 && test < BOARD_SIZE && game_board[test] == 'O')
                        {
                            countX = 0;
                            countO++;
                        }
                        else
                        {
                            countX = 0;
                            countO = 0;
                        }

                        if (countX >= 4) return 'X';
                        if (countO >= 4) return 'O';
                    }

                    countX = 0;
                    countO = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int test = (x - i) + (y + i) * WIDTH;
                        if (test >= 0 && test < BOARD_SIZE && game_board[test] == 'X')
                        {
                            countO = 0;
                            countX++;
                        }
                        else if (test >= 0 && test < BOARD_SIZE && game_board[test] == 'O')
                        {
                            countX = 0;
                            countO++;
                        }
                        else
                        {
                            countX = 0;
                            countO = 0;
                        }

                        if (countX >= 4) return 'X';
                        if (countO >= 4) return 'O';
                    }
                }
            }

            // check for tie
            count = BOARD_SIZE;
            for (x = 0; x < WIDTH; x++)
            {
                for (y = 0; y < HEIGHT; y++)
                {
                    if (game_board[x + y * WIDTH] != '*')
                    {
                        count--;
                    }
                }
            }

            return count == 0 ? 'T' : '*';
        }

        /*
            tries to make a move at x with "turn" mark
            stores the response in msg
        */
        private void MakeMove(char turn, int x)
        {
            char check;

            if (x < 0 || x >= WIDTH) // out of bounds coordinates
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
            else if (game_board[x] != '*') // board space already marked
            {
                msg = ILLMOVE;
            }
            else
            {
                int y = 0;
                while (x + y * WIDTH < BOARD_SIZE && game_board[x + y * WIDTH] == '*') y++;
                y--;
                game_board[x + y * WIDTH] = turn; // make move
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
            int x = 0;

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

            MinMaxEval(game_board, cpu_turn, cpu_turn, out x, 4);
            MakeMove(cpu_turn, x);
        }

        /*
            evaluates the board using the min-max algorithm
            game_board: the current board state
            eval_turn: the turn we are evaluating for (at the top of the tree)
            this_turn: the current turn for this node in the tree
            x, y: best x and y move
            returns eval score for minmax calculations
        */
        private int MinMaxEval(char[] game_board, char eval_turn, char this_turn, out int resultX, int depth)
        {
            int bestX, bestValue, testX, testValue, x;
            char test_win;
            char[] copy_board = new char[BOARD_SIZE];
            game_board.CopyTo(copy_board, 0);

            resultX = 0;

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

            if (depth == 0)
            {
                return 0;
            }

            bestX = 0;
            bestValue = eval_turn == this_turn ? -10000 : 10000;
            for (x = 0; x < WIDTH; x++)
            {
                if (copy_board[x] == '*')
                {
                    int y = 0;
                    while (x + y * WIDTH < BOARD_SIZE && copy_board[x + y * WIDTH] == '*') y++;
                    y--;
                    copy_board[x + y * WIDTH] = this_turn;
                    testValue = MinMaxEval(copy_board, eval_turn, this_turn == 'X' ? 'O' : 'X', out testX, depth-1);
                    if ((testValue > bestValue && eval_turn == this_turn)
                    || (testValue < bestValue && eval_turn != this_turn))
                    {
                        bestValue = testValue;
                        bestX = x;
                    }
                    copy_board[x + y * WIDTH] = '*';
                }
            }

            resultX = bestX;
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
                else if (message.StartsWith("02") && message.Length == 5) // PLAYER TURN
                {
                    string[] args = message.Split(' ');
                    x = int.Parse(args[1]);
                    MakeMove(p1_turn, x); // make the player's move
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
                    MakeMove(p2_turn, x); // make the player's move
                }
                else
                {
                    msg = UNKCMD;
                }
            }
        }
    }
}
