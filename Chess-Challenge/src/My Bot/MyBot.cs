﻿//#define DEBUG
//#define UCI

#pragma warning disable RCS1001, S125 // Add braces (when expression spans over multiple lines) - Tokens are tokens

using ChessChallenge.API;
using System;
using System.Linq;

using static ChessChallenge.API.BitboardHelper;
using static System.Math;
using static System.BitConverter;

public class MyBot : IChessBot
{
    public /*internal*/ Board _position;
    Timer _timer;
    int _timePerMove, _nodes;

    readonly int[,] _previousKillerMoves = new int[2, 128],
                    _killerMoves = new int[2, 128];
    // _historyMoves = new int[12, 64];

    Move _bestMove;

    struct TTElement
    {
        public ulong Key;
        public Move BestMove;
        //public short Score;
        //public byte Depth;
        //public byte NodeType;
    }

    const int _ttMask = 0xFFFFFF;                   // 256 MB
    TTElement[] _tt = new TTElement[_ttMask + 1];

    public Move Think(Board board, Timer timer)
    {
        _position = board;
        _timer = timer;
        Array.Clear(_killerMoves);
        //Array.Clear(_historyMoves);

        int targetDepth = 1,
            alpha = -32_768,    //  short.MinValue
            beta = 32_767;       //  short.MaxValue+
#if DEBUG
        int bestEvaluation = 0;
#endif
        _timePerMove = timer.MillisecondsRemaining / 30;
        _nodes = 0;
        _bestMove = default;

#if DEBUG
        Console.WriteLine($"\n[{GetType().Name}] Searching {_position.GetFenString()} ({_timePerMove}ms to move)");
#endif

        try
        {
            bool isMateDetected;
            int msSpentPerDepth = 0;
            do
            {
                AspirationWindows_SearchAgain:
#if DEBUG
                bestEvaluation = NegaMax(targetDepth, 0, alpha, beta, false);
#else
                int bestEvaluation = NegaMax(targetDepth, 0, alpha, beta, false);
#endif
                isMateDetected = Abs(bestEvaluation) > 27_000;

                if (!isMateDetected && (bestEvaluation <= alpha || bestEvaluation >= beta))
                {
                    alpha = -32_768;    // short.MinValuem  We fell outside the window, so try again with a
                    beta = 32_767;      // short.MaxValue     full-width window (and the same depth).

                    goto AspirationWindows_SearchAgain;
                }

#if DEBUG || UCI
                PrintInfo(board, targetDepth, bestEvaluation, isMateDetected);
#endif
                alpha = bestEvaluation - 50;
                beta = bestEvaluation + 50;

                Array.Copy(_killerMoves, _previousKillerMoves, _killerMoves.Length);

                msSpentPerDepth = timer.MillisecondsElapsedThisTurn - msSpentPerDepth;
                ++targetDepth;
            }
            while (!isMateDetected && msSpentPerDepth < _timePerMove * 0.5);
        }
#if DEBUG
        catch (Exception e)
        {
            if (!e.Message.StartsWith("Exception of type 'System.Exception' was thrown"))
            {
                Console.WriteLine($"Exception: {e.Message}\n{e.StackTrace}");
            }
        }
#else
        catch (Exception){}
#endif

#if DEBUG
        PrintBestMove(board, timer, targetDepth, bestEvaluation);
#endif
        return _bestMove.IsNull ? board.GetLegalMoves()[0] : _bestMove;
    }

    public /*internal */int NegaMax(int targetDepth, int ply, int alpha, int beta, bool isQuiescence)
    {
        if (_position.FiftyMoveCounter >= 100 || _position.IsRepeatedPosition() || _position.IsInsufficientMaterial())
            return 0;

        Move _ttMove = default;
        if (ply > 0)
        {
            ref var ttEntry = ref _tt[_position.ZobristKey & _ttMask];
            if (ttEntry.Key == _position.ZobristKey)
                _ttMove = ttEntry.BestMove;
        }

        if (!isQuiescence)
            if (_timer.MillisecondsElapsedThisTurn > _timePerMove)	//	 oh nice i get a higher nps removing the (nodes & 2047) == 0 💀 - not check all the time
                throw new();
            else if (ply > targetDepth)
                if (_position.IsInCheck())
                    ++targetDepth;
                else
                    return _position.GetLegalMoves().Any()//.Length > 0
                            ? NegaMax(targetDepth, ply, alpha, beta, true)  // Quiescence
                            : EvaluateFinalPosition(ply);

        // TODO: GetLegalMovesNonAlloc
        //Span<Move> spanLegalMoves = stackalloc Move[256];
        //_position.GetLegalMovesNonAlloc(ref spanLegalMoves);
        //spanLegalMoves.Sort((a, b) => Score(a, ply > Score(b, ply) ? 1 : 0));

        int staticEvaluation = 0,
            kingSquare;
        Move bestMove = default;

        if (isQuiescence)
        {
            #region Static evaluation

            ulong bitboard;
            for (int i = 0; ++i < 6;)
            {
                void Eval(bool localIsWhiteToMove)
                {
                    bitboard = _position.GetPieceBitboard((PieceType)i, localIsWhiteToMove);

                    while (bitboard != default)
                    {
                        var square = ClearAndGetIndexOfLSB(ref bitboard);

                        if (!localIsWhiteToMove)
                            square ^= 56;

                        staticEvaluation += (localIsWhiteToMove ? 1 : -1) * (
                            MaterialScore[i]
                            + Magic[square + 64 * i - 64]); // Magic[square + 64 * (i - 1)])
                    }
                }

                Eval(true);
                Eval(false);
            }

            bitboard = _position.GetPieceBitboard(PieceType.King, true);
            kingSquare = ClearAndGetIndexOfLSB(ref bitboard);

            staticEvaluation += Magic[kingSquare + (_position.GetPieceBitboard(PieceType.Queen, false) > 0
                ? 320   // Regular king positional values -  64 * ((int)PieceType(King), after regular tables
                : 384   // Endgame king position values - 64 * ((int)PieceType(King) - 1), last regular table
            )];

            bitboard = _position.GetPieceBitboard(PieceType.King, false);
            kingSquare = ClearAndGetIndexOfLSB(ref bitboard) ^ 56;

            staticEvaluation -= Magic[kingSquare + (_position.GetPieceBitboard(PieceType.Queen, true) > 0
                ? 320   // Regular king positional values -  64 * ((int)PieceType(King), after regular tables
                : 384   // Endgame king position values - 64 * ((int)PieceType(King) - 1), last regular table
            )];

            if (!_position.IsWhiteToMove)
                staticEvaluation = -staticEvaluation;

            #endregion

            // Fail-hard beta-cutoff (updating alpha after this check)
            if (staticEvaluation >= beta)
                return staticEvaluation;

            // Better move
            if (staticEvaluation > alpha)
                alpha = staticEvaluation;
        }

        ++_nodes;

        var moves = _position.GetLegalMoves(isQuiescence);

        if (isQuiescence && moves.Length == 0)
            return staticEvaluation;

        //byte nodeType = 1;   // alpha

        foreach (var move in moves.OrderByDescending(move => Score(move, ply, isQuiescence, _ttMove)))
        {
            _position.MakeMove(move);
            var evaluation = -NegaMax(targetDepth, ply + 1, -beta, -alpha, isQuiescence); // Invokes itself, either Negamax or Quiescence
            _position.UndoMove(move);

            // Fail-hard beta-cutoff - refutation found, no need to keep searching this line
            if (evaluation >= beta)
            {
                if (!isQuiescence && !move.IsCapture)
                {
                    _killerMoves[1, ply] = _killerMoves[0, ply];
                    _killerMoves[0, ply] = move.RawValue;
                }
                alpha = beta;
                //nodeType = 2;   // beta
                break;
            }

            if (evaluation > alpha)
            {
                alpha = evaluation;
                bestMove = move;
                if (ply == 0)
                    _bestMove = bestMove;
                //nodeType = 3;   // exact

                // 🔍 History moves
                //if (!move.IsCapture) // No isNotQuiescence check needed, in quiecence there will never be non capure moves
                //{
                //    _historyMoves[(int)move.MovePieceType, move.TargetSquare.Index] += ply << 2;
                //}
            }
        }

        if (bestMove.IsNull && _position.GetLegalMoves().Length == 0)
            return EvaluateFinalPosition(ply);

        ref var entry = ref _tt[_position.ZobristKey & _ttMask];
        entry.Key = _position.ZobristKey;
        entry.BestMove = bestMove;
        //entry.NodeType = nodeType;
        //entry.Score = (short)alpha;
        //entry.Depth = (byte)(targetDepth - ply);

        // Node fails low
        return alpha;
    }

    public /*internal*/ int Score(Move move, int ply, bool isQuiescence, Move ttMove)
    {
        if (move == ttMove)
            return 200_000;

        if (move.IsCapture)
        {
            int targetPiece = (int)PieceType.Pawn;    // Important to initialize to P or p, due to en-passant captures
            for (int pieceIndex = 0; ++pieceIndex < 7;)
                if (SquareIsSet(
                    _position.GetPieceBitboard((PieceType)pieceIndex, !_position.IsWhiteToMove),
                    move.TargetSquare))
                {
                    targetPiece = pieceIndex;
                    break;
                }

            return 100_000 +
                Magic[441 + targetPiece + 6 * (int)move.MovePieceType];      // MVVLVATest.cs, current expression as a simplification of
                                                                             // 448 + targetPiece - 1 + 6 * ((int)move.MovePieceType - 1)
        }
        else if (!isQuiescence)
            //{
            // 1st killer move
            if (_killerMoves[0, ply] == move.RawValue)
                return 9_000;

            // 2nd killer move
            else if (_killerMoves[1, ply] == move.RawValue)
                return 8_000;

        //    // History move
        //    //else if (historyMoves is not null)
        //    //{
        //    //    return historyMoves[(int)move.MovePieceType + offset, move.TargetSquare.Index];
        //    //}

        //    return 0;
        //}

        return 0;
    }

    private int EvaluateFinalPosition(int ply) => _position.IsInCheck()
        ? -30_000 + 10 * ply
        : 0;

    #region Material score, PQST and MVVLVA

    /// <summary>
    /// P PSQT | N PSQT | B PSQT | R PSQT | Q PSQT | K PSQT | K endgame PSQT | MVVLVA
    /// </summary>
    public /*internal static*/ readonly int[]
        MaterialScore = new[]
        {
            0,      // PieceType.Pawn starts at index 1
            100,
            300,
            350,
            500,
            1_000
        },
        Magic =
        new[/*41*/]
        {
                24868030789173962581061818970m, 27962881072575429945784425040m, 34164717750142730454335511130m,
                34176901826770794923914716270m, 55925761771157817937832870530m, 18654104760093094720549729460m,
                31081933020509579254836122955m, 26427640099376182114853547620m, 34176783119447850105141355605m,
                23314561296157175206972976750m, 24843805044787745151253497374m, 26427545096476845190073566800m,
                37259544051696060033187863125m, 27968973295717185390385259640m, 27962928018458255659375680085m,
                27950744220335595857669020250m, 34164670342372161863418863455m, 27962928481431090069478466670m,
                34164670342010472015904135770m, 27962928481431090069478466670m, 43497814715160557429687623840m,
                24855917919143983528981335180m, 31063799318045908771826261840m, 29516398159635894078093090660m,
                31069843948224851390503346015m, 24874099122699871243475050340m, 24892304121107408860622049360m,
                35736131866136166722759570020m, 12427947061061072563693168680m, 12427947061061072563693168680m,
                12427947061061072563693168680m, 12427947061061072563693168680m, 34170762287008501567167160154m,
                31075959875794436853749541230m, 43473493878160450689378578025m, 31075959875794436853917972620m,
                31688813875635234031847171167m, 32611465967480339888542278758m, 31990068614427286261316681075m,
                31368671261374232633132022641m, 79228162514264337591623186799m
        }
        .SelectMany(x => decimal.GetBits(x).Take(3))    // Removes non-integer part of the Decimal
        .SelectMany(GetBytes)
        //.Where(b => b < byte.MaxValue)    // Removes extra-padding, given the array length could not be multiple of 12
        .Select(b => b - 90)
        .ToArray();

    #endregion

    #region debugging
#if DEBUG || UCI

#if DEBUG
    public MyBot()
    {
        var ttElementSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TTElement));
        Console.WriteLine("TT length:\t{0} items", _tt.Length);
        Console.WriteLine("TT memory:\t{0} MB", (_tt.Length * ttElementSize) / 1024 / 1024);
        Console.WriteLine("TT entry:\t{0} bytes", ttElementSize);
        Console.WriteLine("TT mask:\t{0}", _ttMask.ToString("X"));
    }
#endif

    System.Collections.Generic.List<Move> CalculatePV(Board board, System.Collections.Generic.List<Move> bestMoveList = null)
    {
        bestMoveList ??= new System.Collections.Generic.List<Move>(128);

        ref TTElement entry = ref _tt[_position.ZobristKey & _ttMask];
        if (!entry.BestMove.IsNull && board.GetLegalMoves().Contains(entry.BestMove))
        {
            bestMoveList.Add(entry.BestMove);
            board.MakeMove(entry.BestMove);
            CalculatePV(board, bestMoveList);
            board.UndoMove(entry.BestMove);
        }

        return bestMoveList;
    }

    private void PrintInfo(Board board, int targetDepth, int bestEvaluation, bool isMateDetected)
    {
        Console.WriteLine(
            $"info depth {targetDepth}" +
            $" score {(isMateDetected ? "mate 99" : $"cp {bestEvaluation}")}" +
            $" nodes {_nodes}" +
            $" nps {Convert.ToInt64(Clamp(_nodes / ((0.001 * _timer.MillisecondsElapsedThisTurn) + 1), 0, long.MaxValue))}" +
            $" time {_timer.MillisecondsElapsedThisTurn}" +
            $" pv {string.Join(' ', CalculatePV(board).Select(m => m.ToString()[7..^1]))}");
    }

    private void PrintBestMove(Board board, Timer timer, int targetDepth, int bestEvaluation)
    {
        Console.WriteLine(
            $"bestmove {_bestMove.ToString()[7..^1]}" +
            $" score cp {bestEvaluation}" +
            $" depth {targetDepth - 1}" +
            $" time {timer.MillisecondsElapsedThisTurn}" +
            $" nodes {_nodes}" +
            $" pv {string.Join(' ', CalculatePV(board).Select(m => m.ToString()[7..^1]))}");
    }

#endif
    #endregion
}

#pragma warning restore RCS1001 // Add braces (when expression spans over multiple lines).
