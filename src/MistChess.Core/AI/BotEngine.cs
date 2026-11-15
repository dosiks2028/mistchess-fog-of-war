using System.Collections.Immutable;
using MistChess.Core.Domain;
using MistChess.Core.Engine;
using MistChess.Core.Functional;

namespace MistChess.Core.AI;

public static class BotEngine
{
    private const int MateScore = 1_000_000;

    public static Result<Move, BotError> ChooseMove(
        MaskedBoardState observation,
        ImmutableList<Move> legalMoves,
        BotProfile profile,
        ulong seed = 0,
        FogRule? fogRule = null)
    {
        if (observation.Turn != observation.Viewer)
        {
            return Result<Move, BotError>.Failure(new BotError.NotPlayersTurn(observation.Viewer, observation.Turn));
        }

        if (legalMoves.IsEmpty)
        {
            return Result<Move, BotError>.Failure(new BotError.NoLegalMoves());
        }

        var rule = fogRule ?? FogRules.Standard;
        var beliefBoard = ToBeliefBoard(observation);
        var cache = ImmutableDictionary<ulong, int>.Empty;
        var bestMove = legalMoves[0];
        var bestScore = int.MinValue;
        var alpha = int.MinValue + 1;

        foreach (var move in OrderMoves(observation, legalMoves))
        {
            var applied = GameEngine.ApplyForSearch(beliefBoard, move);
            var search = Search(
                applied,
                Math.Max(0, profile.SearchDepth - 1),
                alpha,
                int.MaxValue - 1,
                observation.Viewer,
                profile.HeuristicEvaluator,
                rule,
                cache,
                ply: 1);
            cache = search.Transpositions;
            var noise = RandomNoise(seed, move, profile.RandomnessFactor);
            var score = search.Score + noise;
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }

            alpha = Math.Max(alpha, score);
        }

        return Result<Move, BotError>.Success(bestMove);
    }

    private static SearchResult Search(
        BoardState board,
        int depth,
        int alpha,
        int beta,
        PlayerColor perspective,
        Func<MaskedBoardState, PlayerColor, int> evaluator,
        FogRule fogRule,
        ImmutableDictionary<ulong, int> transpositions,
        int ply)
    {
        if (board.Status is GameStatus.Won won)
        {
            return new SearchResult(won.Winner == perspective ? MateScore - ply : -MateScore + ply, transpositions);
        }

        if (board.Status is GameStatus.Draw)
        {
            return new SearchResult(0, transpositions);
        }

        var observation = FogEngine.ProjectBoard(board, perspective, fogRule);
        var moves = MoveGenerator.GetLegalMoves(board, board.Turn);
        if (moves.IsEmpty)
        {
            var terminalScore = MoveGenerator.IsInCheck(board, board.Turn)
                ? board.Turn == perspective ? -MateScore + ply : MateScore - ply
                : 0;
            return new SearchResult(terminalScore, transpositions);
        }

        if (depth == 0)
        {
            return new SearchResult(evaluator(observation, perspective), transpositions);
        }

        var key = ComputeZobristKey(board, depth, perspective);
        if (transpositions.TryGetValue(key, out var cachedScore))
        {
            return new SearchResult(cachedScore, transpositions);
        }

        var maximizing = board.Turn == perspective;
        var best = maximizing ? int.MinValue + 1 : int.MaxValue - 1;
        var currentAlpha = alpha;
        var currentBeta = beta;
        var currentTable = transpositions;
        var cutoff = false;

        foreach (var move in OrderMoves(observation, moves))
        {
            var child = GameEngine.ApplyForSearch(board, move);
            var childResult = Search(child, depth - 1, currentAlpha, currentBeta, perspective, evaluator, fogRule, currentTable, ply + 1);
            currentTable = childResult.Transpositions;
            if (maximizing)
            {
                best = Math.Max(best, childResult.Score);
                currentAlpha = Math.Max(currentAlpha, best);
            }
            else
            {
                best = Math.Min(best, childResult.Score);
                currentBeta = Math.Min(currentBeta, best);
            }

            if (currentBeta <= currentAlpha)
            {
                cutoff = true;
                break;
            }
        }

        if (!cutoff)
        {
            currentTable = currentTable.SetItem(key, best);
        }

        return new SearchResult(best, currentTable);
    }

    private static ImmutableList<Move> OrderMoves(MaskedBoardState board, IEnumerable<Move> moves) => moves
        .OrderByDescending(move => move switch
        {
            PromotionMove promotion => 10_000 + (int)promotion.PromotedTo * 10,
            CastlingMove => 150,
            EnPassantMove => 120,
            CaptureMove when board.At(move.To) is SquareVisibility.Visible { Piece: { } target } => 100 + (int)target.Kind * 10,
            CaptureMove => 100,
            _ => 0
        })
        .ThenBy(move => move.From)
        .ThenBy(move => move.To)
        .ToImmutableList();

    private static BoardState ToBeliefBoard(MaskedBoardState observation)
    {
        var pieces = observation.Squares
            .Where(entry => entry.Value is SquareVisibility.Visible { Piece: not null })
            .ToImmutableDictionary(entry => entry.Key, entry => ((SquareVisibility.Visible)entry.Value).Piece!);
        return new BoardState(
            pieces,
            observation.Turn,
            CastlingRights.None,
            status: GameStatus.Playing,
            history: ImmutableList<Move>.Empty,
            variant: observation.Variant);
    }

    private static int RandomNoise(ulong seed, Move move, double randomnessFactor)
    {
        if (randomnessFactor <= 0)
        {
            return 0;
        }

        var moveCode = (ulong)(move.From.File | move.From.Rank << 3 | move.To.File << 6 | move.To.Rank << 9);
        moveCode ^= (ulong)move.GetType().Name.Aggregate(0, (hash, character) => unchecked(hash * 31 + character));
        if (move is PromotionMove promotion)
        {
            moveCode ^= (ulong)promotion.PromotedTo << 16;
        }

        var noiseSpan = Math.Max(1, (int)Math.Round(randomnessFactor * 2_000));
        var mixed = Mix(seed ^ moveCode);
        return (int)(mixed % (uint)(noiseSpan * 2 + 1)) - noiseSpan;
    }

    private static ulong ComputeZobristKey(BoardState board, int depth, PlayerColor perspective)
    {
        var hash = Mix((ulong)depth ^ ((ulong)perspective << 8) ^ ((ulong)board.Turn << 16));
        foreach (var (position, piece) in board.Pieces)
        {
            var pieceCode = (ulong)(position.File + (position.Rank << 3)
                + ((int)piece.Kind << 6) + ((int)piece.Color << 10));
            hash ^= Mix(pieceCode + 0x9E3779B97F4A7C15UL);
        }

        hash ^= Mix((ulong)board.CastlingRights << 24);
        if (board.EnPassantTarget is { } target)
        {
            hash ^= Mix((ulong)(target.File + target.Rank * 8) << 32);
        }

        return hash;
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private sealed record SearchResult(int Score, ImmutableDictionary<ulong, int> Transpositions);
}
