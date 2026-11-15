using System.Collections.Immutable;

namespace MistChess.Core.Domain;

public sealed record BoardState
{
    public ImmutableDictionary<Position, Piece> Pieces { get; init; }
    public PlayerColor Turn { get; init; }
    public CastlingRights CastlingRights { get; init; }
    public Position? EnPassantTarget { get; init; }
    public int HalfmoveClock { get; init; }
    public int FullmoveNumber { get; init; }
    public GameStatus Status { get; init; }
    public ImmutableList<Move> History { get; init; }
    public GameVariant Variant { get; init; }

    public BoardState(
        ImmutableDictionary<Position, Piece> pieces,
        PlayerColor turn = PlayerColor.White,
        CastlingRights castlingRights = CastlingRights.All,
        Position? enPassantTarget = null,
        int halfmoveClock = 0,
        int fullmoveNumber = 1,
        GameStatus? status = null,
        ImmutableList<Move>? history = null,
        GameVariant variant = GameVariant.FogOfWar)
    {
        Pieces = pieces;
        Turn = turn;
        CastlingRights = castlingRights;
        EnPassantTarget = enPassantTarget;
        HalfmoveClock = halfmoveClock;
        FullmoveNumber = fullmoveNumber;
        Status = status ?? GameStatus.Playing;
        History = history ?? ImmutableList<Move>.Empty;
        Variant = variant;
    }

    public Piece? PieceAt(Position position) => Pieces.GetValueOrDefault(position);
    public bool IsEmpty(Position position) => !Pieces.ContainsKey(position);

    public static BoardState Initial { get; } = CreateInitial();

    public static BoardState Create(
        IEnumerable<KeyValuePair<Position, Piece>> pieces,
        PlayerColor turn = PlayerColor.White,
        CastlingRights castlingRights = CastlingRights.None,
        Position? enPassantTarget = null,
        GameVariant variant = GameVariant.FogOfWar) =>
        new(pieces.ToImmutableDictionary(), turn, castlingRights, enPassantTarget, variant: variant);

    private static BoardState CreateInitial()
    {
        var pieces = ImmutableDictionary.CreateBuilder<Position, Piece>();
        var backRank = new PieceKind[]
        {
            PieceKind.Rook, PieceKind.Knight, PieceKind.Bishop, PieceKind.Queen,
            PieceKind.King, PieceKind.Bishop, PieceKind.Knight, PieceKind.Rook
        };

        foreach (var color in new[] { PlayerColor.White, PlayerColor.Black })
        {
            var homeRank = color == PlayerColor.White ? 0 : 7;
            var pawnRank = color == PlayerColor.White ? 1 : 6;
            for (var file = 0; file < 8; file++)
            {
                pieces[new Position(file, homeRank)] = PieceFactory.Create(backRank[file], color)!;
                pieces[new Position(file, pawnRank)] = new Pawn(color);
            }
        }

        return new BoardState(pieces.ToImmutable());
    }
}
