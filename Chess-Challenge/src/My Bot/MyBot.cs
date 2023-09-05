using ChessChallenge.API;
using System;
using System.Linq;

public class MyBot : IChessBot
{
    const int PositiveInfinity = 9999999;
    const int NegativeInfinity = -PositiveInfinity;

    int[] _pieceValues = { 0, 100, 300, 320, 500, 900, 10000 };

    Move _bestMoveOuterScope;
    int _bestEvalOuterScope;

    bool _searchCancelled;

    int _maxTimeElapsed = 150;
    int _timeCeilingMS;
    float _timeDepletionThreshold = 0.4f;

    Board _board;
    Timer _timer;

    Random _random = new();

    public Move Think(Board board, Timer timer)
    {
        _board = board;
        _timer = timer;

        _searchCancelled = false;
        _bestMoveOuterScope = Move.NullMove;
        _bestEvalOuterScope = NegativeInfinity;

        int remainingMS = _timer.MillisecondsRemaining;

        float percentageTimeLeft = remainingMS / 60000f;
        int dynamicMaxTimeElapsed = (percentageTimeLeft >= _timeDepletionThreshold) ? _maxTimeElapsed : (int)(percentageTimeLeft * (_maxTimeElapsed / _timeDepletionThreshold));

        _timeCeilingMS = remainingMS - dynamicMaxTimeElapsed;

        for (int searchDepth = 1; searchDepth < int.MaxValue; searchDepth++)
        {
            SearchMovesRecursive(0, searchDepth, 0, NegativeInfinity, PositiveInfinity, false);

            if (_bestEvalOuterScope > PositiveInfinity - 50000 || _searchCancelled) break;
        }

        return _bestMoveOuterScope;
    }

    int SearchMovesRecursive(int currentDepth, int iterationDepth, int numExtensions, int alpha, int beta, bool capturesOnly)
    {
        if (_timer.MillisecondsRemaining < _timeCeilingMS) _searchCancelled = true;

        if (_searchCancelled || _board.IsDraw()) return 0;

        if (_board.IsInCheckmate()) return NegativeInfinity + 1;

        if (currentDepth != 0 && _board.GameRepetitionHistory.Contains(_board.ZobristKey)) return 0;

        if (currentDepth == iterationDepth) return SearchMovesRecursive(++currentDepth, iterationDepth, numExtensions, alpha, beta, true);

        // no span because I dont think i can properly shuffle the span without first creating an array :c
        Move[] movesToSearch = _board.GetLegalMoves(capturesOnly);

        if (capturesOnly)
        {
            int captureEval = Evaluate();

            if (movesToSearch.Length == 0) return captureEval;

            if (captureEval >= beta) return beta;

            if (captureEval > alpha) alpha = captureEval;
        }

        // Shuffle so that moves with same eval are not deterministic
        movesToSearch = movesToSearch.OrderBy(e => _random.Next()).ToArray();
        Array.Sort(movesToSearch, (x, y) => Math.Sign(MoveOrderCalculator(currentDepth, y) - MoveOrderCalculator(currentDepth, x)));

        for (int i = 0; i < movesToSearch.Length; i++)
        {
            Move move = movesToSearch[i];

            _board.MakeMove(move);

            int extension = (numExtensions < 16 && _board.IsInCheck()) ? 1 : 0;
            int eval = -SearchMovesRecursive(currentDepth + 1, iterationDepth + extension, numExtensions + extension, -beta, -alpha, capturesOnly);

            _board.UndoMove(move);

            if (_searchCancelled) return 0;

            if (eval >= beta) return eval;

            if (eval > alpha)
            {
                alpha = eval;

                if (currentDepth == 0)
                {
                    _bestMoveOuterScope = move;
                    _bestEvalOuterScope = eval;
                }
            }
        }

        return alpha;
    }

    public int MoveOrderCalculator(int depth, Move move)
    {
        int moveScoreGuess = 0;

        if (depth == 0 && move == _bestMoveOuterScope) moveScoreGuess += PositiveInfinity;

        if (move.CapturePieceType != PieceType.None)
        {
            int captureMaterialDelta = 10 * _pieceValues[(int)move.CapturePieceType] - _pieceValues[(int)move.MovePieceType];
            moveScoreGuess += (captureMaterialDelta < 0 && _board.SquareIsAttackedByOpponent(move.TargetSquare)) ? 10000 - captureMaterialDelta : 50000 + captureMaterialDelta;
        }

        _board.MakeMove(move);
        if (_board.SquareIsAttackedByOpponent(_board.GetKingSquare(_board.IsWhiteToMove))) moveScoreGuess += 5000;
        _board.UndoMove(move);

        if (move.IsPromotion) moveScoreGuess += 30000 + _pieceValues[(int)move.PromotionPieceType];

        if (_board.SquareIsAttackedByOpponent(move.TargetSquare)) moveScoreGuess += -1000 - _pieceValues[(int)move.MovePieceType];

        return moveScoreGuess;
    }

    int Evaluate()
    {
        bool isWhite = _board.IsWhiteToMove;

        int[] evals = new[] { 0, 0 };

        int friendlyMaterialIndex = isWhite ? 0 : 1;
        int enemyMaterialIndex = (friendlyMaterialIndex + 1) % 2;

        evals[0] += CountMaterial(true);
        evals[1] += CountMaterial(false);

        float enemyEndgameWeight = 1 - Math.Min(1, (evals[enemyMaterialIndex] - 10000) / 2800.0f);
        float disadvantageReduction = Math.Min(1, (evals[friendlyMaterialIndex] - 10000) / ((float)(evals[enemyMaterialIndex] - 10000)));

        enemyEndgameWeight *= disadvantageReduction;

        evals[0] += ForceKingToCornerEndgameEval(enemyEndgameWeight, true);
        evals[1] += ForceKingToCornerEndgameEval(enemyEndgameWeight, false);

        evals[0] += EvaluatePiecePositions(true);
        evals[1] += EvaluatePiecePositions(false);

        return evals[friendlyMaterialIndex] - evals[enemyMaterialIndex];
    }

    int EvaluatePiecePositions(bool isWhite)
    {
        int eval = 0;

        PieceList pawns = _board.GetPieceList(PieceType.Pawn, isWhite);

        foreach (Piece pawn in pawns)
        {
            Square pawnSquare = pawn.Square;

            int pawnRank = isWhite ? pawnSquare.Rank : 7 - pawnSquare.Rank;
            int distFromMiddle = Math.Max(3 - pawnSquare.File, pawnSquare.File - 4);

            eval += (int) (pawnRank * (6 - distFromMiddle / 4f));
        }

        foreach (Piece piece in _board.GetPieceList(PieceType.Knight, isWhite).Concat(_board.GetPieceList(PieceType.Bishop, isWhite)).Concat(_board.GetPieceList(PieceType.Queen, isWhite)).Concat(_board.GetPieceList(PieceType.Rook, isWhite)))
        {
            eval -= SquareDistanceToCenter(piece.Square) * 2;
        }

        return eval;
    }

    int ForceKingToCornerEndgameEval(float enemyEndgameWeight, bool isWhite)
    {
        int eval = 0;

        if (enemyEndgameWeight > 0)
        {
            Square opponentKingSquare = _board.GetKingSquare(!isWhite);

            eval += SquareDistanceToCenter(opponentKingSquare) * 10;

            Square friendlyKingSquare = _board.GetKingSquare(isWhite);

            int dstBetweenKingsFile = Math.Abs(friendlyKingSquare.File - opponentKingSquare.File);
            int dstBetweenKingsRank = Math.Abs(friendlyKingSquare.Rank - opponentKingSquare.Rank);
            int dstBetweenKings = dstBetweenKingsFile + dstBetweenKingsRank;

            eval += (14 - dstBetweenKings) * 6;
        }

        return (int)(eval * enemyEndgameWeight);
    }

    public int SquareDistanceToCenter(Square square)
    {
        int squareDistToCenterFile = Math.Max(3 - square.File, square.File - 4);
        int squareDistToCenterRank = Math.Max(3 - square.Rank, square.Rank - 4);
        int squareDistToCentre = squareDistToCenterRank + squareDistToCenterFile;

        return squareDistToCentre;
    }

    int CountMaterial(bool isWhite)
    {
        int material = 0;

        PieceList[] allPieceLists = _board.GetAllPieceLists();
        for (int i = 0; i < 6; i++)
        {
            PieceList pieceList = allPieceLists[i + (isWhite ? 0 : 6)];
            material += pieceList.Count * _pieceValues[i + 1];
        }

        return material;
    }
}
