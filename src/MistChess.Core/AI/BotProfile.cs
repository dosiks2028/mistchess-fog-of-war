using System.Collections.Immutable;
using MistChess.Core.Domain;
using MistChess.Core.Engine;

namespace MistChess.Core.AI;

public sealed record BotProfile(
    string Id,
    string Name,
    string Title,
    string Description,
    int SearchDepth,
    Func<MaskedBoardState, PlayerColor, int> HeuristicEvaluator,
    double RandomnessFactor);

public static class BotProfiles
{
    public static BotProfile MistWalker { get; } = new(
        "mist-walker",
        "Mist Walker",
        "Novice / Chaos",
        "A wandering beginner who favors variety and sometimes notices a loose visible piece.",
        1,
        BotHeuristics.MistWalker,
        0.9);

    public static BotProfile ZabuzasGambit { get; } = new(
        "zabuzas-gambit",
        "Zabuza’s Gambit",
        "Scout / Aggressor",
        "Pushes forward, seeks visible captures, and values the ground revealed by its pieces.",
        1,
        BotHeuristics.ZabuzasGambit,
        0.12);

    public static BotProfile DementorsFork { get; } = new(
        "dementors-fork",
        "Dementor’s Fork",
        "Tactician",
        "Looks two plies ahead for forks and coordinated attacks among visible pieces.",
        2,
        BotHeuristics.DementorsFork,
        0.02);

    public static BotProfile GastersShadow { get; } = new(
        "gasters-shadow",
        "Gaster’s Shadow",
        "Strategist",
        "Searches three plies ahead and estimates hidden king positions from a probability map.",
        3,
        BotHeuristics.GastersShadow,
        0.005);

    public static BotProfile DeepFog { get; } = new(
        "deep-fog",
        "Deep Fog",
        "Grandmaster Boss",
        "Searches four plies with alpha-beta pruning and immutable transposition memoization.",
        4,
        BotHeuristics.DeepFog,
        0);

    public static ImmutableArray<BotProfile> All { get; } = ImmutableArray.Create(
        MistWalker, ZabuzasGambit, DementorsFork, GastersShadow, DeepFog);
}

public static class BotHeuristics
{
    public static int MistWalker(MaskedBoardState board, PlayerColor perspective) =>
        MaterialScore(board, perspective) + CaptureOpportunityScore(board, perspective);

    public static int ZabuzasGambit(MaskedBoardState board, PlayerColor perspective) =>
        MaterialScore(board, perspective)
        + CaptureOpportunityScore(board, perspective)
        + AdvancementScore(board, perspective) * 3
        + ExploredVisionScore(board) * 8;

    public static int DementorsFork(MaskedBoardState board, PlayerColor perspective) =>
        MaterialScore(board, perspective) + ForkScore(board, perspective) * 2 + CenterScore(board, perspective);

    public static int GastersShadow(MaskedBoardState board, PlayerColor perspective) =>
        MaterialScore(board, perspective)
        + ForkScore(board, perspective)
        + ExpectedKingPressure(board, perspective) * 2
        + KingSafetyScore(board, perspective);

    public static int DeepFog(MaskedBoardState board, PlayerColor perspective) =>
        MaterialScore(board, perspective)
        + ForkScore(board, perspective)
        + ExpectedKingPressure(board, perspective)
        + KingSafetyScore(board, perspective) * 2
        + CenterScore(board, perspective) * 2
        + ExploredVisionScore(board) * 5;

    public static ImmutableDictionary<Position, double> BuildBeliefMap(MaskedBoardState board, PlayerColor kingColor)
    {
        var knownKing = board.Squares
            .Where(entry => entry.Value is SquareVisibility.Visible { Piece: King king } && king.Color == kingColor)
            .Select(entry => (Position?)entry.Key)
            .FirstOrDefault();
        if (knownKing is { } knownPosition)
        {
            return ImmutableDictionary<Position, double>.Empty.Add(knownPosition, 1d);
        }

        var candidates = board.Squares
            .Where(entry => entry.Value is SquareVisibility.Hidden)
            .Select(entry => entry.Key)
            .Order()
            .ToImmutableArray();
        if (candidates.IsEmpty)
        {
            return ImmutableDictionary<Position, double>.Empty;
        }

        var weighted = candidates.Select(position =>
        {
            var centerDistance = Math.Abs(position.File - 3.5) + Math.Abs(position.Rank - 3.5);
            var weight = Math.Max(0.1, 1.8 - centerDistance * 0.12);
            return (position, weight);
        }).ToImmutableArray();
        var totalWeight = weighted.Sum(entry => entry.weight);
        return weighted.ToImmutableDictionary(entry => entry.position, entry => entry.weight / totalWeight);
    }

    private static int MaterialScore(MaskedBoardState board, PlayerColor perspective)
    {
        var score = 0;
        foreach (var (position, visibility) in board.Squares)
        {
            if (visibility is not SquareVisibility.Visible { Piece: { } piece })
            {
                continue;
            }

            var signedValue = PieceValue(piece.Kind);
            score += piece.Color == perspective ? signedValue : -signedValue;
            if (piece is Pawn)
            {
                var advancement = piece.Color == PlayerColor.White ? position.Rank : 7 - position.Rank;
                score += (piece.Color == perspective ? 1 : -1) * advancement * 3;
            }
        }

        return score;
    }

    private static int CaptureOpportunityScore(MaskedBoardState board, PlayerColor perspective)
    {
        var score = 0;
        foreach (var (origin, visibility) in board.Squares)
        {
            if (visibility is not SquareVisibility.Visible { Piece: { } attacker } || attacker.Color != perspective)
            {
                continue;
            }

            foreach (var (target, targetVisibility) in board.Squares)
            {
                if (targetVisibility is SquareVisibility.Visible { Piece: { } victim }
                    && victim.Color != perspective && AttacksKnownSquare(board, origin, attacker, target))
                {
                    score += PieceValue(victim.Kind) / 8;
                }
            }
        }

        return score;
    }

    private static int AdvancementScore(MaskedBoardState board, PlayerColor perspective)
    {
        var score = 0;
        foreach (var (position, visibility) in board.Squares)
        {
            if (visibility is SquareVisibility.Visible { Piece: Pawn pawn })
            {
                var advance = pawn.Color == PlayerColor.White ? position.Rank : 7 - position.Rank;
                score += (pawn.Color == perspective ? 1 : -1) * advance;
            }
        }

        return score;
    }

    private static int ForkScore(MaskedBoardState board, PlayerColor perspective)
    {
        var score = 0;
        foreach (var (target, visibility) in board.Squares)
        {
            if (visibility is not SquareVisibility.Visible { Piece: { } victim } || victim.Color == perspective)
            {
                continue;
            }

            var attackers = board.Squares.Count(entry =>
                entry.Value is SquareVisibility.Visible { Piece: { } piece }
                && piece.Color == perspective
                && AttacksKnownSquare(board, entry.Key, piece, target));
            if (attackers > 1)
            {
                score += (attackers - 1) * Math.Max(10, PieceValue(victim.Kind) / 10);
            }
        }

        return score;
    }

    private static int ExpectedKingPressure(MaskedBoardState board, PlayerColor perspective)
    {
        var beliefs = BuildBeliefMap(board, perspective.Opponent());
        var expectedPressure = 0d;
        foreach (var (kingSquare, probability) in beliefs)
        {
            var attackingPieces = board.Squares
                .Where(entry => entry.Value is SquareVisibility.Visible { Piece: { } piece } && piece.Color == perspective)
                .Count(entry => AttacksKnownSquare(board, entry.Key,
                    ((SquareVisibility.Visible)board.At(entry.Key)).Piece!, kingSquare));

            var attackedEscapes = KingOffsets
                .Select(offset => kingSquare.Offset(offset.File, offset.Rank))
                .Where(position => position.IsOnBoard)
                .Count(escape => board.Squares.Any(entry =>
                    entry.Value is SquareVisibility.Visible { Piece: { } piece }
                    && piece.Color == perspective
                    && AttacksKnownSquare(board, entry.Key, piece, escape)));

            expectedPressure += probability * (attackingPieces * 60 + attackedEscapes * 14);
        }

        return (int)Math.Round(expectedPressure, MidpointRounding.AwayFromZero);
    }

    private static int KingSafetyScore(MaskedBoardState board, PlayerColor perspective)
    {
        var king = board.Squares.FirstOrDefault(entry =>
            entry.Value is SquareVisibility.Visible { Piece: King piece } && piece.Color == perspective);
        if (king.Value is not SquareVisibility.Visible { Piece: King })
        {
            return 0;
        }

        var danger = board.Squares
            .Where(entry => entry.Value is SquareVisibility.Visible { Piece: { } piece } && piece.Color != perspective)
            .Count(entry => Math.Max(Math.Abs(entry.Key.File - king.Key.File), Math.Abs(entry.Key.Rank - king.Key.Rank)) <= 2
                && AttacksKnownSquare(board, entry.Key, ((SquareVisibility.Visible)entry.Value).Piece!, king.Key));
        return -danger * 35;
    }

    private static int CenterScore(MaskedBoardState board, PlayerColor perspective)
    {
        var score = 0;
        foreach (var (position, visibility) in board.Squares)
        {
            if (visibility is SquareVisibility.Visible { Piece: { } piece })
            {
                var distance = Math.Abs(position.File - 3.5) + Math.Abs(position.Rank - 3.5);
                score += (piece.Color == perspective ? 1 : -1) * (int)Math.Round(14 - distance * 2);
            }
        }

        return score;
    }

    private static int ExploredVisionScore(MaskedBoardState board) => board.Squares.Count(entry =>
        entry.Value is SquareVisibility.Visible) + board.Squares.Count(entry => entry.Value is SquareVisibility.AcousticRipple) * 2;

    private static bool AttacksKnownSquare(MaskedBoardState board, Position from, Piece piece, Position to)
    {
        var fileDelta = to.File - from.File;
        var rankDelta = to.Rank - from.Rank;
        var absFile = Math.Abs(fileDelta);
        var absRank = Math.Abs(rankDelta);
        var geometryMatches = piece switch
        {
            Pawn => absFile == 1 && rankDelta == (piece.Color == PlayerColor.White ? 1 : -1),
            Knight => (absFile, absRank) is (1, 2) or (2, 1),
            King => Math.Max(absFile, absRank) == 1,
            Bishop => absFile == absRank && absFile > 0,
            Rook => ((fileDelta == 0) != (rankDelta == 0)),
            Queen => absFile == absRank && absFile > 0 || ((fileDelta == 0) != (rankDelta == 0)),
            _ => false
        };
        if (!geometryMatches || piece is Pawn or Knight or King)
        {
            return geometryMatches;
        }

        var fileStep = Math.Sign(fileDelta);
        var rankStep = Math.Sign(rankDelta);
        var cursor = from.Offset(fileStep, rankStep);
        while (cursor != to)
        {
            if (board.At(cursor) is SquareVisibility.Visible { Piece: not null })
            {
                return false;
            }

            cursor = cursor.Offset(fileStep, rankStep);
        }

        return true;
    }

    private static int PieceValue(PieceKind kind) => kind switch
    {
        PieceKind.Pawn => 100,
        PieceKind.Knight => 310,
        PieceKind.Bishop => 330,
        PieceKind.Rook => 500,
        PieceKind.Queen => 900,
        PieceKind.King => 20_000,
        _ => 0
    };

    private static readonly ImmutableArray<(int File, int Rank)> KingOffsets =
        ImmutableArray.Create((1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1));
}

public abstract record BotError
{
    private BotError() { }

    public sealed record NoLegalMoves : BotError;
    public sealed record NotPlayersTurn(PlayerColor Viewer, PlayerColor Turn) : BotError;
}
