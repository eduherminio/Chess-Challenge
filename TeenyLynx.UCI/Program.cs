// Based on https://github.com/Alex2262/Chess-Challenge/tree/UCI

using ChessChallenge.API;
using ChessChallenge.Chess;
using System.Diagnostics;
using System.Reflection;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;
using Timer = ChessChallenge.API.Timer;

const string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
Board board = Board.CreateBoardFromFEN(startFen);
MyBot myBot = new MyBot();

while (true)
{
    try
    {
        var line = Console.ReadLine();
        var tokens = line.Split();

        if (tokens[0] == "quit")
        {
            break;
        }

        if (tokens[0] == "uci")
        {
            Console.WriteLine($"id name TeenyLynx {GetVersion()}");
            Console.WriteLine("id author Eduardo Cáceres");
            Console.WriteLine("uciok");
        }

        if (tokens[0] == "option")
        {
            Console.WriteLine("option name UCI_Opponent type string");
            Console.WriteLine("option name UCI_EngineAbout type string default TeenyLynx by Eduardo Cáceres, see https://github.com/lynx-chess/TeenyLynx");
        }

        if (tokens[0] == "isready")
        {
            Console.WriteLine("readyok");
        }

        if (tokens[0] == "position")
        {
            int nextIndex;

            if (tokens[1] == "startpos")
            {
                board = Board.CreateBoardFromFEN(startFen);
                nextIndex = 2;
            }
            else if (tokens[1] == "fen")
            {
                var fen = "";
                for (int i = 2; i < tokens.Length; i++)
                {
#pragma warning disable S1643 // Strings should not be concatenated using '+' in a loop - it actually depends on the size
                    fen += tokens[i] + " ";
#pragma warning restore S1643 // Strings should not be concatenated using '+' in a loop
                }

                board = Board.CreateBoardFromFEN(fen);
                nextIndex = 8;
            }
            else
            {
                continue;
            }

            if (tokens.Length <= nextIndex || tokens[nextIndex] != "moves")
            {
                continue;
            }

            for (int i = nextIndex + 1; i < tokens.Length; i++)
            {
                Move move = new Move(tokens[i], board);
                board.MakeMove(move);
            }
        }

        if (tokens[0] == "go")
        {
            int allocatedTime = 0, wTime = 0, bTime = 0, wInc = 0, bInc = 0;

            for (int i = 1; i < tokens.Length; i += 2)
            {
                var type = tokens[i];
                int value = 0;

                if (tokens.Length > i + 1) value = int.Parse(tokens[i + 1]);

                if (type == "movetime") allocatedTime = (int)(value * 0.95);

                if (type == "wtime") wTime = value;

                if (type == "btime") bTime = value;

                if (type == "winc") wInc = value;

                if (type == "binc") bInc = value;
            }

            if (allocatedTime == 0)
            {
                allocatedTime = board.IsWhiteToMove
                    ? wTime + wInc
                    : bTime + bInc;
            }

            Timer timer = new Timer(allocatedTime);
            Move move = myBot.Think(board, timer);

            Console.WriteLine("bestmove " + GetMoveNameUCI(move));
        }
        if (tokens[0] == "perft" && tokens.Length >=2)
        {
            var depth =  int.Parse(tokens[1]);
            myBot._position = board;

            var sw = new Stopwatch();
            sw.Start();
            var nodes = ResultsImpl(myBot, depth, 0);
            sw.Stop();

            var results = (nodes, CalculateElapsedMilliseconds(sw));
            PrintPerftResult(depth, results, str => Console.WriteLine(str));
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
        Console.WriteLine(e.StackTrace);
    }
}

static string GetMoveNameUCI(Move move)
{
    string startSquareName = BoardHelper.SquareNameFromIndex(move.StartSquare.Index);
    string endSquareName = BoardHelper.SquareNameFromIndex(move.TargetSquare.Index);
    string moveName = startSquareName + endSquareName;

    if (move.IsPromotion)
    {
        switch (move.PromotionPieceType)
        {
            case PieceType.Rook:
                moveName += "r";
                break;
            case PieceType.Knight:
                moveName += "n";
                break;
            case PieceType.Bishop:
                moveName += "b";
                break;
            case PieceType.Queen:
                moveName += "q";
                break;
        }
    }
    return moveName;
}

static string GetVersion()
{
    return
        Assembly.GetAssembly(typeof(IChessBot))
        !.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')?[0]
        ?? "Unknown";
}

static long ResultsImpl(MyBot myBot, int depth, long nodes)
{
    if (depth != 0)
    {
        foreach (var move in myBot._position.GetLegalMoves(false))
        {
            myBot._position.MakeMove(move);
            nodes = ResultsImpl(myBot, depth - 1, nodes);
            myBot._position.UndoMove(move);
        }

        return nodes;
    }

    return nodes + 1;
}

/// <summary>
/// http://geekswithblogs.net/BlackRabbitCoder/archive/2012/01/12/c.net-little-pitfalls-stopwatch-ticks-are-not-timespan-ticks.aspx
/// </summary>
/// <param name="stopwatch"></param>
/// <returns></returns>
static double CalculateElapsedMilliseconds(Stopwatch stopwatch)
{
    return 1000 * stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
}

static void PrintPerftResult(int depth, (long Nodes, double ElapsedMilliseconds) peftResult, Action<string> write)
{
    var timeStr = TimeToString(peftResult.ElapsedMilliseconds);

    write(
        $"Depth:\t{depth}" + Environment.NewLine +
        $"Nodes:\t{peftResult.Nodes}" + Environment.NewLine +
        $"Time:\t{timeStr}" + Environment.NewLine +
        $"nps:\t{(Math.Round(peftResult.Nodes / peftResult.ElapsedMilliseconds)) / 1000} Mnps" + Environment.NewLine);
}

static string TimeToString(double milliseconds)
{
    return milliseconds switch
    {
        < 1 => $"{milliseconds:F} ms",
        < 1_000 => $"{Math.Round(milliseconds)} ms",
        < 60_000 => $"{0.001 * milliseconds:F} s",
        < 3_600_000 => $"{Math.Floor(milliseconds / 60_000)} min {Math.Round(0.001 * (milliseconds % 60_000))} s",
        _ => $"{Math.Floor(milliseconds / 3_600_000)} h {Math.Round((milliseconds % 3_600_000) / 60_000)} min"
    };
}