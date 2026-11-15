using System.Collections.Immutable;
using System.Security.Cryptography;
using MistChess.Core.Domain;
using MistChess.Core.Engine;
using MistChess.Core.Functional;

namespace MistChess.Web.Services;

public sealed record OnlineSquare(
    Position Position,
    bool IsVisible,
    bool IsRipple,
    PieceKind? PieceKind,
    PlayerColor? PieceColor);

public sealed record OnlineGameView(
    string Code,
    string WhiteName,
    string? BlackName,
    PlayerColor MyColor,
    PlayerColor Turn,
    GameVariant Variant,
    bool IsOpponentConnected,
    bool IsFinished,
    int MoveCount,
    string Status,
    ImmutableArray<OnlineSquare> Squares);

public sealed record RoomOperationResult(
    bool Success,
    string Message,
    string? Code = null,
    PlayerColor? Seat = null,
    bool NeedsPromotion = false,
    OnlineGameView? View = null);

public sealed class OnlineGameRegistry
{
    private const int MaximumRooms = 500;
    private static readonly char[] CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
    private readonly object _gate = new();
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _participantRooms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<OnlineGameView>> _listeners = new(StringComparer.Ordinal);

    public RoomOperationResult CreateRoom(string participantId, string playerName, GameVariant variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        var cleanName = CleanName(playerName);
        Room room;
        OnlineGameView view;
        List<(Action<OnlineGameView> Listener, OnlineGameView View)> updates;
        lock (_gate)
        {
            PruneExpiredRooms();
            if (_participantRooms.ContainsKey(participantId))
            {
                return new RoomOperationResult(false, "You are already in a room.");
            }

            if (_rooms.Count >= MaximumRooms)
            {
                return new RoomOperationResult(false, "There are too many active rooms. Please try again later.");
            }

            var code = CreateCode();
            var initial = BoardState.Initial with { Variant = variant };
            room = new Room(code, initial, cleanName, participantId);
            _rooms.Add(code, room);
            _participantRooms.Add(participantId, code);
            view = BuildView(room, participantId);
            updates = BuildUpdates(room);
        }

        Publish(updates);
        return new RoomOperationResult(true, "Room created. Share the code with another player.", room.Code, PlayerColor.White,
            View: view);
    }

    public RoomOperationResult JoinRoom(string participantId, string code, string playerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode.Length != 8)
        {
            return new RoomOperationResult(false, "Enter a valid 8-character room code.");
        }

        Room room;
        OnlineGameView view;
        List<(Action<OnlineGameView> Listener, OnlineGameView View)> updates;
        lock (_gate)
        {
            if (_participantRooms.ContainsKey(participantId))
            {
                return new RoomOperationResult(false, "You are already in a room.");
            }

            if (!_rooms.TryGetValue(normalizedCode, out var foundRoom)
                || foundRoom is null
                || foundRoom.BlackParticipantId is not null
                || foundRoom.IsFinished)
            {
                return new RoomOperationResult(false, "Room not found or already full.");
            }

            room = foundRoom;
            room.BlackParticipantId = participantId;
            room.BlackName = CleanName(playerName);
            room.BlackConnected = true;
            room.LastActivity = DateTimeOffset.UtcNow;
            _participantRooms.Add(participantId, room.Code);
            view = BuildView(room, participantId);
            updates = BuildUpdates(room);
        }

        Publish(updates);
        return new RoomOperationResult(true, "You joined the room.", room.Code, PlayerColor.Black,
            View: view);
    }

    public void Subscribe(string participantId, Action<OnlineGameView> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        OnlineGameView? current = null;
        lock (_gate)
        {
            _listeners[participantId] = listener;
            if (_participantRooms.TryGetValue(participantId, out var code)
                && _rooms.TryGetValue(code, out var room))
            {
                current = BuildView(room, participantId);
            }
        }

        if (current is not null)
        {
            listener(current);
        }
    }

    public void Unsubscribe(string participantId)
    {
        lock (_gate)
        {
            _listeners.Remove(participantId);
        }
    }

    public RoomOperationResult TryMove(
        string participantId,
        string code,
        Position from,
        Position to,
        PieceKind? promotion = null)
    {
        if (!from.IsOnBoard || !to.IsOnBoard)
        {
            return new RoomOperationResult(false, "Move rejected.");
        }

        var normalizedCode = NormalizeCode(code);
        List<(Action<OnlineGameView> Listener, OnlineGameView View)>? updates = null;
        RoomOperationResult result;
        lock (_gate)
        {
            if (!_participantRooms.TryGetValue(participantId, out var participantCode)
                || !StringComparer.OrdinalIgnoreCase.Equals(participantCode, normalizedCode)
                || !_rooms.TryGetValue(participantCode, out var room)
                || !room.TryGetSeat(participantId, out var seat)
                || room.IsFinished
                || room.BlackParticipantId is null
                || room.Board.Turn != seat)
            {
                return new RoomOperationResult(false, "Move rejected.");
            }

            var candidates = MoveGenerator.GetLegalMoves(room.Board, seat)
                .Where(move => move.From == from && move.To == to)
                .ToImmutableArray();
            if (candidates.IsEmpty)
            {
                return new RoomOperationResult(false, "Move rejected.");
            }

            Move move;
            var promotions = candidates.OfType<PromotionMove>().ToImmutableArray();
            if (!promotions.IsEmpty)
            {
                if (promotion is null)
                {
                    return new RoomOperationResult(false, "Choose a piece for promotion.", room.Code, seat, NeedsPromotion: true);
                }

                var selectedPromotion = promotions.FirstOrDefault(candidate => candidate.PromotedTo == promotion);
                if (selectedPromotion is null)
                {
                    return new RoomOperationResult(false, "Move rejected.");
                }

                move = selectedPromotion;
            }
            else
            {
                move = candidates[0];
            }

            if (GameEngine.ApplyMove(room.Board, move) is not Result<BoardState, IllegalMoveError>.Ok applied)
            {
                return new RoomOperationResult(false, "Move rejected.");
            }

            room.Board = applied.Value;
            room.LastActivity = DateTimeOffset.UtcNow;
            updates = BuildUpdates(room);
            result = new RoomOperationResult(true, "Move accepted.", room.Code, seat);
        }

        Publish(updates);
        return result;
    }

    public void MarkDisconnected(string participantId)
    {
        List<(Action<OnlineGameView> Listener, OnlineGameView View)>? updates = null;
        lock (_gate)
        {
            if (_participantRooms.TryGetValue(participantId, out var code)
                && _rooms.TryGetValue(code, out var room))
            {
                room.SetConnected(participantId, false);
                room.LastActivity = DateTimeOffset.UtcNow;
                updates = BuildUpdates(room);
            }

            _listeners.Remove(participantId);
        }

        if (updates is not null)
        {
            Publish(updates);
        }
    }

    public void LeaveRoom(string participantId)
    {
        List<(Action<OnlineGameView> Listener, OnlineGameView View)>? updates = null;
        lock (_gate)
        {
            _listeners.Remove(participantId);
            if (!_participantRooms.Remove(participantId, out var code)
                || !_rooms.TryGetValue(code, out var room))
            {
                return;
            }

            if (room.BlackParticipantId is null)
            {
                _rooms.Remove(code);
            }
            else
            {
                if (room.TryGetSeat(participantId, out var leavingSeat)
                    && room.Board.Status is GameStatus.InProgress)
                {
                    room.Board = room.Board with { Status = new GameStatus.Won(leavingSeat.Opponent()) };
                }

                room.SetConnected(participantId, false);
                room.LastActivity = DateTimeOffset.UtcNow;
                updates = BuildUpdates(room);
            }
        }

        if (updates is not null)
        {
            Publish(updates);
        }
    }

    private List<(Action<OnlineGameView> Listener, OnlineGameView View)> BuildUpdates(Room room)
    {
        var updates = new List<(Action<OnlineGameView>, OnlineGameView)>(2);
        AddUpdate(room, room.WhiteParticipantId, updates);
        AddUpdate(room, room.BlackParticipantId, updates);
        return updates;
    }

    private void AddUpdate(
        Room room,
        string? participantId,
        List<(Action<OnlineGameView> Listener, OnlineGameView View)> updates)
    {
        if (participantId is not null && _listeners.TryGetValue(participantId, out var listener))
        {
            updates.Add((listener, BuildView(room, participantId)));
        }
    }

    private static OnlineGameView BuildView(Room room, string participantId)
    {
        room.TryGetSeat(participantId, out var seat);
        var observation = room.Board.Variant == GameVariant.ClassicChess
            ? FogEngine.ProjectOmniscientBoard(room.Board)
            : FogEngine.ProjectBoard(room.Board, seat, FogRules.Standard);
        var squares = ImmutableArray.CreateBuilder<OnlineSquare>(64);
        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var position = new Position(file, rank);
                var visibility = observation.At(position);
                squares.Add(visibility switch
                {
                    SquareVisibility.Visible { Piece: { } piece } => new OnlineSquare(position, true, false, piece.Kind, piece.Color),
                    SquareVisibility.Visible => new OnlineSquare(position, true, false, null, null),
                    SquareVisibility.AcousticRipple => new OnlineSquare(position, false, true, null, null),
                    _ => new OnlineSquare(position, false, false, null, null)
                });
            }
        }

        var isWhite = seat == PlayerColor.White;
        var hasOpponent = room.BlackParticipantId is not null;
        var isConnected = isWhite ? room.BlackConnected : room.WhiteConnected;
        var status = room.Board.Status switch
        {
            GameStatus.Won { Winner: var winner } => winner == seat ? "You won" : "You lost",
            GameStatus.Draw { } => "Draw",
            _ when !hasOpponent => "Waiting for opponent",
            _ when !isConnected => "Opponent temporarily disconnected",
            _ when room.Board.Turn == seat => "Your turn",
            _ => "Opponent's turn"
        };

        return new OnlineGameView(
            room.Code,
            room.WhiteName,
            room.BlackName,
            seat,
            room.Board.Turn,
            room.Board.Variant,
            isConnected,
            room.IsFinished,
            room.Board.History.Count,
            status,
            squares.ToImmutable());
    }

    private static void Publish(List<(Action<OnlineGameView> Listener, OnlineGameView View)> updates)
    {
        foreach (var (listener, view) in updates)
        {
            try
            {
                listener(view);
            }
            catch (InvalidOperationException)
            {
                // A browser circuit can disappear between publishing a move and rendering it.
            }
        }
    }

    private void PruneExpiredRooms()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(-2);
        foreach (var expired in _rooms.Values.Where(room => room.LastActivity < expiry).ToArray())
        {
            _rooms.Remove(expired.Code);
            if (expired.WhiteParticipantId is not null)
            {
                _participantRooms.Remove(expired.WhiteParticipantId);
            }

            if (expired.BlackParticipantId is not null)
            {
                _participantRooms.Remove(expired.BlackParticipantId);
            }
        }
    }

    private static string CleanName(string? name)
    {
        var cleaned = new string((name ?? string.Empty)
            .Trim()
            .Where(character => !char.IsControl(character))
            .Take(24)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Player" : cleaned;
    }

    private static string NormalizeCode(string? code) =>
        new((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private string CreateCode()
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var code = new string(Enumerable.Range(0, 8)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
                .ToArray());
            if (!_rooms.ContainsKey(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("A unique room code could not be allocated.");
    }

    private sealed class Room(string code, BoardState board, string whiteName, string whiteParticipantId)
    {
        public string Code { get; } = code;
        public BoardState Board { get; set; } = board;
        public string WhiteName { get; } = whiteName;
        public string? BlackName { get; set; }
        public string? WhiteParticipantId { get; } = whiteParticipantId;
        public string? BlackParticipantId { get; set; }
        public bool WhiteConnected { get; private set; } = true;
        public bool BlackConnected { get; set; }
        public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
        public bool IsFinished => Board.Status is not GameStatus.InProgress;

        public bool TryGetSeat(string participantId, out PlayerColor seat)
        {
            if (StringComparer.Ordinal.Equals(WhiteParticipantId, participantId))
            {
                seat = PlayerColor.White;
                return true;
            }

            if (StringComparer.Ordinal.Equals(BlackParticipantId, participantId))
            {
                seat = PlayerColor.Black;
                return true;
            }

            seat = default;
            return false;
        }

        public void SetConnected(string participantId, bool connected)
        {
            if (StringComparer.Ordinal.Equals(WhiteParticipantId, participantId))
            {
                WhiteConnected = connected;
            }
            else if (StringComparer.Ordinal.Equals(BlackParticipantId, participantId))
            {
                BlackConnected = connected;
            }
        }

    }
}
