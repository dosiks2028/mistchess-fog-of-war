using MistChess.Core.Domain;
using MistChess.Core.AI;
using MistChess.Core.Engine;
using MistChess.Web.Services;
using Xunit;

namespace MistChess.Tests;

public sealed class GameSessionStoreTests
{
    [Fact]
    public void FogPlayerSeesLegalPawnDestinationsWhileEnemyPiecesRemainHidden()
    {
        var session = new GameSessionStore();
        session.StartNewGame(GameVariant.FogOfWar, GameMode.LocalTwoPlayer, BotProfiles.MistWalker.Id, PlayerColor.White);

        Assert.IsType<SquareVisibility.Hidden>(session.DisplayBoard.At(new Position(0, 6)));
        Assert.Equal(BoardAction.Selected, session.ClickSquare(new Position(4, 1)));
        Assert.Contains(new Position(4, 2), session.LegalTargets);
        Assert.Contains(new Position(4, 3), session.LegalTargets);

        Assert.Equal(BoardAction.MoveApplied, session.ClickSquare(new Position(4, 3)));
        Assert.IsType<Pawn>(session.Board.PieceAt(new Position(4, 3)));
    }

    [Fact]
    public void UndoRestoresTheOriginalImmutablePosition()
    {
        var session = new GameSessionStore();
        session.StartNewGame(GameVariant.ClassicChess, GameMode.LocalTwoPlayer, BotProfiles.MistWalker.Id, PlayerColor.White);
        session.ClickSquare(new Position(4, 1));
        session.ClickSquare(new Position(4, 3));

        Assert.True(session.Undo());
        Assert.Empty(session.Board.History);
        Assert.IsType<Pawn>(session.Board.PieceAt(new Position(4, 1)));
        Assert.Null(session.Board.PieceAt(new Position(4, 3)));
    }

    [Fact]
    public async Task UndoAgainstBotRewindsTheFullExchange()
    {
        var session = new GameSessionStore();
        session.StartNewGame(GameVariant.FogOfWar, GameMode.Bot, BotProfiles.MistWalker.Id, PlayerColor.White);
        session.ClickSquare(new Position(4, 1));
        session.ClickSquare(new Position(4, 3));
        await session.PlayBotTurnAsync();

        Assert.Equal(2, session.History.Count);
        Assert.True(session.Undo());
        Assert.Empty(session.History);
    }
}
