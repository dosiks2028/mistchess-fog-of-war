using System.Collections.Immutable;
using MistChess.Core.Domain;
using MistChess.Core.Functional;

namespace MistChess.Core.Engine;

public static class GameEngine
{
    public static Result<BoardState, IllegalMoveError> ApplyMove(BoardState board, Move move)
    {
        if (board.Status is not GameStatus.InProgress)
        {
            return Result<BoardState, IllegalMoveError>.Failure(new IllegalMoveError.GameAlreadyFinished());
        }

        if (!board.Pieces.TryGetValue(move.From, out var piece))
        {
            return Result<BoardState, IllegalMoveError>.Failure(new IllegalMoveError.NoPieceAtOrigin(move.From));
        }

        if (piece.Color != board.Turn)
        {
            return Result<BoardState, IllegalMoveError>.Failure(new IllegalMoveError.WrongPlayerToMove(board.Turn, piece.Color));
        }

        var legalMove = MoveGenerator.GetLegalMoves(board, board.Turn).Contains(move);
        return legalMove
            ? Result<BoardState, IllegalMoveError>.Success(ApplyGeneratedMove(board, move, adjudicate: true))
            : Result<BoardState, IllegalMoveError>.Failure(new IllegalMoveError.IllegalMove(move));
    }

    public static Result<BoardState, IllegalMoveError> Replay(BoardState initial, IEnumerable<Move> moves, int? step = null)
    {
        var line = moves.ToImmutableArray();
        var count = step ?? line.Length;
        if (count < 0 || count > line.Length)
        {
            return Result<BoardState, IllegalMoveError>.Failure(new IllegalMoveError.InvalidReplayStep(count, line.Length));
        }

        var current = initial;
        for (var index = 0; index < count; index++)
        {
            var applied = ApplyMove(current, line[index]);
            if (applied is Result<BoardState, IllegalMoveError>.Err error)
            {
                return Result<BoardState, IllegalMoveError>.Failure(error.Error);
            }

            current = ((Result<BoardState, IllegalMoveError>.Ok)applied).Value;
        }

        return Result<BoardState, IllegalMoveError>.Success(current);
    }

    internal static BoardState ApplyForSearch(BoardState board, Move move) =>
        ApplyGeneratedMove(board, move, adjudicate: false);

    private static BoardState ApplyGeneratedMove(BoardState board, Move move, bool adjudicate)
    {
        var movingPiece = board.Pieces[move.From];
        var pieces = board.Pieces.ToBuilder();
        pieces.Remove(move.From);

        Position? capturePosition = move switch
        {
            EnPassantMove enPassant => enPassant.CapturedPawnPosition,
            CaptureMove => move.To,
            PromotionMove { IsCapture: true } => move.To,
            _ => null
        };

        Piece? capturedPiece = null;
        if (capturePosition is { } capturedAt && pieces.TryGetValue(capturedAt, out var captured))
        {
            capturedPiece = captured;
            pieces.Remove(capturedAt);
        }

        switch (move)
        {
            case CastlingMove castling:
                pieces[castling.To] = movingPiece;
                if (pieces.TryGetValue(castling.RookFrom, out var rook))
                {
                    pieces.Remove(castling.RookFrom);
                    pieces[castling.RookTo] = rook;
                }

                break;
            case PromotionMove promotion:
                pieces[promotion.To] = PieceFactory.Create(promotion.PromotedTo, movingPiece.Color)!;
                break;
            case NormalMove:
            case CaptureMove:
            case EnPassantMove:
                pieces[move.To] = movingPiece;
                break;
            default:
                throw new InvalidOperationException($"Unsupported move type {move.GetType().Name}.");
        }

        var rights = UpdateCastlingRights(board.CastlingRights, movingPiece, move.From, capturePosition, capturedPiece);
        Position? enPassantTarget = movingPiece is Pawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
            ? new Position(move.From.File, (move.From.Rank + move.To.Rank) / 2)
            : null;
        var isCapture = capturePosition is not null;
        var nextTurn = board.Turn.Opponent();
        var next = new BoardState(
            pieces.ToImmutable(),
            nextTurn,
            rights,
            enPassantTarget,
            movingPiece is Pawn || isCapture ? 0 : board.HalfmoveClock + 1,
            board.FullmoveNumber + (board.Turn == PlayerColor.Black ? 1 : 0),
            GameStatus.Playing,
            board.History.Add(move),
            board.Variant);

        if (capturedPiece is King)
        {
            return next with { Status = new GameStatus.Won(movingPiece.Color) };
        }

        if (adjudicate && board.Variant == GameVariant.ClassicChess)
        {
            if (next.HalfmoveClock >= 100)
            {
                return next with { Status = new GameStatus.Draw(DrawReason.FiftyMoveRule) };
            }

            if (IsInsufficientMaterial(next))
            {
                return next with { Status = new GameStatus.Draw(DrawReason.InsufficientMaterial) };
            }

            var replies = MoveGenerator.GetLegalMoves(next, nextTurn);
            if (replies.IsEmpty)
            {
                return MoveGenerator.IsInCheck(next, nextTurn)
                    ? next with { Status = new GameStatus.Won(movingPiece.Color) }
                    : next with { Status = new GameStatus.Draw(DrawReason.Stalemate) };
            }
        }

        return next;
    }

    private static CastlingRights UpdateCastlingRights(
        CastlingRights rights,
        Piece movingPiece,
        Position from,
        Position? capturePosition,
        Piece? capturedPiece)
    {
        if (movingPiece is King)
        {
            rights &= movingPiece.Color == PlayerColor.White
                ? ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide)
                : ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide);
        }

        if (movingPiece is Rook)
        {
            rights = RemoveRookRight(rights, movingPiece.Color, from);
        }

        if (capturedPiece is Rook && capturePosition is { } square)
        {
            rights = RemoveRookRight(rights, capturedPiece.Color, square);
        }

        return rights;
    }

    private static CastlingRights RemoveRookRight(CastlingRights rights, PlayerColor color, Position square)
    {
        var homeRank = color == PlayerColor.White ? 0 : 7;
        if (square == new Position(0, homeRank))
        {
            rights &= color == PlayerColor.White ? ~CastlingRights.WhiteQueenSide : ~CastlingRights.BlackQueenSide;
        }
        else if (square == new Position(7, homeRank))
        {
            rights &= color == PlayerColor.White ? ~CastlingRights.WhiteKingSide : ~CastlingRights.BlackKingSide;
        }

        return rights;
    }

    private static bool IsInsufficientMaterial(BoardState board)
    {
        var material = board.Pieces.Values.Where(piece => piece is not King).ToImmutableArray();
        if (material.IsEmpty)
        {
            return true;
        }

        if (material.Length == 1 && material[0] is Bishop or Knight)
        {
            return true;
        }

        if (material.All(piece => piece is Bishop))
        {
            var bishopSquares = board.Pieces
                .Where(entry => entry.Value is Bishop)
                .Select(entry => (entry.Key.File + entry.Key.Rank) % 2)
                .Distinct()
                .ToImmutableArray();
            return bishopSquares.Length <= 1;
        }

        return false;
    }
}

public abstract record IllegalMoveError
{
    private IllegalMoveError() { }

    public sealed record GameAlreadyFinished : IllegalMoveError;
    public sealed record NoPieceAtOrigin(Position Position) : IllegalMoveError;
    public sealed record WrongPlayerToMove(PlayerColor Expected, PlayerColor Actual) : IllegalMoveError;
    public sealed record IllegalMove(Move Move) : IllegalMoveError;
    public sealed record InvalidReplayStep(int RequestedStep, int AvailableMoves) : IllegalMoveError;
}
