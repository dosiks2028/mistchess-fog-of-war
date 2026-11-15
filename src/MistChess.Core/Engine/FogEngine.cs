using System.Collections.Immutable;
using MistChess.Core.Domain;
using MistChess.Core.Functional;

namespace MistChess.Core.Engine;

public abstract record SquareVisibility
{
    private SquareVisibility() { }

    public sealed record Hidden : SquareVisibility;
    public sealed record Visible(Piece? Piece) : SquareVisibility;
    public sealed record AcousticRipple : SquareVisibility;

    public static SquareVisibility HiddenSquare { get; } = new Hidden();
}

public sealed record MaskedBoardState(
    ImmutableDictionary<Position, SquareVisibility> Squares,
    PlayerColor Viewer,
    PlayerColor Turn,
    GameVariant Variant)
{
    public bool IsOmniscient { get; init; }
    public SquareVisibility At(Position position) => Squares.GetValueOrDefault(position, SquareVisibility.HiddenSquare);
}

public enum ReplayPerspective
{
    WhiteFog,
    BlackFog,
    Omniscient
}

public sealed record FogRule(
    string Name,
    Func<BoardState, PlayerColor, ImmutableHashSet<Position>> Vision,
    Func<BoardState, PlayerColor, ImmutableHashSet<Position>> AcousticRipples)
{
    public FogRule Combine(FogRule other) => new(
        $"{Name} + {other.Name}",
        (board, viewer) => Vision(board, viewer).Union(other.Vision(board, viewer)),
        (board, viewer) => AcousticRipples(board, viewer).Union(other.AcousticRipples(board, viewer)));
}

public static class FogRules
{
    public static FogRule ClassicAttackVision { get; } = new(
        "Classic attack vision",
        MoveGenerator.GetVisionSquares,
        EmptySquares);

    public static FogRule KingRadarVision { get; } = new(
        "King radar vision",
        GetKingRadarSquares,
        EmptySquares);

    public static FogRule AcousticFootsteps { get; } = new(
        "Acoustic footsteps",
        EmptySquares,
        GetAcousticRippleSquares);

    public static FogRule Standard { get; } =
        ClassicAttackVision.Combine(KingRadarVision).Combine(AcousticFootsteps);

    private static ImmutableHashSet<Position> EmptySquares(BoardState _, PlayerColor __) =>
        ImmutableHashSet<Position>.Empty;

    private static ImmutableHashSet<Position> GetKingRadarSquares(BoardState board, PlayerColor viewer)
    {
        var kings = board.Pieces
            .Where(entry => entry.Value.Color == viewer && entry.Value is King)
            .Select(entry => entry.Key);
        var squares = ImmutableHashSet.CreateBuilder<Position>();
        foreach (var king in kings)
        {
            for (var fileDelta = -2; fileDelta <= 2; fileDelta++)
            {
                for (var rankDelta = -2; rankDelta <= 2; rankDelta++)
                {
                    var position = king.Offset(fileDelta, rankDelta);
                    if (position.IsOnBoard)
                    {
                        squares.Add(position);
                    }
                }
            }
        }

        return squares.ToImmutable();
    }

    private static ImmutableHashSet<Position> GetAcousticRippleSquares(BoardState board, PlayerColor viewer)
    {
        if (board.History.IsEmpty)
        {
            return ImmutableHashSet<Position>.Empty;
        }

        var lastMove = board.History[^1];
        if (board.PieceAt(lastMove.To) is not { } movedPiece || movedPiece.Color != viewer.Opponent())
        {
            return ImmutableHashSet<Position>.Empty;
        }

        var isNearFriendlyPiece = board.Pieces
            .Where(entry => entry.Value.Color == viewer)
            .Any(entry => Math.Max(Math.Abs(entry.Key.File - lastMove.To.File), Math.Abs(entry.Key.Rank - lastMove.To.Rank)) <= 2);

        return isNearFriendlyPiece
            ? ImmutableHashSet.Create(lastMove.To)
            : ImmutableHashSet<Position>.Empty;
    }
}

public static class FogEngine
{
    public static MaskedBoardState ProjectBoard(BoardState truth, PlayerColor viewer, FogRule rule)
    {
        var visible = rule.Vision(truth, viewer).Union(
            truth.Pieces.Where(entry => entry.Value.Color == viewer).Select(entry => entry.Key));
        var ripples = rule.AcousticRipples(truth, viewer);
        var squares = ImmutableDictionary.CreateBuilder<Position, SquareVisibility>();

        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var position = new Position(file, rank);
                if (ripples.Contains(position) && truth.PieceAt(position) is { Color: var color } && color != viewer)
                {
                    squares[position] = new SquareVisibility.AcousticRipple();
                }
                else if (visible.Contains(position))
                {
                    squares[position] = new SquareVisibility.Visible(truth.PieceAt(position));
                }
                else
                {
                    squares[position] = SquareVisibility.HiddenSquare;
                }
            }
        }

        return new MaskedBoardState(squares.ToImmutable(), viewer, truth.Turn, truth.Variant);
    }

    public static MaskedBoardState ProjectOmniscientBoard(BoardState truth)
    {
        var squares = ImmutableDictionary.CreateBuilder<Position, SquareVisibility>();
        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var position = new Position(file, rank);
                squares[position] = new SquareVisibility.Visible(truth.PieceAt(position));
            }
        }

        return new MaskedBoardState(squares.ToImmutable(), truth.Turn, truth.Turn, truth.Variant)
        {
            IsOmniscient = true
        };
    }

    public static Result<MaskedBoardState, IllegalMoveError> ProjectReplay(
        BoardState initial,
        IEnumerable<Move> moves,
        int step,
        ReplayPerspective perspective,
        FogRule? rule = null) =>
        GameEngine.Replay(initial, moves, step).Map(board => perspective switch
        {
            ReplayPerspective.WhiteFog => ProjectBoard(board, PlayerColor.White, rule ?? FogRules.Standard),
            ReplayPerspective.BlackFog => ProjectBoard(board, PlayerColor.Black, rule ?? FogRules.Standard),
            ReplayPerspective.Omniscient => ProjectOmniscientBoard(board),
            _ => throw new ArgumentOutOfRangeException(nameof(perspective))
        });
}
