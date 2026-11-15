using System.Collections.Immutable;
using MistChess.Core.Domain;
using MistChess.Core.Engine;

namespace MistChess.Web.Services;

public sealed class OnlineGameClient : IDisposable
{
    private readonly OnlineGameRegistry _registry;
    private readonly Action<OnlineGameView> _listener;
    private bool _disposed;

    public OnlineGameClient(OnlineGameRegistry registry)
    {
        _registry = registry;
        _listener = OnViewReceived;
    }

    public string ParticipantId { get; } = Guid.NewGuid().ToString("N");
    public string PlayerName { get; set; } = "Player";
    public string? RoomCode { get; private set; }
    public PlayerColor? Seat { get; private set; }
    public OnlineGameView? View { get; private set; }
    public MaskedBoardState? DisplayBoard => View is { } view
        ? new MaskedBoardState(
            view.Squares.ToImmutableDictionary(square => square.Position, square => square.IsVisible
                ? new SquareVisibility.Visible(square.PieceKind is { } kind && square.PieceColor is { } color ? PieceFactory.Create(kind, color) : null)
                : square.IsRipple ? new SquareVisibility.AcousticRipple() : SquareVisibility.HiddenSquare),
            view.MyColor,
            view.Turn,
            view.Variant)
        { IsOmniscient = view.Variant == GameVariant.ClassicChess }
        : null;
    public string Message { get; private set; } = "Create a room or enter an invite code.";
    public event Action? Changed;

    public RoomOperationResult CreateRoom(GameVariant variant)
    {
        var result = _registry.CreateRoom(ParticipantId, PlayerName, variant);
        CompleteOperation(result);
        return result;
    }

    public RoomOperationResult JoinRoom(string code)
    {
        var result = _registry.JoinRoom(ParticipantId, code, PlayerName);
        CompleteOperation(result);
        return result;
    }

    public RoomOperationResult TryMove(Position from, Position to, PieceKind? promotion = null)
    {
        if (RoomCode is null)
        {
            return new RoomOperationResult(false, "Join a room first.");
        }

        var result = _registry.TryMove(ParticipantId, RoomCode, from, to, promotion);
        Message = result.Message;
        NotifyChanged();
        return result;
    }

    public void LeaveRoom()
    {
        _registry.Unsubscribe(ParticipantId);
        _registry.LeaveRoom(ParticipantId);
        RoomCode = null;
        Seat = null;
        View = null;
        Message = "You left the room.";
        NotifyChanged();
    }

    private void CompleteOperation(RoomOperationResult result)
    {
        Message = result.Message;
        if (result.Success && result.Code is not null && result.Seat is { } seat)
        {
            RoomCode = result.Code;
            Seat = seat;
            View = result.View;
            _registry.Subscribe(ParticipantId, _listener);
        }

        NotifyChanged();
    }

    private void OnViewReceived(OnlineGameView view)
    {
        View = view;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registry.Unsubscribe(ParticipantId);
        _registry.MarkDisconnected(ParticipantId);
    }
}
