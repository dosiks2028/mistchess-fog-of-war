namespace MistChess.Core.Domain;

public abstract record Move(Position From, Position To);

public sealed record NormalMove(Position From, Position To) : Move(From, To);

public sealed record CaptureMove(Position From, Position To) : Move(From, To);

public sealed record CastlingMove(Position From, Position To, Position RookFrom, Position RookTo)
    : Move(From, To);

public sealed record EnPassantMove(Position From, Position To, Position CapturedPawnPosition)
    : Move(From, To);

public sealed record PromotionMove(Position From, Position To, PieceKind PromotedTo, bool IsCapture)
    : Move(From, To);

[Flags]
public enum CastlingRights
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    All = WhiteKingSide | WhiteQueenSide | BlackKingSide | BlackQueenSide
}

public abstract record GameStatus
{
    private GameStatus() { }

    public sealed record InProgress : GameStatus;
    public sealed record Won(PlayerColor Winner) : GameStatus;
    public sealed record Draw(DrawReason Reason) : GameStatus;

    public static GameStatus Playing { get; } = new InProgress();
}

public enum DrawReason
{
    Stalemate,
    FiftyMoveRule,
    InsufficientMaterial
}

public enum GameVariant
{
    FogOfWar,
    ClassicChess
}
