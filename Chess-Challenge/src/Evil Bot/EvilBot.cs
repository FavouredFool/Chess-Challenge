using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class EvilBot : IChessBot
    {
        // Piece values: pawn, knight, bishop, rook, queen, king
        int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };

        const int _maxSearchDepth = 4;

        const int PositiveInfinity = 9999999;
        const int NegativeInfinity = -PositiveInfinity;

        struct MoveValue
        {
            public MoveValue(List<Move> moves, int eval)
            {
                Moves = moves;
                Eval = eval;
            }

            public List<Move> Moves;
            public int Eval;
        }

        public Move Think(Board board, Timer timer)
        {
            List<Move> allMovesToDepth = Search(0, NegativeInfinity, PositiveInfinity, board).Moves;

            //LogSequence(allMovesToDepth, board);

            return allMovesToDepth[0];
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

        MoveValue SearchAllCaptures(int depth, int alpha, int beta, Board board)
        {
            int eval = Evaluate(board);

            if (eval >= beta)
            {
                return new MoveValue(new(), beta);
            }
            if (eval > alpha)
            {
                alpha = eval;
            }

            Move[] captureMoves = board.GetLegalMoves(true);

            if (captureMoves.Length == 0)
            {
                return new MoveValue(new(), eval);
            }

            captureMoves = RandomizeAndOrderMoves(captureMoves, board);

            return SearchMovesRecursive(captureMoves, depth, alpha, beta, board, true);
        }

        MoveValue Search(int depth, int alpha, int beta, Board board)
        {
            if (depth == _maxSearchDepth) return SearchAllCaptures(depth + 1, alpha, beta, board);


            Move[] allMoves = board.GetLegalMoves();

            if (board.IsInStalemate()) return new MoveValue(new(), 0);

            if (board.IsInCheckmate()) return new MoveValue(new(), NegativeInfinity + 1);

            if (depth != 0 && board.GameRepetitionHistory.Contains(board.ZobristKey)) return new MoveValue(new(), 0);

            allMoves = RandomizeAndOrderMoves(allMoves, board);

            return SearchMovesRecursive(allMoves, depth, alpha, beta, board, false);
        }

        MoveValue SearchMovesRecursive(Move[] movesToSearch, int depth, int alpha, int beta, Board board, bool capturesOnly)
        {
            int eval;

            Move localBestMoveSoFar = movesToSearch[0];
            List<Move> localPastBestMovesSoFar = new();

            foreach (Move captureMove in movesToSearch)
            {
                MoveValue moveValue;

                board.MakeMove(captureMove);
                if (capturesOnly)
                {
                    moveValue = SearchAllCaptures(depth + 1, -beta, -alpha, board);
                }
                else
                {
                    moveValue = Search(depth + 1, -beta, -alpha, board);
                }

                board.UndoMove(captureMove);

                moveValue.Eval = moveValue.Eval * -1;
                eval = moveValue.Eval;

                if (eval >= beta)
                {
                    // This can never be called on depth == 0
                    return new MoveValue(new(), beta);
                }

                if (eval > alpha)
                {
                    alpha = eval;
                    localBestMoveSoFar = captureMove;
                    localPastBestMovesSoFar = moveValue.Moves;
                }
            }

            List<Move> totalMovesSoFar = new() { localBestMoveSoFar };
            totalMovesSoFar.AddRange(localPastBestMovesSoFar);

            return new MoveValue(totalMovesSoFar, alpha);
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

        /*
        void LogSequence(List<Move> allBestMoves, Board board)
        {
            Log("-----------------START------------------");

            Log("Start Eval: " + Evaluate(board));
            Log(board.CreateDiagram());

            for(int i=0; i<allBestMoves.Count; i++)
            {
                Move move = allBestMoves[i];

                board.MakeMove(move);

                Log("Depth " + i + "\n" + move + "\nEval: " + Evaluate(board));

                Log(board.CreateDiagram());
            }

            for (int i= allBestMoves.Count-1; i>=0;i--)
            {
                Move move = allBestMoves[i];
                board.UndoMove(move);
            }
        }
        */

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
}