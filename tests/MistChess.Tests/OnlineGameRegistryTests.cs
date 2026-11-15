using MistChess.Core.Domain;
using MistChess.Web.Services;
using Xunit;

namespace MistChess.Tests;

public sealed class OnlineGameRegistryTests
{
    [Fact]
    public void CreateAndJoinRoomAssignOppositeSeatsAndHideUnseenPieces()
    {
        var registry = new OnlineGameRegistry();
        var created = registry.CreateRoom("white-player", "White", GameVariant.FogOfWar);
        var whiteViews = new List<OnlineGameView>();
        var blackViews = new List<OnlineGameView>();
        registry.Subscribe("white-player", whiteViews.Add);

        var joined = registry.JoinRoom("black-player", created.Code!, "Black");
        registry.Subscribe("black-player", blackViews.Add);

        Assert.True(created.Success);
        Assert.Equal(8, created.Code!.Length);
        Assert.Equal(PlayerColor.White, created.Seat);
        Assert.True(joined.Success);
        Assert.Equal(PlayerColor.Black, joined.Seat);
        Assert.Equal("Black", whiteViews[^1].BlackName);

        var hiddenWhitePawn = blackViews[^1].Squares.Single(square => square.Position == new Position(0, 1));
        Assert.False(hiddenWhitePawn.IsVisible);
        Assert.Null(hiddenWhitePawn.PieceKind);
        Assert.Null(hiddenWhitePawn.PieceColor);
    }

    [Fact]
    public void RoomRejectsWrongTurnAndPublishesOnlyProjectedOpponentPosition()
    {
        var registry = new OnlineGameRegistry();
        var created = registry.CreateRoom("white-player", "White", GameVariant.FogOfWar);
        registry.JoinRoom("black-player", created.Code!, "Black");
        var blackViews = new List<OnlineGameView>();
        registry.Subscribe("black-player", blackViews.Add);

        var blackMovesFirst = registry.TryMove("black-player", created.Code!, new Position(4, 6), new Position(4, 4));
        Assert.False(blackMovesFirst.Success);

        var whiteMove = registry.TryMove("white-player", created.Code!, new Position(4, 1), new Position(4, 3));

        Assert.True(whiteMove.Success);
        Assert.Equal(PlayerColor.Black, blackViews[^1].Turn);
        var advancedPawnSquare = blackViews[^1].Squares.Single(square => square.Position == new Position(4, 3));
        Assert.False(advancedPawnSquare.IsVisible);
        Assert.Null(advancedPawnSquare.PieceKind);
    }

    [Fact]
    public void RoomRejectsInvalidCoordinatesAndPlayersCannotJoinTwice()
    {
        var registry = new OnlineGameRegistry();
        var created = registry.CreateRoom("white-player", "White", GameVariant.ClassicChess);
        var secondRoom = registry.CreateRoom("white-player", "White", GameVariant.FogOfWar);

        Assert.True(created.Success);
        Assert.False(secondRoom.Success);
        var invalidMove = registry.TryMove("white-player", created.Code!, new Position(-1, 0), new Position(0, 0));
        Assert.False(invalidMove.Success);

        var joined = registry.JoinRoom("black-player", created.Code!, "Black");
        Assert.True(joined.Success);
        Assert.False(registry.JoinRoom("third-player", created.Code!, "Third").Success);
    }

    [Fact]
    public void LeavingAnActiveRoomEndsTheGameForTheRemainingPlayer()
    {
        var registry = new OnlineGameRegistry();
        var created = registry.CreateRoom("white-player", "White", GameVariant.FogOfWar);
        registry.JoinRoom("black-player", created.Code!, "Black");
        var whiteViews = new List<OnlineGameView>();
        registry.Subscribe("white-player", whiteViews.Add);
        registry.Subscribe("black-player", _ => { });

        registry.LeaveRoom("black-player");

        Assert.True(whiteViews[^1].IsFinished);
        Assert.Equal("Победа", whiteViews[^1].Status);
        Assert.False(whiteViews[^1].IsOpponentConnected);
    }
}
