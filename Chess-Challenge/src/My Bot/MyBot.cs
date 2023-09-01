using ChessChallenge.API;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using static ChessChallenge.Application.ConsoleHelper;

public class MyBot : IChessBot
{
    // Piece values: pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 100, 300, 320, 500, 900, 10000 };

    const int PositiveInfinity = 9999999;
    const int NegativeInfinity = -PositiveInfinity;

    Move _bestMoveOuterScope;
    int _bestEvalOuterScope;

    bool _searchCancelled;

    int _maxTimeElapsed = 1000;
    int _currentMaxTimeElapsed;
    float _timeDepletionThreshold = 0.4f;

    Board _board;
    Timer _timer;

    int _searchCounter = 0;
    int _millisecondsStart;

    public Move Think(Board board, Timer timer)
    {
        _searchCounter = 0;
        int _millisecondsStart = timer.MillisecondsRemaining;

        Log("----- GOOOOOOOD ------");

        _board = board;
        _timer = timer;

        _searchCancelled = false;
        _bestMoveOuterScope = Move.NullMove;

        for (int searchDepth = 1; searchDepth < int.MaxValue; searchDepth++)
        {
            float percentageTimeLeft = timer.MillisecondsRemaining / 60000f;
            _currentMaxTimeElapsed = (percentageTimeLeft >= _timeDepletionThreshold) ? _maxTimeElapsed : (int)(percentageTimeLeft * (_maxTimeElapsed / _timeDepletionThreshold));

            SearchMovesRecursive(0, searchDepth, 0, NegativeInfinity, PositiveInfinity, false);

            // eval so good it's gotta be mate
            if (_bestEvalOuterScope > PositiveInfinity - 50000 || _searchCancelled) break;

            //Log("Best Move iteration: " + searchDepth + " " +_bestMoveOuterScope + "");

            Log("Time at which depth " + searchDepth + " has finished: " + (_millisecondsStart - _timer.MillisecondsRemaining));
        }

        //Log("Final Move: " + _bestMoveOuterScope + "");
        Log("searches: " + _searchCounter);

        return _bestMoveOuterScope;
    }

    int SearchMovesRecursive(int currentDepth, int iterationDepth, int numExtensions, int alpha, int beta, bool capturesOnly)
    {
        _searchCounter++;

        if (_timer.MillisecondsElapsedThisTurn > _currentMaxTimeElapsed) _searchCancelled = true;

        if (_searchCancelled || _board.IsDraw()) return 0;

        if (_board.IsInCheckmate()) return NegativeInfinity + 1;

        if (currentDepth == iterationDepth) return SearchMovesRecursive(++currentDepth, iterationDepth, numExtensions, alpha, beta, true);

        Span<Move> movesToSearch = stackalloc Move[256];
        _board.GetLegalMovesNonAlloc(ref movesToSearch, capturesOnly);

        if (capturesOnly)
        {
            int captureEval = Evaluate();

            if (movesToSearch.Length == 0) return captureEval;

            if (captureEval >= beta) return beta;

            if (captureEval > alpha) alpha = captureEval;
        }

        movesToSearch.Sort((x, y) => Math.Sign(MoveOrderCalculator(currentDepth, y) - MoveOrderCalculator(currentDepth, x)));

        for (int i = 0; i < movesToSearch.Length; i++)
        {
            Move move = movesToSearch[i];

            _board.MakeMove(move);

                //PieceType movedPieceType = move.MovePieceType;
                //int targetRank = move.TargetSquare.Rank;

                //bool promotingSoon = movedPieceType == PieceType.Pawn && (targetRank == 6 || targetRank == 1);

                int extension = (numExtensions < 16 && _board.IsInCheck()) ? 1 : 0;
                int eval = -SearchMovesRecursive(currentDepth + 1, iterationDepth + extension, numExtensions + extension, -beta, -alpha, capturesOnly);
            
            _board.UndoMove(move);

            if (_searchCancelled) return 0;

            if (eval >= beta)return eval;

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

        // diese Umstellung ist verpflichtend -> Ohne sie funktioniert der Search nicht vernünftig.
        if (depth == 0 && move == _bestMoveOuterScope) moveScoreGuess += PositiveInfinity;

        // der Rest der Umstellungen ist optional

        // capture most valuable with least valuable - determine if they are able to recapture afterwards
        if (move.CapturePieceType != PieceType.None)
        {
            int captureMaterialDelta = pieceValues[(int)move.CapturePieceType] - pieceValues[(int)move.MovePieceType];

            moveScoreGuess += (captureMaterialDelta < 0 && _board.SquareIsAttackedByOpponent(move.TargetSquare)) ? 10000 - captureMaterialDelta : 50000 + captureMaterialDelta;
        }
        

        // Does this move attack the square that the enemy king is on? If so -> moveScoreGuess += 2
        // TO BE IMPLEMENTED

        // promote pawns
        if (move.IsPromotion) moveScoreGuess += 30000 + pieceValues[(int)move.PromotionPieceType];

        // dont move into opponent attacked area. Maybe more extreme for pawns?
        if (_board.SquareIsAttackedByOpponent(move.TargetSquare)) moveScoreGuess +=  - 1000 - pieceValues[(int)move.MovePieceType];

        // POSITIVE VALUES -> EARLIER SEARCH

        return moveScoreGuess;
    }

    int Evaluate()
    {
        bool isWhite = _board.IsWhiteToMove;

        // the score is given from the perspective of who's turn it is. Positive -> active mover has advantage
        // evaluate piece positions is important!

        // THESE CALCULATIONS TAKE EFFORT AND THEY LEAD TO A SHALLOWER DEPTH SEARCH. SO MAKE EM COUNT!
        int whiteEval = CountMaterial(_board, true) + ForceKingToCornerEndgameEval(_board, true) + EvaluatePiecePositions(_board, true);
        int blackEval = CountMaterial(_board, false) + ForceKingToCornerEndgameEval(_board, false) + EvaluatePiecePositions(_board, false);

        int perspective = isWhite ? 1 : -1;

        int eval = whiteEval - blackEval;

        return eval * perspective;
    }
    
    int EvaluatePiecePositions(Board board, bool isWhite)
    {
        int eval = 0;

        // Pawns need to move forward (its more complicated but lets try) -- Optimization would be to change the early game
        PieceList pawns = board.GetPieceList(PieceType.Pawn, isWhite);

        foreach (Piece pawn in pawns)
        {
            // Values range from 0 to ~80

            Square pawnSquare = pawn.Square;

            int pawnRank = isWhite ? pawnSquare.Rank : 7 - pawnSquare.Rank;
            int distFromMiddle = Math.Max(3 - pawnSquare.File, pawnSquare.File - 4);

            // if dist from middle is higher make the importance of pawnrank lower
            eval += pawnRank * (6 - distFromMiddle);
        }

        // --- Put these three together in one calculation
        // knights REALLY dont want to be on the outer ranks
        // Bishops kinda wanna be more towards the middle
        // queen doesn't want to be in the corners (too far from the centre)

        foreach (Piece center in board.GetPieceList(PieceType.Knight, isWhite).Concat(board.GetPieceList(PieceType.Bishop, isWhite)).Concat(board.GetPieceList(PieceType.Queen, isWhite)))
        {
            eval -= SquareDistanceToCenter(center.Square) * 2;
        }
        // king needs to stay in the two files closest to home + on the edges until the endgame in which he needs to go to the centre

        return eval;
    }

    int ForceKingToCornerEndgameEval(Board board, bool isWhite)
    {
        int eval = 0;

        int whiteMaterial = CountMaterial(board, true);
        int blackMaterial = CountMaterial(board, false);

        int enemyMaterial = isWhite ? blackMaterial : whiteMaterial;
        int friendlyMaterial = isWhite ? whiteMaterial : blackMaterial;

        //float friendlyEndgameWeight = 1 - Math.Min(1, (friendlyMaterial - 10000) / 2800.0f);
        float enemyEndgameWeight = 1 - Math.Min(1, (enemyMaterial - 10000) / 2800.0f);

        if (friendlyMaterial > enemyMaterial + pieceValues[1] * 2 && enemyEndgameWeight > 0)
        {
            // Move king away from centre
            Square opponentKingSquare = board.GetKingSquare(!isWhite);

            // Range 0-4
            eval += SquareDistanceToCenter(opponentKingSquare); ;

            // move king closer to opponent king when up material

            Square friendlyKingSquare = board.GetKingSquare(isWhite);

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
    
    int CountMaterial(Board board, bool isWhite)
    {
        int material = 0;
        int offset = isWhite ? 0 : 6;

        PieceList[] allPieceLists = board.GetAllPieceLists();
        for (int i = 0; i < allPieceLists.Length / 2; i++)
        {
            PieceList pieceList = allPieceLists[i + offset];

            material += pieceList.Count * pieceValues[i + 1];
        }

        return material;
    }
}


