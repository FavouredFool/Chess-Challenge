using ChessChallenge.API;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;
using static ChessChallenge.Application.ConsoleHelper;

public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 100, 300, 300, 600, 900, 10000 };

    const int _maxSearchDepth = 5;

    const int PositiveInfinity = 9999999;
    const int NegativeInfinity = -PositiveInfinity;

    Move _bestMoveOuterScope;

    public Move Think(Board board, Timer timer)
    {
        SearchMovesRecursive(0, NegativeInfinity, PositiveInfinity, board, false);

        return _bestMoveOuterScope;
    }

    public int MoveOrderCalculator(Move move, Board board)
    {
        int moveScoreGuess = 0;

        // capture most valuable with least valuable
        if (move.CapturePieceType != PieceType.None)
        {
            moveScoreGuess = 10 * pieceValues[(int)move.CapturePieceType] - pieceValues[(int)move.MovePieceType];
        }

        // promote pawns
        if (move.IsPromotion)
        {
            moveScoreGuess += pieceValues[(int)move.PromotionPieceType];
        }

        // dont move into opponent pawn area
        if (board.SquareIsAttackedByOpponent(move.TargetSquare))
        {
            moveScoreGuess -= pieceValues[(int)move.MovePieceType];
        }

        return moveScoreGuess;
    }

    int SearchMovesRecursive(int depth, int alpha, int beta, Board board, bool capturesOnly)
    {
        if (depth == _maxSearchDepth) return SearchMovesRecursive(depth + 1, alpha, beta, board, true);

        if (capturesOnly)
        {
            int captureEval = Evaluate(board);

            if (captureEval >= beta) return beta;

            if (captureEval > alpha) alpha = captureEval;
        }

        // Get all moves
        Move[] movesToSearch = board.GetLegalMoves(capturesOnly);

        if (movesToSearch.Length == 0)
        {
            if (board.IsInStalemate()) return 0;

            if (board.IsInCheckmate()) return NegativeInfinity + 1;

            // this might be slow because you evaluate the same position more than once. It will likely not leave too big of an impact
            return Evaluate(board);
        }

        movesToSearch = RandomizeAndOrderMoves(movesToSearch, board);

        Move localBestMoveSoFar = movesToSearch[0];

        foreach (Move captureMove in movesToSearch)
        {
            int eval;

            board.MakeMove(captureMove);
            eval = -SearchMovesRecursive(depth + 1, -beta, -alpha, board, capturesOnly);
            board.UndoMove(captureMove);

            if (eval >= beta)
            {
                // This can never be called on depth == 0
                return beta;
            }

            if (eval > alpha)
            {
                alpha = eval;
                localBestMoveSoFar = captureMove;
            }
        }

        if (depth == 0)
        {
            _bestMoveOuterScope = localBestMoveSoFar;
        }
       
        return alpha;
    }

    Move[] RandomizeAndOrderMoves(Move[] allMoves, Board board)
    {
        // randomize
        Random rng = new();
        Move[] randomMove = allMoves.OrderBy(e => rng.Next()).ToArray();
        // then order
        Array.Sort(randomMove, (x, y) => Math.Sign(MoveOrderCalculator(y, board) - MoveOrderCalculator(x, board)));
        return randomMove;
    }
    
    int Evaluate(Board board)
    {
        bool isWhite = board.IsWhiteToMove;

        // the score is given from the perspective of who's turn it is. Positive -> active mover has advantage
        int whiteEval = CountMaterial(board, true) + ForceKingToCornerEndgameEval(board, true) + EvaluatePiecePositions(board, true);
        int blackEval = CountMaterial(board, false) + ForceKingToCornerEndgameEval(board, false) + EvaluatePiecePositions(board, false);

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
            eval -= SquareDistanceToCenter(center.Square) * 3;
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

            // Range 6-13
            eval += 14 - dstBetweenKings;
        }

        return (int)(eval * 15 * enemyEndgameWeight);
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
