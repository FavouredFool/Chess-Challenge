using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    using ChessChallenge.API;
    using System;
    using System.Numerics;
    using System.Collections.Generic;
    using System.Linq;
    using Raylib_cs;
    using static ChessChallenge.Application.ConsoleHelper;
    using System.Diagnostics;

    public class EvilBot : IChessBot
    {
        // Piece values: pawn, knight, bishop, rook, queen, king
        int[] pieceValues = { 0, 100, 250, 300, 500, 900, 10000 };

        const int _maxSearchDepth = 5;

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
                moveScoreGuess = 10 * GetPieceValue(move.CapturePieceType) - GetPieceValue(move.MovePieceType);
            }

            // promote pawns
            if (move.IsPromotion)
            {
                moveScoreGuess += GetPieceValue(move.PromotionPieceType);
            }

            // dont move into opponent pawn area
            if (board.SquareIsAttackedByOpponent(move.TargetSquare))
            {
                moveScoreGuess -= GetPieceValue(move.MovePieceType);
            }

            return moveScoreGuess;
        }

        public int GetPieceValue(PieceType type)
        {
            return pieceValues[(int)type];
        }

        MoveValue SearchAllCaptures(int alpha, int beta, Board board)
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

            Random rng = new();
            Move[] randomMoves = captureMoves.OrderBy(e => rng.Next()).ToArray();
            Array.Sort(randomMoves, (x, y) => Math.Sign(MoveOrderCalculator(y, board) - MoveOrderCalculator(x, board)));
            captureMoves = randomMoves;

            Move localBestCaptureSoFar = captureMoves[0];
            List<Move> localPastBestCapturesSoFar = new();

            foreach (Move captureMove in captureMoves)
            {
                board.MakeMove(captureMove);
                MoveValue moveValue = SearchAllCaptures(-beta, -alpha, board);
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
                    localBestCaptureSoFar = captureMove;
                    localPastBestCapturesSoFar = moveValue.Moves;
                }
            }

            List<Move> totalMovesSoFar = new() { localBestCaptureSoFar };
            totalMovesSoFar.AddRange(localPastBestCapturesSoFar);

            return new MoveValue(totalMovesSoFar, alpha);
        }


        MoveValue Search(int depth, int alpha, int beta, Board board)
        {
            if (depth == _maxSearchDepth)
            {
                return SearchAllCaptures(alpha, beta, board);
                //return new(new(), Evaluate(board));
            }

            if (board.IsInStalemate())
            {
                return new MoveValue(new(), 0);
            }

            if (board.IsInCheckmate())
            {
                return new MoveValue(new(), NegativeInfinity + 5);
            }

            // New Move needs to be added from here on because it goes deeper
            Move[] allMoves = board.GetLegalMoves();

            // randomize
            Random rng = new();
            Move[] randomMove = allMoves.OrderBy(e => rng.Next()).ToArray();

            // then order
            Array.Sort(randomMove, (x, y) => Math.Sign(MoveOrderCalculator(y, board) - MoveOrderCalculator(x, board)));

            allMoves = randomMove;

            Move localBestMoveSoFar = allMoves[0];
            List<Move> localBestPastMovesSoFar = new();

            foreach (Move move in allMoves)
            {
                board.MakeMove(move);
                MoveValue currentMoveValue = Search(depth + 1, -beta, -alpha, board);

                // invert the eval
                currentMoveValue.Eval = currentMoveValue.Eval * -1;

                board.UndoMove(move);

                if (currentMoveValue.Eval >= beta)
                {
                    // This can never be called on depth == 0
                    return new MoveValue(new(), beta);
                }

                if (currentMoveValue.Eval > alpha)
                {
                    localBestMoveSoFar = move;

                    alpha = currentMoveValue.Eval;
                    localBestPastMovesSoFar = currentMoveValue.Moves;
                }
            }

            List<Move> totalMovesSoFar = new() { localBestMoveSoFar };
            totalMovesSoFar.AddRange(localBestPastMovesSoFar);

            return new MoveValue(totalMovesSoFar, alpha);
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
            // the score is given from the perspective of who's turn it is. Positive -> active mover has advantage
            int whiteEval = CountMaterial(board, true) + ForceKingToCornerEndgameEval(board, true);
            int blackEval = CountMaterial(board, false) + ForceKingToCornerEndgameEval(board, false);

            int perspective = board.IsWhiteToMove ? 1 : -1;

            int eval = whiteEval - blackEval;

            return eval * perspective;
        }

        int ForceKingToCornerEndgameEval(Board board, bool isWhite)
        {
            int eval = 0;

            int enemyMaterial = CountMaterial(board, !isWhite);
            int myMaterial = CountMaterial(board, isWhite);

            // unter 2000 gesamtpiecevalue beginnt der Endgame-Fade
            float enemyEndgameWeight = 1 - Math.Min(1, (enemyMaterial - 10000) / 2800.0f);

            if (myMaterial > enemyMaterial + pieceValues[1] * 2 && enemyEndgameWeight > 0)
            {
                // Move king away from centre
                Square opponentKingSquare = board.GetKingSquare(!isWhite);

                int opponentKingDstToCentreFile = Math.Max(3 - opponentKingSquare.File, opponentKingSquare.File - 4);
                int opponentKingDstToCentreRank = Math.Max(3 - opponentKingSquare.Rank, opponentKingSquare.Rank - 4);
                int opponentKingDstToCentre = opponentKingDstToCentreRank + opponentKingDstToCentreFile;

                // Range 0-4
                eval += opponentKingDstToCentre;

                // move king closer to opponent king when up material

                Square friendlyKingSquare = board.GetKingSquare(isWhite);

                int dstBetweenKingsFile = Math.Abs(friendlyKingSquare.File - opponentKingSquare.File);
                int dstBetweenKingsRank = Math.Abs(friendlyKingSquare.Rank - opponentKingSquare.Rank);
                int dstBetweenKings = dstBetweenKingsFile + dstBetweenKingsRank;

                // Range 6-13
                eval += 14 - dstBetweenKings;
            }

            return (int)(eval * 10 * enemyEndgameWeight);
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