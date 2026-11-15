namespace MistChess.Core.Domain;

public enum PlayerColor
{
    White,
    Black
}

public static class PlayerColorExtensions
{
    public static PlayerColor Opponent(this PlayerColor color) => color switch
    {
        PlayerColor.White => PlayerColor.Black,
        PlayerColor.Black => PlayerColor.White,
        _ => throw new ArgumentOutOfRangeException(nameof(color))
    };
}

public enum PieceKind
{
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King
}

public abstract record Piece(PlayerColor Color)
{
    public abstract PieceKind Kind { get; }
}

public sealed record Pawn(PlayerColor Color) : Piece(Color)
{
    public override PieceKind Kind => PieceKind.Pawn;
}

public sealed record Knight(PlayerColor Color) : Piece(Color)
{
    public override PieceKind Kind => PieceKind.Knight;
}

public sealed record Bishop(PlayerColor Color) : Piece(Color)
{
    public override PieceKind Kind => PieceKind.Bishop;
}

public sealed record Rook(PlayerColor Color) : Piece(Color)
{
    public override PieceKind Kind => PieceKind.Rook;
}

public sealed record Queen(PlayerColor Color) : Piece(Color)
{
    public override PieceKind Kind => PieceKind.Queen;
}

public sealed record King(PlayerColor Color) : Piece(Color)
{
    public override PieceKind Kind => PieceKind.King;
}

public static class PieceFactory
{
    public static Piece? Create(PieceKind kind, PlayerColor color) => kind switch
    {
        PieceKind.Pawn => new Pawn(color),
        PieceKind.Knight => new Knight(color),
        PieceKind.Bishop => new Bishop(color),
        PieceKind.Rook => new Rook(color),
        PieceKind.Queen => new Queen(color),
        PieceKind.King => new King(color),
        _ => null
    };
}
