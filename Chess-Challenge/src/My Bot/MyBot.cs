using ChessChallenge.API;
using System;
using System.Linq;
using static ChessChallenge.Application.ConsoleHelper;

public class MyBot : IChessBot
{
    /*
     * ----- A small rundown of how the "Supertask Time Troubles" Bot works -----
     * 
     * -- What is a Supertask? --
     * When we talk about a "Supertask", we mean a sequence of countably infinite operations "squeezed" into a finite frame.
     * 
     * An example:
     * A marathon runner is really close to finishing her race but she's also reaaaally groggy. She is two meters away from the finish line, but due to her exhaustion every step that she takes is half as far as her last one.
     * Her first step is a strong 1 meters far. The following only 0.5 meters. The one after that 0.25 meters etcetera etcetera.
     * After how many steps will she reach the goal? Because this is all very philosophical, we need to understand that our marathon runner is actually just a line, or a point - she has no width that would trigger the finish line by proximity.
     * She should reach the goal after an infinite amount of steps, right? But damn, dat's alotta steps.
     * 
     * 
     * -- What do the bot do? --
     * The bot has *terrible* time-management-skills.
     * It treats its time like a Supertask in that it always takes half its remaining time for its turn.
     * This might not seem like a smart strategy (it aint), but I can assure you that the bot will use the first 30 seconds to calculate a *banger* opening move.
     * 
     * I *tried* to make it as competitive as possible. It's hyper-aggressive, so it can use the first moves that still search a few iterations deep to make some impact on the board.
     * It's especially entertaining to see it play against itself. If you have the patience to wait through their respective first moves, the way they ramp up their speed is quite satisfying to watch.
     * 
     * The bot calculates in time increments when they are not zero, making the bot significantly better at chess but also kind of beating the whole Supertask point.
     * 
     * 
     * -- What are its limitations? --
     * There is an unavoidable time-cost between the time measured at the end of a turn and the time measured at the start of the next turn.
     * I calculated this delta cost (_averageDeltaCostBetweenTurnsMS) to be around 16ms on my machine, which is unfortunately quite a lot.
     * To combat this, I included a puffer (_pufferMS) for 80 turns (_estimatedMaxTotalMoves) which decreases every turn and should help to remove the delta-cost from the equasion.
     * 
     * Additionally, computers are not cool enough to be infinitely fast (smh), which is why even without this puffer, we'd *potentially* hit some time constraints.
     * To keep up, the bot will end up playing bollocks moves at random. That should be enough to cover the 60 rounds. Any more than 60 and the puffer will be our bots detriment anyway.
     */

    const int PositiveInfinity = 9999999;
    const int NegativeInfinity = -PositiveInfinity;

    int[] _pieceValues = { 0, 100, 300, 320, 500, 900, 10000 };

    Move _bestMoveOuterScope;
    int _bestEvalOuterScope;

    bool _searchCancelled;

    Board _board;
    Timer _timer;

    // While I measured a delta-cost of 16ms (see comment above), I'ma double it (and give it to the next person) so more computers can keep up.
    const int _averageDeltaCostBetweenTurnsMS = 32;
    const int _estimatedMaxTotalMoves = 80;

    int _timeCeilingMS;
    int _pufferMS;
    int _turnCounter = 0;

    Random _random = new Random();

    public Move Think(Board board, Timer timer)
    {
        Log("--start--");
        _board = board;
        _timer = timer;

        _searchCancelled = false;
        Move[] moves = _board.GetLegalMoves();
        _bestMoveOuterScope = moves[_random.Next(moves.Length)];

        _timeCeilingMS = (int)Math.Ceiling(_timer.MillisecondsRemaining * 0.5);
        _pufferMS = (_estimatedMaxTotalMoves - _turnCounter) * _averageDeltaCostBetweenTurnsMS * 2;

        Log("TimeCeil: " + _timeCeilingMS);

        // TODO: For some reason the depth falls from 5 to 0 instantly for both bots which doesnt make too much sense

        for (int searchDepth = 0; searchDepth < int.MaxValue; searchDepth++)
        {
            SearchMovesRecursive(0, searchDepth, NegativeInfinity, PositiveInfinity);
            Log("finished: " + searchDepth);

            if (_bestEvalOuterScope > PositiveInfinity - 50000 || _searchCancelled) break;
        }

        _turnCounter++;
        return _bestMoveOuterScope;
    }

    int SearchMovesRecursive(int currentDepth, int iterationDepth, int alpha, int beta)
    {
        if (_timer.MillisecondsRemaining - _pufferMS <= _timeCeilingMS) _searchCancelled = true;

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