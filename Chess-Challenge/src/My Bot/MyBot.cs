using ChessChallenge.API;
using System;
using System.Linq;
using static ChessChallenge.Application.ConsoleHelper;

public class MyBot : IChessBot
{
    const int PositiveInfinity = 9999999;
    const int NegativeInfinity = -PositiveInfinity;

    int[] _pieceValues = { 0, 100, 300, 320, 500, 900, 10000 };

    Move _bestMoveOuterScope;
    int _bestEvalOuterScope;

    bool _searchCancelled;

    Board _board;
    Timer _timer;

    /*
     * The program (unavoidably) has a time cost
     */
    const int _averageMoveMakingCostMS = 16;
    const int _estimatedMaxTotalMoves = 80;
    const int _pufferMS = _averageMoveMakingCostMS * _estimatedMaxTotalMoves;
    int _timeCeilingMS = 60000 - _pufferMS;

    int _lastEndMS;
    int _deltaMS;

    Random _random = new Random();


    public Move Think(Board board, Timer timer)
    {
        int startMS = timer.MillisecondsRemaining;
        _deltaMS = _lastEndMS - startMS;
        Log("delta: " + _deltaMS);

        _board = board;
        _timer = timer;

        _searchCancelled = false;
        Move[] moves = _board.GetLegalMoves();
        _bestMoveOuterScope = moves[_random.Next(moves.Length)];

        _timeCeilingMS = (int)Math.Ceiling(_timeCeilingMS * 0.5);
        Log(_timeCeilingMS + "");

        while (true)
        {
            if (_timer.MillisecondsElapsedThisTurn > 100) break;

            //if (_timer.MillisecondsRemaining - _pufferMS <= _timeCeilingMS) break;
        }

        //Log("End: " + _timer.MillisecondsRemaining);

        _lastEndMS = _timer.MillisecondsRemaining;

        return _bestMoveOuterScope;

        for (int searchDepth = 0; searchDepth < int.MaxValue; searchDepth++)
        {
            SearchMovesRecursive(0, searchDepth, NegativeInfinity, PositiveInfinity);

            if (_bestEvalOuterScope > PositiveInfinity - 50000 || _searchCancelled) break;
        }

        return _bestMoveOuterScope;
    }

    int SearchMovesRecursive(int currentDepth, int iterationDepth, int alpha, int beta)
    {
        if (_timer.MillisecondsRemaining <= _timeCeilingMS) _searchCancelled = true;

        if (_searchCancelled || _board.IsDraw()) return 0;

        if (_board.IsInCheckmate()) return NegativeInfinity + 1;

        if (currentDepth != 0 && _board.GameRepetitionHistory.Contains(_board.ZobristKey)) return 0;        

        if (currentDepth == iterationDepth) return Evaluate();

        Move[] movesToSearch = _board.GetLegalMoves();

        Random rng = new();
        movesToSearch = movesToSearch.OrderBy(e => rng.Next()).ToArray();
        Array.Sort(movesToSearch, (x, y) => Math.Sign(MoveOrderCalculator(currentDepth, y) - MoveOrderCalculator(currentDepth, x)));

        for (int i = 0; i < movesToSearch.Length; i++)
        {
            Move move = movesToSearch[i];

            _board.MakeMove(move);

                int eval = -SearchMovesRecursive(currentDepth + 1, iterationDepth, -beta, -alpha);
            
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

        if (_board.SquareIsAttackedByOpponent(move.TargetSquare)) moveScoreGuess +=  - 1000 - _pieceValues[(int)move.MovePieceType];

        return moveScoreGuess;
    }

    int Evaluate()
    {
        bool isWhite = _board.IsWhiteToMove;

        int[] evals = new[] { 0, 0 };

        evals[0] += CountMaterial(true);
        evals[1] += CountMaterial(false);

        evals[0] += ForceKingToCornerEndgameEval(evals[0], evals[1], true);
        evals[1] += ForceKingToCornerEndgameEval(evals[1], evals[0], false);

        evals[0] += EvaluatePiecePositions(true);
        evals[1] += EvaluatePiecePositions(false);

        return (evals[0] - evals[1]) * (isWhite ? 1 : -1);
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

            eval += pawnRank * (6 - distFromMiddle);
        }

        foreach (Piece piece in _board.GetPieceList(PieceType.Knight, isWhite).Concat(_board.GetPieceList(PieceType.Bishop, isWhite)).Concat(_board.GetPieceList(PieceType.Queen, isWhite)).Concat(_board.GetPieceList(PieceType.Rook, isWhite)))
        {
            eval -= SquareDistanceToCenter(piece.Square) * 2;
        }
        
        return eval;
    }

    int ForceKingToCornerEndgameEval(int whiteMaterial, int blackMaterial, bool isWhite)
    {
        int eval = 0;

        int enemyMaterial = isWhite ? blackMaterial : whiteMaterial;
        int friendlyMaterial = isWhite ? whiteMaterial : blackMaterial;

        float enemyEndgameWeight = 1 - Math.Min(1, (enemyMaterial - 10000) / 2500.0f);

        if (friendlyMaterial > enemyMaterial + _pieceValues[1] * 2 && enemyEndgameWeight > 0)
        {
            Square opponentKingSquare = _board.GetKingSquare(!isWhite);

            eval += SquareDistanceToCenter(opponentKingSquare); ;

            Square friendlyKingSquare = _board.GetKingSquare(isWhite);

            int dstBetweenKingsFile = Math.Abs(friendlyKingSquare.File - opponentKingSquare.File);
            int dstBetweenKingsRank = Math.Abs(friendlyKingSquare.Rank - opponentKingSquare.Rank);
            int dstBetweenKings = dstBetweenKingsFile + dstBetweenKingsRank;

            eval += 14 - dstBetweenKings;
        }

        return (int)(eval * 20 * enemyEndgameWeight);
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
        int offset = isWhite ? 0 : 6;

        PieceList[] allPieceLists = _board.GetAllPieceLists();
        for (int i = 0; i < 6; i++)
        {
            PieceList pieceList = allPieceLists[i + offset];
            material += pieceList.Count * _pieceValues[i + 1];
        }

        return material;
    }
}