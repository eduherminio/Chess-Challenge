#pragma warning disable RCS1001 // Add braces (when expression spans over multiple lines) - Tokens are tokens

using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    Board _position;
    int _targetDepth;
    readonly int[] _indexes = new int[128 + 1];
    readonly Move[] _pVTable = new Move[128 * (128 + 1) / 2];
    //readonly int[,] _previousKillerMoves = new int[2, 128];
    //readonly int[,] _killerMoves = new int[2, 128];
    //readonly int[,] _historyMoves = new int[12, 64];

    bool _isFollowingPV, _isScoringPV;

    public MyBot()
    {
        #region _indexes initialization

        int previousPVIndex = 0;
        _indexes[0] = previousPVIndex;

        for (int i = -1; ++i < _indexes.Length - 1;)
        {
            _indexes[i + 1] = previousPVIndex + 128 - i;
            previousPVIndex = _indexes[i + 1];
        }
        #endregion
    }

    public Move Think(Board board, Timer timer)
    {
        _position = board;
        _targetDepth = 1;
        _isScoringPV = false;
        Array.Clear(_pVTable);
        //Array.Clear(_killerMoves);
        //Array.Clear(_historyMoves);

        #region Time management

        var movesToGo = 100 - board.PlyCount >> 1;
        movesToGo = movesToGo <= 0 ? 20 : movesToGo;
        int timePerMove = timer.MillisecondsRemaining / movesToGo;

#if DEBUG
        Console.WriteLine($"Time to move {timePerMove}");
#endif

        #endregion

        int alpha = short.MinValue, beta = short.MaxValue;

        Move bestMove = new();
        try
        {
            bool isMateDetected;
            int msSpentPerDepth = 0;
            do
            {
                //AspirationWindows_SearchAgain:
                _isFollowingPV = true;
                int bestEvaluation = NegaMax(0, alpha, beta);
                isMateDetected = Math.Abs(bestEvaluation) > 27_000;

                //if (!isMateDetected && ((bestEvaluation <= alpha) || (bestEvaluation >= beta)))
                //{
                alpha = short.MinValue;   // We fell outside the window, so try again with a
                beta = short.MaxValue;    // full-width window (and the same depth).

                //    goto AspirationWindows_SearchAgain;
                //}

                bestMove = _pVTable[0];
#if DEBUG
                Console.WriteLine($"Depth {_targetDepth}: bestmove {bestMove.ToString()[7..^1]}, eval: {bestEvaluation}, PV: {string.Join(", ", _pVTable.TakeWhile(m => !m.IsNull).Select(m => m.ToString()[7..^1]))}");
#endif
                //alpha = bestEvaluation - 50;
                //beta = bestEvaluation + 50;

                //Array.Copy(_killerMoves, _previousKillerMoves, _killerMoves.Length);

                msSpentPerDepth = timer.MillisecondsElapsedThisTurn - msSpentPerDepth;
                ++_targetDepth;
            }
            while (!isMateDetected && msSpentPerDepth < timePerMove * 0.5);
        }
        catch (Exception)
        {
            ;
        }

#if DEBUG
        Console.WriteLine($"Time used {timer.MillisecondsElapsedThisTurn}");
#endif

        return bestMove;
    }

    int NegaMax(int ply, int alpha, int beta)
    {
        if (_position.IsDraw())
            return 0;

        if (ply > _targetDepth)
            return _position.GetLegalMoves().Any()//.Length > 0
                 ? QuiescenceSearch(ply, alpha, beta)
                 : EvaluateFinalPosition(ply);

        Move bestMove = new();

        int pvIndex, nextPvIndex;
        pvIndex = _indexes[ply];
        nextPvIndex = _indexes[ply + 1];
        _pVTable[pvIndex] = bestMove;

        var legalMoves = _position.GetLegalMoves();

        #region Move sorting

        if (_isFollowingPV)
        {
            _isFollowingPV = false;
            foreach (var move in legalMoves)
                if (move == _pVTable[ply])
                {
                    _isFollowingPV = _isScoringPV = true;
                    break;
                }
        }

        #endregion

        foreach (var move in legalMoves.OrderByDescending(move => Score(move, ply/*, _killerMoves*/)))
        {
            //PrintPreMove(ply, move);
            _position.MakeMove(move);
            var evaluation = -NegaMax(ply + 1, -beta, -alpha);
            _position.UndoMove(move);

            // Fail-hard beta-cutoff - refutation found, no need to keep searching this line
            if (evaluation >= beta)
                return beta;
            //{
            //if (!move.IsCapture)
            //{
            //    _killerMoves[1, ply] = _killerMoves[0, ply];
            //    _killerMoves[0, ply] = move.RawValue;
            //}

            //    return beta;
            //}

            if (evaluation > alpha)
            {
                alpha = evaluation;
                bestMove = move;

                // 🔍 History moves
                //if (!move.IsCapture)
                //{
                //    _historyMoves[(int)move.MovePieceType, move.TargetSquare.Index] += ply << 2;
                //}

                _pVTable[pvIndex] = move;
                CopyPVTableMoves(pvIndex + 1, nextPvIndex, ply);
            }
        }

        if (bestMove.IsNull && legalMoves.Length == 0)
            return EvaluateFinalPosition(ply);

        // Node fails low
        return alpha;
    }

    int QuiescenceSearch(int ply, int alpha, int beta)
    {
        if (_position.IsDraw())
            return 0;

        var pvIndex = _indexes[ply];
        var nextPvIndex = _indexes[ply + 1];
        Move bestMove = new();
        _pVTable[pvIndex] = bestMove;   // Nulling the first value before any returns

        var staticEvaluation = StaticEvaluation();

        // Fail-hard beta-cutoff (updating alpha after this check)
        if (staticEvaluation >= beta)
            return staticEvaluation;

        // Better move
        if (staticEvaluation > alpha)
            alpha = staticEvaluation;

        var captures = _position.GetLegalMoves(true);
        if (captures.Length == 0)
            return staticEvaluation;

        foreach (var move in captures.OrderByDescending(m => Score(m, ply)))
        {
            //PrintPreMove(ply, move, true);
            _position.MakeMove(move);
            var evaluation = -QuiescenceSearch(ply + 1, -beta, -alpha);
            _position.UndoMove(move);

            // Fail-hard beta-cutoff
            if (evaluation >= beta)
                return evaluation; // Pruning before starting quiescence search

            if (evaluation > alpha)
            {
                alpha = evaluation;
                bestMove = move;

                _pVTable[pvIndex] = move;
                CopyPVTableMoves(pvIndex + 1, nextPvIndex, ply);
            }
        }

        if (bestMove.IsNull)
            return _position.GetLegalMoves().Length > 0
                ? alpha
                : EvaluateFinalPosition(ply);

        // Node fails low
        return alpha;
    }

    int Score(Move move, int depth/*, int[,]? killerMoves = null,  int[,]? historyMoves = null*/)
    {
        if (_isScoringPV && move == _pVTable[depth])
        {
            _isScoringPV = false;

            return 20_000;
        }

        var offset = _position.IsWhiteToMove ? -1 : +5;

        if (move.IsCapture)
        {
            int targetPiece = (int)PieceType.Pawn;    // Important to initialize to P or p, due to en-passant captures
            for (int pieceIndex = 0; ++pieceIndex < 7;)
            {
                if (BitboardHelper.SquareIsSet(
                    _position.GetPieceBitboard((PieceType)pieceIndex, !_position.IsWhiteToMove),
                    move.TargetSquare))
                {
                    targetPiece = pieceIndex;
                    break;
                }
            }

            return 100_000 + MostValueableVictimLeastValuableAttacker[(int)move.MovePieceType + offset, targetPiece + offset];
        }
        //else
        //{
        //    // 1st killer move
        //    if (killerMoves?[0, depth] == move.RawValue)
        //    {
        //        return 9_000;
        //    }

        //    // 2nd killer move
        //    else if (killerMoves?[1, depth] == move.RawValue)
        //    {
        //        return 8_000;
        //    }

        //    // History move
        //    //else if (historyMoves is not null)
        //    //{
        //    //    return historyMoves[(int)move.MovePieceType + offset, move.TargetSquare.Index];
        //    //}

        //    return 0;
        //}

        return 0;
    }

    void CopyPVTableMoves(int target, int source, int ply)
    {
        if (_pVTable[source].IsNull)
        {
            Array.Clear(_pVTable, target, _pVTable.Length - target);
            return;
        }

        Array.Copy(_pVTable, source, _pVTable, target, 128 - ply - 1);
    }

    int StaticEvaluation()
    {
        int eval = 0;

        for (int i = 0; ++i < 6;)
        {
            var bitboard = _position.GetPieceBitboard((PieceType)i, _position.IsWhiteToMove);
            while (bitboard != default)
            {
                BitboardHelper.ClearAndGetIndexOfLSB(ref bitboard);
                eval += MaterialScore[i];
            }

            bitboard = _position.GetPieceBitboard((PieceType)i, !_position.IsWhiteToMove);
            while (bitboard != default)
            {
                BitboardHelper.ClearAndGetIndexOfLSB(ref bitboard);
                eval -= MaterialScore[i];
            }
        }

        return eval;
    }

    int EvaluateFinalPosition(int ply) => _position.IsInCheckmate()
                ? -30_000 + 10 * ply
                : 0;

    static readonly int[] MaterialScore = new[]
    {
        0,      // PieceType.Pawn starts at index 1
        100,
        300,
        350,
        500,
        1_000
    };

    static readonly int[,] MostValueableVictimLeastValuableAttacker = new int[12, 12]
    {
        { 105, 205, 305, 405, 505, 605, 105, 205, 305, 405, 505, 605 },
        { 104, 204, 304, 404, 504, 604, 104, 204, 304, 404, 504, 604 },
        { 103, 203, 303, 403, 503, 603, 103, 203, 303, 403, 503, 603 },
        { 102, 202, 302, 402, 502, 602, 102, 202, 302, 402, 502, 602 },
        { 101, 201, 301, 401, 501, 601, 101, 201, 301, 401, 501, 601 },
        { 100, 200, 300, 400, 500, 600, 100, 200, 300, 400, 500, 600 },
        { 105, 205, 305, 405, 505, 605, 105, 205, 305, 405, 505, 605 },
        { 104, 204, 304, 404, 504, 604, 104, 204, 304, 404, 504, 604 },
        { 103, 203, 303, 403, 503, 603, 103, 203, 303, 403, 503, 603 },
        { 102, 202, 302, 402, 502, 602, 102, 202, 302, 402, 502, 602 },
        { 101, 201, 301, 401, 501, 601, 101, 201, 301, 401, 501, 601 },
        { 100, 200, 300, 400, 500, 600, 100, 200, 300, 400, 500, 600 }
    };

    //private void PrintPreMove(int plies, Move move, bool isQuiescence = false)
    //{
    //    var sb = new StringBuilder();
    //    for (int i = 0; i <= plies; ++i)
    //    {
    //        sb.Append("\t\t");
    //    }
    //    string depthStr = sb.ToString();

    //    Console.WriteLine($"{Environment.NewLine}{depthStr}{(isQuiescence ? "[Qui] " : "")}{move.ToString()[7..^1]} ({(_position.IsWhiteToMove ? "White" : "Black")}, {plies})");
    //}
}

#pragma warning restore RCS1001 // Add braces (when expression spans over multiple lines).
