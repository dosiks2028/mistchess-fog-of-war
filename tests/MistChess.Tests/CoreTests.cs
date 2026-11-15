using System.Collections.Immutable;
using MistChess.Core.AI;
using MistChess.Core.Domain;
using MistChess.Core.Engine;
using MistChess.Core.Functional;
using Xunit;

namespace MistChess.Tests;

public sealed class CoreTests
{
    [Fact]
    public void InitialPositionHasTwentyLegalMoves()
    {
        Assert.Equal(20, MoveGenerator.GetLegalMoves(BoardState.Initial, PlayerColor.White).Count);
    }

    [Fact]
    public void InitialBoardContainsThirtyTwoPieces()
    {
        Assert.Equal(32, BoardState.Initial.Pieces.Count);
    }

    [Theory]
    [InlineData("a1", 0, 0)]
    [InlineData("h8", 7, 7)]
    [InlineData("E4", 4, 3)]
    public void PositionParsesValidAlgebraicCoordinates(string algebraic, int file, int rank)
    {
        var result = Position.Parse(algebraic);
        Assert.Equal(new Option<Position>.Some(new Position(file, rank)), result);
    }

    [Theory]
    [InlineData(null!)]
    [InlineData("")]
    [InlineData("i4")]
    [InlineData("a9")]
    [InlineData("abc")]
    public void PositionRejectsInvalidAlgebraicCoordinates(string? algebraic)
    {
        Assert.True(Position.Parse(algebraic).IsNone);
    }

    [Fact]
    public void ResultMapTransformsSuccess()
    {
        var result = Result<int, string>.Success(3).Map(value => value * 4);
        Assert.Equal(12, ((Result<int, string>.Ok)result).Value);
    }

    [Fact]
    public void ResultBindPreservesFailure()
    {
        var result = Result<int, string>.Failure("invalid").Bind(value => Result<string, string>.Success(value.ToString()));
        Assert.Equal("invalid", ((Result<string, string>.Err)result).Error);
    }

    [Fact]
    public void ResultTapRunsOnlyOnSuccess()
    {
        var observed = 0;
        Result<int, string>.Success(9).Tap(value => observed = value);
        Assert.Equal(9, observed);
    }

    [Fact]
    public void OptionMapTransformsPresentValue()
    {
        var option = Option<int>.SomeOf(5).Map(value => value + 2);
        Assert.Equal(7, ((Option<int>.Some)option).Value);
    }

    [Fact]
    public void OptionBindPreservesAbsence()
    {
        var option = Option<int>.None.Bind(value => Option<string>.SomeOf(value.ToString()));
        Assert.True(option.IsNone);
    }

    [Fact]
    public void OptionMatchHandlesBothCases()
    {
        Assert.Equal("none", Option<int>.None.Match(value => value.ToString(), () => "none"));
        Assert.Equal("7", Option<int>.SomeOf(7).Match(value => value.ToString(), () => "none"));
    }

    [Fact]
    public void KnightJumpsOverBlockingPieces()
    {
        var board = PositionBoard(
            ("a1", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("c3", new Knight(PlayerColor.White)),
            ("c2", new Pawn(PlayerColor.White)));
        var moves = MoveGenerator.GetLegalMoves(board, PlayerColor.White);
        Assert.Contains(moves, move => move.From == At("c3") && move.To == At("b5"));
    }

    [Fact]
    public void SlidingPieceCannotMoveThroughBlockingPiece()
    {
        var board = PositionBoard(
            ("a1", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("a4", new Rook(PlayerColor.White)),
            ("a6", new Pawn(PlayerColor.White)));
        var moves = MoveGenerator.GetLegalMoves(board, PlayerColor.White);
        Assert.DoesNotContain(moves, move => move.From == At("a4") && move.To == At("a7"));
    }

    [Fact]
    public void BishopMovesDiagonallyUntilBlocked()
    {
        var board = PositionBoard(
            ("a1", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("c1", new Bishop(PlayerColor.White)),
            ("e3", new Pawn(PlayerColor.White)));
        var moves = MoveGenerator.GetLegalMoves(board, PlayerColor.White);
        Assert.Contains(moves, move => move.From == At("c1") && move.To == At("d2"));
        Assert.DoesNotContain(moves, move => move.From == At("c1") && move.To == At("f4"));
    }

    [Fact]
    public void QueenMovesAlongFilesRanksAndDiagonals()
    {
        var board = PositionBoard(
            ("a1", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("d4", new Queen(PlayerColor.White)));
        var moves = MoveGenerator.GetLegalMoves(board, PlayerColor.White);
        Assert.Contains(moves, move => move.From == At("d4") && move.To == At("d7"));
        Assert.Contains(moves, move => move.From == At("d4") && move.To == At("g7"));
    }

    [Fact]
    public void KingMovesOneSquareInAnyDirection()
    {
        var board = PositionBoard(
            ("e4", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)));
        var moves = MoveGenerator.GetLegalMoves(board, PlayerColor.White);
        Assert.Contains(moves, move => move.From == At("e4") && move.To == At("f5"));
        Assert.DoesNotContain(moves, move => move.From == At("e4") && move.To == At("g6"));
    }

    [Fact]
    public void PawnCanMoveTwoSquaresFromStartingRank()
    {
        var moves = MoveGenerator.GetLegalMoves(BoardState.Initial, PlayerColor.White);
        Assert.Contains(moves, move => move.From == At("e2") && move.To == At("e4"));
    }

    [Fact]
    public void PawnCannotMoveTwoSquaresAfterLeavingStartingRank()
    {
        var board = PositionBoard(
            ("a1", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("e3", new Pawn(PlayerColor.White)));
        Assert.DoesNotContain(MoveGenerator.GetLegalMoves(board, PlayerColor.White), move => move.To == At("e5"));
    }

    [Fact]
    public void PawnCapturesDiagonallyButDoesNotCaptureForward()
    {
        var board = PositionBoard(
            ("a1", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("e4", new Pawn(PlayerColor.White)),
            ("d5", new Knight(PlayerColor.Black)),
            ("e5", new Rook(PlayerColor.Black)));
        var moves = MoveGenerator.GetLegalMoves(board, PlayerColor.White);
        Assert.Contains(moves, move => move is CaptureMove && move.From == At("e4") && move.To == At("d5"));
        Assert.DoesNotContain(moves, move => move.From == At("e4") && move.To == At("e5"));
    }

    [Fact]
    public void CheckDetectionFindsRookFileAttack()
    {
        var board = PositionBoard(
            ("e1", new King(PlayerColor.White)),
            ("a8", new King(PlayerColor.Black)),
            ("e8", new Rook(PlayerColor.Black)));
        Assert.True(MoveGenerator.IsInCheck(board, PlayerColor.White));
    }

    [Fact]
    public void KingCannotMoveIntoAnAttackedSquare()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("a8", new King(PlayerColor.Black)),
            ("e8", new Rook(PlayerColor.Black))
        }, variant: GameVariant.ClassicChess);
        Assert.DoesNotContain(MoveGenerator.GetLegalMoves(board, PlayerColor.White), move => move.To == At("e2"));
    }

    [Fact]
    public void FogVariantAllowsKingToMoveIntoAnAttackedSquare()
    {
        var board = PositionBoard(
            ("e1", new King(PlayerColor.White)),
            ("a8", new King(PlayerColor.Black)),
            ("e8", new Rook(PlayerColor.Black)));
        Assert.Contains(MoveGenerator.GetLegalMoves(board, PlayerColor.White), move =>
            move.From == At("e1") && move.To == At("e2"));
    }

    [Theory]
    [InlineData(CastlingRights.WhiteKingSide, "g1")]
    [InlineData(CastlingRights.WhiteQueenSide, "c1")]
    public void CastlingMovesKingAndRook(CastlingRights right, string destination)
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("a8", new King(PlayerColor.Black)),
            ("a1", new Rook(PlayerColor.White)),
            ("h1", new Rook(PlayerColor.White))
        }, rights: right);
        var castle = Assert.Single(MoveGenerator.GetLegalMoves(board, PlayerColor.White),
            move => move is CastlingMove && move.To == At(destination));
        var result = Apply(board, castle);
        Assert.IsType<King>(result.PieceAt(At(destination)));
        var rookTarget = destination == "g1" ? "f1" : "d1";
        Assert.IsType<Rook>(result.PieceAt(At(rookTarget)));
    }

    [Fact]
    public void CastlingIsUnavailableWhileInCheck()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("a1", new Rook(PlayerColor.White)),
            ("h1", new Rook(PlayerColor.White)),
            ("e2", new Rook(PlayerColor.Black))
        }, rights: CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide, variant: GameVariant.ClassicChess);
        Assert.DoesNotContain(MoveGenerator.GetLegalMoves(board, PlayerColor.White), move => move is CastlingMove);
    }

    [Fact]
    public void FogVariantCanCastleThroughAnAttackedSquare()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("a8", new King(PlayerColor.Black)),
            ("h1", new Rook(PlayerColor.White)),
            ("f8", new Rook(PlayerColor.Black))
        }, rights: CastlingRights.WhiteKingSide);
        Assert.Contains(MoveGenerator.GetLegalMoves(board, PlayerColor.White), move =>
            move is CastlingMove && move.To == At("g1"));
    }

    [Fact]
    public void ClassicCastlingCannotCrossAnAttackedTransitSquare()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("a8", new King(PlayerColor.Black)),
            ("h1", new Rook(PlayerColor.White)),
            ("f8", new Rook(PlayerColor.Black))
        }, rights: CastlingRights.WhiteKingSide, variant: GameVariant.ClassicChess);
        Assert.DoesNotContain(MoveGenerator.GetLegalMoves(board, PlayerColor.White), move => move is CastlingMove);
    }

    [Theory]
    [InlineData(PieceKind.Queen)]
    [InlineData(PieceKind.Rook)]
    [InlineData(PieceKind.Bishop)]
    [InlineData(PieceKind.Knight)]
    public void PawnPromotionOffersEveryStandardPiece(PieceKind kind)
    {
        var board = PromotionBoard();
        Assert.Contains(MoveGenerator.GetLegalMoves(board, PlayerColor.White), move =>
            move is PromotionMove { PromotedTo: var promoted } && promoted == kind);
    }

    [Fact]
    public void PromotionCreatesSelectedPieceAndPreservesOriginalBoard()
    {
        var board = PromotionBoard();
        var move = Assert.Single(MoveGenerator.GetLegalMoves(board, PlayerColor.White).OfType<PromotionMove>(),
            candidate => candidate.PromotedTo == PieceKind.Knight);
        var result = Apply(board, move);
        Assert.IsType<Knight>(result.PieceAt(At("a8")));
        Assert.IsType<Pawn>(board.PieceAt(At("a7")));
        Assert.True(board.IsEmpty(At("a8")));
    }

    [Fact]
    public void EnPassantCapturesThePawnBehindTheDestination()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("e5", new Pawn(PlayerColor.White)),
            ("d7", new Pawn(PlayerColor.Black))
        }, turn: PlayerColor.Black);
        var afterDoubleStep = Apply(board, new NormalMove(At("d7"), At("d5")));
        var enPassant = Assert.Single(MoveGenerator.GetLegalMoves(afterDoubleStep, PlayerColor.White)
            .OfType<EnPassantMove>());
        var result = Apply(afterDoubleStep, enPassant);
        Assert.IsType<Pawn>(result.PieceAt(At("d6")));
        Assert.Null(result.PieceAt(At("d5")));
    }

    [Fact]
    public void CapturingKingWinsFogOfWarMatch()
    {
        var board = PositionBoard(
            ("a1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("e1", new Rook(PlayerColor.White)));
        var result = Apply(board, new CaptureMove(At("e1"), At("e8")));
        Assert.Equal(new GameStatus.Won(PlayerColor.White), result.Status);
    }

    [Fact]
    public void IllegalMoveReturnsDomainErrorWithoutChangingBoard()
    {
        var board = BoardState.Initial;
        var result = GameEngine.ApplyMove(board, new NormalMove(At("e2"), At("e5")));
        Assert.IsType<Result<BoardState, IllegalMoveError>.Err>(result);
        Assert.Equal(32, board.Pieces.Count);
    }

    [Fact]
    public void ApplyingMovePreservesPreviousImmutableState()
    {
        var board = BoardState.Initial;
        var after = Apply(board, new NormalMove(At("e2"), At("e4")));
        Assert.IsType<Pawn>(board.PieceAt(At("e2")));
        Assert.Null(board.PieceAt(At("e4")));
        Assert.IsType<Pawn>(after.PieceAt(At("e4")));
        Assert.Equal(PlayerColor.Black, after.Turn);
    }

    [Fact]
    public void MovingKingRemovesBothCastlingRights()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("a1", new Rook(PlayerColor.White)),
            ("h1", new Rook(PlayerColor.White)),
        }, rights: CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide);
        var result = Apply(board, new NormalMove(At("e1"), At("d1")));
        Assert.Equal(CastlingRights.None, result.CastlingRights);
    }

    [Fact]
    public void MovingHomeRookRemovesItsCastlingRight()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("a1", new Rook(PlayerColor.White))
        }, rights: CastlingRights.WhiteQueenSide);
        var result = Apply(board, new NormalMove(At("a1"), At("a2")));
        Assert.Equal(CastlingRights.None, result.CastlingRights);
    }

    [Fact]
    public void CapturingHomeRookRemovesItsCastlingRight()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("a1", new Rook(PlayerColor.White)),
            ("a8", new Rook(PlayerColor.Black))
        }, rights: CastlingRights.WhiteQueenSide | CastlingRights.BlackQueenSide);
        var result = Apply(board, new CaptureMove(At("a1"), At("a8")));
        Assert.Equal(CastlingRights.None, result.CastlingRights);
    }

    [Fact]
    public void PawnCanCaptureAndPromoteOnLastRank()
    {
        var board = PositionBoard(
            ("h1", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("a7", new Pawn(PlayerColor.White)),
            ("b8", new Rook(PlayerColor.Black)));
        var move = Assert.Single(MoveGenerator.GetLegalMoves(board, PlayerColor.White).OfType<PromotionMove>(),
            candidate => candidate.To == At("b8") && candidate.PromotedTo == PieceKind.Queen);
        var result = Apply(board, move);
        Assert.IsType<Queen>(result.PieceAt(At("b8")));
        Assert.Null(result.PieceAt(At("a7")));
    }

    [Fact]
    public void FoolsmateAdjudicatesCheckmate()
    {
        var board = BoardState.Initial with { Variant = GameVariant.ClassicChess };
        board = Apply(board, new NormalMove(At("f2"), At("f3")));
        board = Apply(board, new NormalMove(At("e7"), At("e5")));
        board = Apply(board, new NormalMove(At("g2"), At("g4")));
        board = Apply(board, new NormalMove(At("d8"), At("h4")));
        Assert.Equal(new GameStatus.Won(PlayerColor.Black), board.Status);
    }

    [Fact]
    public void StalemateIsAdjudicatedAsDraw()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("a1", new King(PlayerColor.White)),
            ("c2", new King(PlayerColor.Black)),
            ("c4", new Queen(PlayerColor.Black))
        }, turn: PlayerColor.Black, variant: GameVariant.ClassicChess);
        var result = Apply(board, new NormalMove(At("c4"), At("b3")));
        Assert.Equal(new GameStatus.Draw(DrawReason.Stalemate), result.Status);
    }

    [Fact]
    public void FiftyMoveRuleDrawsAfterOneHundredQuietPlies()
    {
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("a1", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("b1", new Rook(PlayerColor.White))
        }, halfmoveClock: 99, variant: GameVariant.ClassicChess);
        var result = Apply(board, new NormalMove(At("b1"), At("b2")));
        Assert.Equal(new GameStatus.Draw(DrawReason.FiftyMoveRule), result.Status);
    }

    [Fact]
    public void EventReplayReconstructsSameBoardAndHistory()
    {
        var moves = ImmutableList.Create<Move>(
            new NormalMove(At("e2"), At("e4")),
            new NormalMove(At("e7"), At("e5")),
            new NormalMove(At("g1"), At("f3")));
        var replay = GameEngine.Replay(BoardState.Initial, moves);
        var result = Assert.IsType<Result<BoardState, IllegalMoveError>.Ok>(replay).Value;
        var direct = moves.Aggregate(BoardState.Initial, Apply);
        Assert.Equal(direct.Pieces, result.Pieces);
        Assert.Equal(moves, result.History);
    }

    [Fact]
    public void ReplayCanReturnHistoricalPrefix()
    {
        var moves = ImmutableList.Create<Move>(
            new NormalMove(At("e2"), At("e4")),
            new NormalMove(At("e7"), At("e5")));
        var replay = GameEngine.Replay(BoardState.Initial, moves, step: 1);
        var result = Assert.IsType<Result<BoardState, IllegalMoveError>.Ok>(replay).Value;
        Assert.Equal(PlayerColor.Black, result.Turn);
        Assert.Single(result.History);
    }

    [Fact]
    public void ReplayProjectsWhiteFogBlackFogAndOmniscientViews()
    {
        var moves = ImmutableList.Create<Move>(new NormalMove(At("e2"), At("e4")));
        var whiteResult = FogEngine.ProjectReplay(BoardState.Initial, moves, 1, ReplayPerspective.WhiteFog);
        var blackResult = FogEngine.ProjectReplay(BoardState.Initial, moves, 1, ReplayPerspective.BlackFog);
        var omniscientResult = FogEngine.ProjectReplay(BoardState.Initial, moves, 1, ReplayPerspective.Omniscient);
        var white = Assert.IsType<Result<MaskedBoardState, IllegalMoveError>.Ok>(whiteResult).Value;
        var black = Assert.IsType<Result<MaskedBoardState, IllegalMoveError>.Ok>(blackResult).Value;
        var omniscient = Assert.IsType<Result<MaskedBoardState, IllegalMoveError>.Ok>(omniscientResult).Value;
        Assert.IsType<SquareVisibility.Hidden>(white.At(At("a8")));
        Assert.IsType<SquareVisibility.Hidden>(black.At(At("a1")));
        Assert.True(omniscient.IsOmniscient);
        Assert.Equal(new SquareVisibility.Visible(new Rook(PlayerColor.Black)), omniscient.At(At("a8")));
    }

    [Fact]
    public void ClassicFogHidesUnseenEnemyPieceWithoutLeakingItsValue()
    {
        var board = PositionBoard(
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("a8", new Queen(PlayerColor.Black)));
        var projection = FogEngine.ProjectBoard(board, PlayerColor.White, FogRules.ClassicAttackVision);
        Assert.IsType<SquareVisibility.Hidden>(projection.At(At("a8")));
        var allCoordinates = Enumerable.Range(0, 64)
            .Select(index => new Position(index % 8, index / 8))
            .ToImmutableHashSet();
        Assert.Equal(allCoordinates, projection.Squares.Keys.ToImmutableHashSet());
    }

    [Fact]
    public void FriendlyPiecesAreAlwaysVisible()
    {
        var board = PositionBoard(
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("a1", new Rook(PlayerColor.White)));
        var projection = FogEngine.ProjectBoard(board, PlayerColor.White, FogRules.AcousticFootsteps);
        Assert.Equal(new SquareVisibility.Visible(new Rook(PlayerColor.White)), projection.At(At("a1")));
    }

    [Fact]
    public void KingRadarRevealsTwoTileRadius()
    {
        var board = PositionBoard(
            ("e4", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("g6", new Queen(PlayerColor.Black)),
            ("h7", new Rook(PlayerColor.Black)));
        var projection = FogEngine.ProjectBoard(board, PlayerColor.White, FogRules.KingRadarVision);
        Assert.Equal(new SquareVisibility.Visible(new Queen(PlayerColor.Black)), projection.At(At("g6")));
        Assert.IsType<SquareVisibility.Hidden>(projection.At(At("h7")));
    }

    [Fact]
    public void AcousticFootstepsMarksNearbyOpponentMoveWithoutShowingPiece()
    {
        var move = new NormalMove(At("g4"), At("f2"));
        var board = PositionBoard(new (string Square, Piece Piece)[]
        {
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)),
            ("e2", new Pawn(PlayerColor.White)),
            ("f2", new Knight(PlayerColor.Black))
        }, turn: PlayerColor.White, history: ImmutableList.Create<Move>(move));
        var projection = FogEngine.ProjectBoard(board, PlayerColor.White, FogRules.AcousticFootsteps);
        Assert.IsType<SquareVisibility.AcousticRipple>(projection.At(At("f2")));
    }

    [Fact]
    public void FogRulesComposeTheirVisionSets()
    {
        var board = PositionBoard(
            ("e4", new King(PlayerColor.White)),
            ("h8", new King(PlayerColor.Black)),
            ("g6", new Queen(PlayerColor.Black)));
        var combined = FogRules.ClassicAttackVision.Combine(FogRules.KingRadarVision);
        var projection = FogEngine.ProjectBoard(board, PlayerColor.White, combined);
        Assert.Equal(new SquareVisibility.Visible(new Queen(PlayerColor.Black)), projection.At(At("g6")));
    }

    [Fact]
    public void BeliefMapIsNormalizedAndContainsOnlyHiddenSquares()
    {
        var board = PositionBoard(
            ("e1", new King(PlayerColor.White)),
            ("e8", new King(PlayerColor.Black)));
        var view = FogEngine.ProjectBoard(board, PlayerColor.White, FogRules.ClassicAttackVision);
        var beliefs = BotHeuristics.BuildBeliefMap(view, PlayerColor.Black);
        Assert.All(beliefs.Keys, position => Assert.IsType<SquareVisibility.Hidden>(view.At(position)));
        Assert.Equal(1d, beliefs.Values.Sum(), precision: 10);
    }

    [Fact]
    public void EveryBotReturnsALegalMoveFromTheSuppliedFogView()
    {
        var board = BoardState.Initial;
        var legalMoves = MoveGenerator.GetLegalMoves(board, PlayerColor.White);
        var view = FogEngine.ProjectBoard(board, PlayerColor.White, FogRules.Standard);
        foreach (var profile in BotProfiles.All)
        {
            var choice = BotEngine.ChooseMove(view, legalMoves, profile);
            var move = Assert.IsType<Result<Move, BotError>.Ok>(choice).Value;
            Assert.Contains(move, legalMoves);
        }
    }

    [Fact]
    public void SeededBotSelectionIsDeterministic()
    {
        var board = BoardState.Initial;
        var legalMoves = MoveGenerator.GetLegalMoves(board, PlayerColor.White);
        var view = FogEngine.ProjectBoard(board, PlayerColor.White, FogRules.Standard);
        var first = BotEngine.ChooseMove(view, legalMoves, BotProfiles.MistWalker, seed: 1234);
        var second = BotEngine.ChooseMove(view, legalMoves, BotProfiles.MistWalker, seed: 1234);
        Assert.Equal(first, second);
    }

    [Fact]
    public void BotReturnsErrorWhenItIsNotItsTurn()
    {
        var board = BoardState.Initial with { Turn = PlayerColor.Black };
        var view = FogEngine.ProjectBoard(board, PlayerColor.White, FogRules.Standard);
        var result = BotEngine.ChooseMove(view, ImmutableList<Move>.Empty, BotProfiles.MistWalker);
        Assert.IsType<Result<Move, BotError>.Err>(result);
    }

    private static BoardState PromotionBoard() => PositionBoard(
        ("h1", new King(PlayerColor.White)),
        ("h8", new King(PlayerColor.Black)),
        ("a7", new Pawn(PlayerColor.White)));

    private static BoardState PositionBoard(
        params (string Square, Piece Piece)[] entries) => PositionBoard((IEnumerable<(string Square, Piece Piece)>)entries);

    private static BoardState PositionBoard(
        IEnumerable<(string Square, Piece Piece)> entries,
        PlayerColor turn = PlayerColor.White,
        CastlingRights rights = CastlingRights.None,
        int halfmoveClock = 0,
        ImmutableList<Move>? history = null,
        GameVariant variant = GameVariant.FogOfWar) => new(
            entries.ToImmutableDictionary(entry => At(entry.Square), entry => entry.Piece),
            turn,
            rights,
            halfmoveClock: halfmoveClock,
            status: GameStatus.Playing,
            history: history,
            variant: variant);

    private static Position At(string algebraic) => ((Option<Position>.Some)Position.Parse(algebraic)).Value;

    private static BoardState Apply(BoardState board, Move move) =>
        Assert.IsType<Result<BoardState, IllegalMoveError>.Ok>(GameEngine.ApplyMove(board, move)).Value;
}
