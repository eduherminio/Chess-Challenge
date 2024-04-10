using ChessChallenge.API;
using System.Diagnostics;

namespace TeenyLynx.UCI;

/// <summary>
/// Based on Lynx perft
/// </summary>
public static class Perft
{
    public const int MaxNumberOfPossibleMovesInAPosition = 250;

    public static (long Nodes, double ElapsedMilliseconds) Results(Board position, int depth)
    {
        var sw = new Stopwatch();
        sw.Start();
        var nodes = ResultsImpl(position, depth, 0);
        sw.Stop();

        return (nodes, CalculateElapsedMilliseconds(sw));
    }

    public static (long Nodes, double ElapsedMilliseconds) Divide(Board position, int depth, Action<string> write)
    {
        var sw = new Stopwatch();
        sw.Start();
        var nodes = DivideImpl(position, depth, 0, write);
        sw.Stop();

        return (nodes, CalculateElapsedMilliseconds(sw));
    }

    /// <summary>
    /// Proper implementation, used by <see cref="DivideImpl(Position, int, long)"/> as well
    /// </summary>
    /// <param name="position"></param>
    /// <param name="depth"></param>
    /// <param name="nodes"></param>
    /// <returns></returns>
    internal static long ResultsImpl(Board position, int depth, long nodes)
    {
        if (depth != 0)
        {
            Span<Move> moves = stackalloc Move[MaxNumberOfPossibleMovesInAPosition];
            position.GetLegalMovesNonAlloc(ref moves);
            foreach (var move in moves)
            {
                position.MakeMove(move);

                nodes = ResultsImpl(position, depth - 1, nodes);

                position.UndoMove(move);
            }

            return nodes;
        }

        return nodes + 1;
    }

    private static long DivideImpl(Board position, int depth, long nodes, Action<string> write)
    {
        if (depth != 0)
        {
            Span<Move> moves = stackalloc Move[MaxNumberOfPossibleMovesInAPosition];
            position.GetLegalMovesNonAlloc(ref moves);
            foreach (var move in moves)
            {
                position.MakeMove(move);

                var accumulatedNodes = nodes;
                nodes = ResultsImpl(position, depth - 1, nodes);
                write($"{move.ToString()}\t\t{nodes - accumulatedNodes}");

                position.UndoMove(move);
            }

            write(string.Empty);

            return nodes;
        }

        return nodes + 1;
    }

    public static void PrintPerftResult(int depth, (long Nodes, double ElapsedMilliseconds) peftResult, Action<string> write)
    {
        var timeStr = TimeToString(peftResult.ElapsedMilliseconds);

        write(
            $"Depth:\t{depth}" + Environment.NewLine +
            $"Nodes:\t{peftResult.Nodes}" + Environment.NewLine +
            $"Time:\t{timeStr}" + Environment.NewLine +
            $"nps:\t{(Math.Round(peftResult.Nodes / peftResult.ElapsedMilliseconds)) / 1000} Mnps" + Environment.NewLine);
    }

    /// <summary>
    /// http://geekswithblogs.net/BlackRabbitCoder/archive/2012/01/12/c.net-little-pitfalls-stopwatch-ticks-are-not-timespan-ticks.aspx
    /// </summary>
    /// <param name="stopwatch"></param>
    /// <returns></returns>
    private static double CalculateElapsedMilliseconds(Stopwatch stopwatch)
    {
        return 1000 * stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
    }

    private static string TimeToString(double milliseconds)
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
}
