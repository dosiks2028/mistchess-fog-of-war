using System.Collections.Immutable;
using MistChess.Core.Domain;

namespace MistChess.Core.Engine;

public static class MoveGenerator
{
    private static readonly ImmutableArray<(int File, int Rank)> KnightOffsets =
        ImmutableArray.Create((1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2));

    private static readonly ImmutableArray<(int File, int Rank)> KingOffsets =
        ImmutableArray.Create((1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1));

    private static readonly ImmutableArray<(int File, int Rank)> BishopDirections =
        ImmutableArray.Create((1, 1), (1, -1), (-1, 1), (-1, -1));

    private static readonly ImmutableArray<(int File, int Rank)> RookDirections =
        ImmutableArray.Create((1, 0), (-1, 0), (0, 1), (0, -1));

    private static readonly ImmutableArray<PieceKind> PromotionKinds =
        ImmutableArray.Create(PieceKind.Queen, PieceKind.Rook, PieceKind.Bishop, PieceKind.Knight);

    public static ImmutableList<Move> GetLegalMoves(BoardState board, PlayerColor turn)
    {
        if (board.Status is not GameStatus.InProgress)
        {
            return ImmutableList<Move>.Empty;
        }

        return GeneratePseudoLegalMoves(board, turn)
            .Where(move => LeavesKingSafe(board, move, turn))
            .ToImmutableList();
    }

    public static bool IsInCheck(BoardState board, PlayerColor color)
    {
        var kingPosition = board.Pieces
            .Where(entry => entry.Value.Color == color && entry.Value is King)
            .Select(entry => (Position?)entry.Key)
            .FirstOrDefault();

        return kingPosition is { } position && IsSquareAttacked(board, position, color.Opponent());
    }

    public static bool IsSquareAttacked(BoardState board, Position target, PlayerColor byColor)
    {
        foreach (var (from, piece) in board.Pieces)
        {
            if (piece.Color != byColor)
            {
                continue;
            }

            var fileDelta = target.File - from.File;
            var rankDelta = target.Rank - from.Rank;
            var absFile = Math.Abs(fileDelta);
            var absRank = Math.Abs(rankDelta);
            var attacked = piece switch
            {
                Pawn => absFile == 1 && rankDelta == (byColor == PlayerColor.White ? 1 : -1),
                Knight => (absFile, absRank) is (1, 2) or (2, 1),
                King => Math.Max(absFile, absRank) == 1,
                Bishop => absFile == absRank && absFile > 0 && IsPathClear(board, from, target),
                Rook => ((fileDelta == 0) != (rankDelta == 0)) && IsPathClear(board, from, target),
                Queen => (absFile == absRank && absFile > 0 || (fileDelta == 0) != (rankDelta == 0))
                         && IsPathClear(board, from, target),
                _ => false
            };

            if (attacked)
            {
                return true;
            }
        }

        return false;
    }

    public static ImmutableHashSet<Position> GetVisionSquares(BoardState board, PlayerColor viewer)
    {
        var visible = ImmutableHashSet.CreateBuilder<Position>();
        foreach (var (position, piece) in board.Pieces.Where(entry => entry.Value.Color == viewer))
        {
            visible.Add(position);
            switch (piece)
            {
                case Pawn:
                {
                    var direction = viewer == PlayerColor.White ? 1 : -1;
                    AddIfOnBoard(visible, position.Offset(-1, direction));
                    AddIfOnBoard(visible, position.Offset(1, direction));
                    var forward = position.Offset(0, direction);
                    if (forward.IsOnBoard && board.IsEmpty(forward))
                    {
                        visible.Add(forward);
                        var doubleForward = position.Offset(0, 2 * direction);
                        if (IsPawnStartingRank(position, viewer) && doubleForward.IsOnBoard && board.IsEmpty(doubleForward))
                        {
                            visible.Add(doubleForward);
                        }
                    }

                    break;
                }
                case Knight:
                    foreach (var (file, rank) in KnightOffsets)
                    {
                        AddIfOnBoard(visible, position.Offset(file, rank));
                    }

                    break;
                case King:
                    foreach (var (file, rank) in KingOffsets)
                    {
                        AddIfOnBoard(visible, position.Offset(file, rank));
                    }

                    break;
                case Bishop:
                    AddVisionRays(board, position, BishopDirections, visible);
                    break;
                case Rook:
                    AddVisionRays(board, position, RookDirections, visible);
                    break;
                case Queen:
                    AddVisionRays(board, position, BishopDirections, visible);
                    AddVisionRays(board, position, RookDirections, visible);
                    break;
            }
        }

        return visible.ToImmutable();
    }

    internal static ImmutableList<Move> GeneratePseudoLegalMoves(BoardState board, PlayerColor turn)
    {
        var moves = ImmutableList.CreateBuilder<Move>();
        foreach (var (from, piece) in board.Pieces.OrderBy(entry => entry.Key))
        {
            if (piece.Color != turn)
            {
                continue;
            }

            switch (piece)
            {
                case Pawn:
                    AddPawnMoves(board, from, turn, moves);
                    break;
                case Knight:
                    foreach (var (file, rank) in KnightOffsets)
                    {
                        AddStepMove(board, from, from.Offset(file, rank), turn, moves);
                    }

                    break;
                case Bishop:
                    AddSlidingMoves(board, from, turn, BishopDirections, moves);
                    break;
                case Rook:
                    AddSlidingMoves(board, from, turn, RookDirections, moves);
                    break;
                case Queen:
                    AddSlidingMoves(board, from, turn, BishopDirections, moves);
                    AddSlidingMoves(board, from, turn, RookDirections, moves);
                    break;
                case King:
                    foreach (var (file, rank) in KingOffsets)
                    {
                        AddStepMove(board, from, from.Offset(file, rank), turn, moves);
                    }

                    AddCastlingMoves(board, from, turn, moves);
                    break;
            }
        }

        return moves.ToImmutable();
    }

    private static bool LeavesKingSafe(BoardState board, Move move, PlayerColor color)
    {
        if (board.Variant == GameVariant.FogOfWar)
        {
            return true;
        }

        if (move is CastlingMove && IsInCheck(board, color))
        {
            return false;
        }

        var next = GameEngine.ApplyForSearch(board, move);
        return !IsInCheck(next, color);
    }

    private static void AddPawnMoves(BoardState board, Position from, PlayerColor color, ImmutableList<Move>.Builder moves)
    {
        var direction = color == PlayerColor.White ? 1 : -1;
        var oneForward = from.Offset(0, direction);
        if (oneForward.IsOnBoard && board.IsEmpty(oneForward))
        {
            AddPawnMove(board, from, oneForward, isCapture: false, color, moves);
            var twoForward = from.Offset(0, 2 * direction);
            if (IsPawnStartingRank(from, color) && board.IsEmpty(twoForward))
            {
                moves.Add(new NormalMove(from, twoForward));
            }
        }

        foreach (var fileDelta in new[] { -1, 1 })
        {
            var target = from.Offset(fileDelta, direction);
            if (!target.IsOnBoard)
            {
                continue;
            }

            if (board.PieceAt(target) is { } targetPiece && targetPiece.Color != color)
            {
                AddPawnMove(board, from, target, isCapture: true, color, moves);
            }
            else if (board.EnPassantTarget == target && board.PieceAt(from.Offset(fileDelta, 0)) is Pawn adjacentPawn
                     && adjacentPawn.Color != color)
            {
                moves.Add(new EnPassantMove(from, target, from.Offset(fileDelta, 0)));
            }
        }
    }

    private static void AddPawnMove(
        BoardState board,
        Position from,
        Position to,
        bool isCapture,
        PlayerColor color,
        ImmutableList<Move>.Builder moves)
    {
        var promotionRank = color == PlayerColor.White ? 7 : 0;
        if (to.Rank == promotionRank)
        {
            foreach (var promotionKind in PromotionKinds)
            {
                moves.Add(new PromotionMove(from, to, promotionKind, isCapture));
            }
        }
        else
        {
            moves.Add(isCapture ? new CaptureMove(from, to) : new NormalMove(from, to));
        }
    }

    private static void AddStepMove(
        BoardState board,
        Position from,
        Position to,
        PlayerColor color,
        ImmutableList<Move>.Builder moves)
    {
        if (!to.IsOnBoard)
        {
            return;
        }

        if (board.PieceAt(to) is { } target)
        {
            if (target.Color != color)
            {
                moves.Add(new CaptureMove(from, to));
            }
        }
        else
        {
            moves.Add(new NormalMove(from, to));
        }
    }

    private static void AddSlidingMoves(
        BoardState board,
        Position from,
        PlayerColor color,
        ImmutableArray<(int File, int Rank)> directions,
        ImmutableList<Move>.Builder moves)
    {
        foreach (var (file, rank) in directions)
        {
            var target = from.Offset(file, rank);
            while (target.IsOnBoard)
            {
                if (board.PieceAt(target) is { } targetPiece)
                {
                    if (targetPiece.Color != color)
                    {
                        moves.Add(new CaptureMove(from, target));
                    }

                    break;
                }

                moves.Add(new NormalMove(from, target));
                target = target.Offset(file, rank);
            }
        }
    }

    private static void AddCastlingMoves(BoardState board, Position kingPosition, PlayerColor color, ImmutableList<Move>.Builder moves)
    {
        var homeRank = color == PlayerColor.White ? 0 : 7;
        var kingSideRight = color == PlayerColor.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        var queenSideRight = color == PlayerColor.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        if (kingPosition != new Position(4, homeRank))
        {
            return;
        }

        if (board.CastlingRights.HasFlag(kingSideRight)
            && board.PieceAt(new Position(7, homeRank)) is Rook { Color: var rookColor } && rookColor == color
            && board.IsEmpty(new Position(5, homeRank)) && board.IsEmpty(new Position(6, homeRank))
            && (board.Variant == GameVariant.FogOfWar
                || !IsKingSquareAttackedAfterStep(board, kingPosition, new Position(5, homeRank), color)
                && !IsKingSquareAttackedAfterStep(board, kingPosition, new Position(6, homeRank), color)))
        {
            moves.Add(new CastlingMove(kingPosition, new Position(6, homeRank), new Position(7, homeRank), new Position(5, homeRank)));
        }

        if (board.CastlingRights.HasFlag(queenSideRight)
            && board.PieceAt(new Position(0, homeRank)) is Rook { Color: var queenRookColor } && queenRookColor == color
            && board.IsEmpty(new Position(1, homeRank)) && board.IsEmpty(new Position(2, homeRank)) && board.IsEmpty(new Position(3, homeRank))
            && (board.Variant == GameVariant.FogOfWar
                || !IsKingSquareAttackedAfterStep(board, kingPosition, new Position(3, homeRank), color)
                && !IsKingSquareAttackedAfterStep(board, kingPosition, new Position(2, homeRank), color)))
        {
            moves.Add(new CastlingMove(kingPosition, new Position(2, homeRank), new Position(0, homeRank), new Position(3, homeRank)));
        }
    }

    private static bool IsPawnStartingRank(Position position, PlayerColor color) =>
        position.Rank == (color == PlayerColor.White ? 1 : 6);

    private static bool IsKingSquareAttackedAfterStep(BoardState board, Position kingFrom, Position kingTo, PlayerColor color)
    {
        if (board.PieceAt(kingFrom) is not King king)
        {
            return true;
        }

        var movedKing = board with { Pieces = board.Pieces.Remove(kingFrom).SetItem(kingTo, king) };
        return IsSquareAttacked(movedKing, kingTo, color.Opponent());
    }

    private static bool IsPathClear(BoardState board, Position from, Position to)
    {
        var fileStep = Math.Sign(to.File - from.File);
        var rankStep = Math.Sign(to.Rank - from.Rank);
        var cursor = from.Offset(fileStep, rankStep);
        while (cursor != to)
        {
            if (!board.IsEmpty(cursor))
            {
                return false;
            }

            cursor = cursor.Offset(fileStep, rankStep);
        }

        return true;
    }

    private static void AddVisionRays(
        BoardState board,
        Position origin,
        ImmutableArray<(int File, int Rank)> directions,
        ImmutableHashSet<Position>.Builder visible)
    {
        foreach (var (file, rank) in directions)
        {
            var target = origin.Offset(file, rank);
            while (target.IsOnBoard)
            {
                visible.Add(target);
                if (!board.IsEmpty(target))
                {
                    break;
                }

                target = target.Offset(file, rank);
            }
        }
    }

    private static void AddIfOnBoard(ImmutableHashSet<Position>.Builder positions, Position position)
    {
        if (position.IsOnBoard)
        {
            positions.Add(position);
        }
    }
}
