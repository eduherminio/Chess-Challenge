using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    Timer _timer;
    Board _position;
    int _targetDepth = 1;
    readonly int[] _indexes = new int[128 + 1];
    readonly Move[] _pVTable = new Move[128 * (128 + 1) / 2];
    //readonly int[,] _previousKillerMoves = new int[2, 128];
    //readonly int[,] _killerMoves = new int[2, 128];
    //readonly int[,] _historyMoves = new int[12, 64];

    bool _isFollowingPV;
    bool _isScoringPV;

    public Move Think(Board board, Timer timer)
    {
        _position = board;
        _timer = timer;
        int previousPVIndex = 0;
        _indexes[0] = previousPVIndex;

        for (int i = -1; ++i < _indexes.Length - 1;)
        {
            _indexes[i + 1] = previousPVIndex + 128 - i;
            previousPVIndex = _indexes[i + 1];
        }
        _isScoringPV = false;

        Array.Clear(_pVTable);
        //Array.Clear(_killerMoves);
        //Array.Clear(_historyMoves);

        int alpha = short.MinValue;
        int beta = short.MaxValue;

        Move bestMove = board.GetLegalMoves()[0];
        try
        {
            bool isMateDetected;
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

                bestMove = _pVTable[0] != default ? _pVTable[0] : bestMove;
                //Console.WriteLine($"Depth {depth}: bestmove {bestMove.ToString()[7..^1]}, eval: {bestEvaluation}, PV: {string.Join(", ", _pVTable.TakeWhile(m => !m.IsNull).Select(m => m.ToString()[7..^1]))}");
                //alpha = bestEvaluation - 50;
                //beta = bestEvaluation + 50;

                //Array.Copy(_killerMoves, _previousKillerMoves, _killerMoves.Length);

                ++_targetDepth;
            }
            while (!isMateDetected && timer.MillisecondsElapsedThisTurn < 3_000);
        }
        catch (Exception e)
        {
            ;
        }

        return bestMove;
    }

    int NegaMax(int ply, int alpha, int beta)
    {
        //if (Position.IsThreefoldRepetition(Game.PositionHashHistory) || Position.Is50MovesRepetition(_halfMovesWithoutCaptureOrPawnMove))
        //{
        //    return 0;
        //}

        if (_timer.MillisecondsElapsedThisTurn > 3_000)
        {
            return StaticEvaluation();
        }

        if (ply > _targetDepth)
        {
            return _position.GetLegalMoves().Length > 0
                 ? QuiescenceSearch(ply, alpha, beta)
                 : EvaluateFinalPosition(ply);
        }

        Move bestMove = new Move();

        int pvIndex, nextPvIndex;
        pvIndex = _indexes[ply];
        nextPvIndex = _indexes[ply + 1];
        _pVTable[pvIndex] = new Move();

        var legalMoves = _position.GetLegalMoves();
        foreach (var move in Sort(legalMoves, ply))
        {
            _position.MakeMove(move);

            var evaluation = -NegaMax(ply + 1, -beta, -alpha);

            _position.UndoMove(move);

            // Fail-hard beta-cutoff - refutation found, no need to keep searching this line
            if (evaluation >= beta)
            {
                //if (!move.IsCapture)
                //{
                //    _killerMoves[1, ply] = _killerMoves[0, ply];
                //    _killerMoves[0, ply] = move.RawValue;
                //}

                return beta;
            }

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
                CopyPVTableMoves(pvIndex + 1, nextPvIndex, 128 - ply - 1);
            }
        }

        if (bestMove.IsNull && legalMoves.Length == 0)
        {
            return EvaluateFinalPosition(ply);
        }

        // Node fails low
        return alpha;
    }

    int QuiescenceSearch(int ply, int alpha, int beta)
    {
        //if (Position.IsThreefoldRepetition(Game.PositionHashHistory) || Position.Is50MovesRepetition(_halfMovesWithoutCaptureOrPawnMove))
        //{
        //    return 0;
        //}

        if (ply >= 128)
        {
            return StaticEvaluation();
        }

        var pvIndex = _indexes[ply];
        var nextPvIndex = _indexes[ply + 1];
        _pVTable[pvIndex] = new Move();   // Nulling the first value before any returns

        var staticEvaluation = StaticEvaluation();

        // Fail-hard beta-cutoff (updating alpha after this check)
        if (staticEvaluation >= beta)
        {
            return staticEvaluation;
        }

        // Better move
        if (staticEvaluation > alpha)
        {
            alpha = staticEvaluation;
        }

        var generatedMoves = _position.GetLegalMoves(true);
        if (!generatedMoves.Any())
        {
            return staticEvaluation;
        }

        Move bestMove = new Move();

        foreach (var move in generatedMoves.OrderByDescending(m => Score(m)))
        {
            _position.MakeMove(move);

            var evaluation = -QuiescenceSearch(ply + 1, -beta, -alpha);

            _position.UndoMove(move);

            // Fail-hard beta-cutoff
            if (evaluation >= beta)
            {
                return evaluation; // The refutation doesn't matter, since it'll be pruned
            }

            if (evaluation > alpha)
            {
                alpha = evaluation;
                bestMove = move;

                _pVTable[pvIndex] = move;
                CopyPVTableMoves(pvIndex + 1, nextPvIndex, 128 - ply - 1);
            }
        }

        if (bestMove.IsNull)
        {
            return _position.GetLegalMoves().Length > 0
                ? alpha
                : EvaluateFinalPosition(ply);
        }

        // Node fails low
        return alpha;
    }

    IEnumerable<Move> Sort(Move[] moves, int depth)
    {
        if (_isFollowingPV)
        {
            _isFollowingPV = false;
            foreach (var move in moves)
            {
                if (move == _pVTable[depth])
                {
                    _isFollowingPV = true;
                    _isScoringPV = true;
                    break;
                }
            }
        }

        return moves
            .OrderByDescending(move => Score(move, depth/*, _killerMoves*/));
    }

    int Score(Move move, int depth = 0/*, int[,]? killerMoves = null,  int[,]? historyMoves = null*/)
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

    void CopyPVTableMoves(int target, int source, int moveCountToCopy)
    {
        if (_pVTable[source].IsNull)
        {
            Array.Clear(_pVTable, target, _pVTable.Length - target);
            return;
        }

        Array.Copy(_pVTable, source, _pVTable, target, moveCountToCopy);
    }

    int StaticEvaluation()
    {
        int eval = 0;

        for (int i = 0; ++i < 6;)
        {
            var whiteBitboard = _position.GetPieceBitboard((PieceType)i, true);
            while (whiteBitboard != default)
            {
                BitboardHelper.ClearAndGetIndexOfLSB(ref whiteBitboard);

                eval += MaterialScore[i];
            }

            var blackBitboard = _position.GetPieceBitboard((PieceType)i, false);
            while (blackBitboard != default)
            {
                BitboardHelper.ClearAndGetIndexOfLSB(ref blackBitboard);

                eval -= MaterialScore[i];
            }
        }

        return _position.IsWhiteToMove ? eval : -eval;
    }

    int EvaluateFinalPosition(int ply) => _position.IsInCheck()
                ? -30_000 + 10 * ply
                : 0;

    static readonly int[] MaterialScore = new int[6]
    {
        0,
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
}