using System.Collections.Immutable;
using MistChess.Core.AI;
using MistChess.Core.Domain;
using MistChess.Core.Engine;
using MistChess.Core.Functional;

namespace MistChess.Web.Services;

public enum GameMode
{
    Bot,
    LocalTwoPlayer
}

public enum BoardAction
{
    Selected,
    Deselected,
    MoveApplied,
    PromotionRequired,
    Rejected
}

public sealed record CapturedPiece(PlayerColor CapturedBy, Piece Piece);

public sealed class GameSessionStore
{
    private readonly List<CapturedPiece> _captured = [];
    private BoardState _initial = BoardState.Initial;
    private BoardState _board = BoardState.Initial;
    private Position? _selected;
    private Position? _promotionFrom;
    private Position? _promotionTo;
    private int? _replayStep;
    private ReplayPerspective _replayPerspective = ReplayPerspective.WhiteFog;
    private ulong _botSeed;

    public event Action? Changed;

    public BoardState Board => _board;
    public GameVariant Variant => _board.Variant;
    public GameMode Mode { get; private set; } = GameMode.Bot;
    public PlayerColor HumanColor { get; private set; } = PlayerColor.White;
    public BotProfile Bot { get; private set; } = BotProfiles.MistWalker;
    public Position? SelectedSquare => _selected;
    public Position? PromotionFrom => _promotionFrom;
    public Position? PromotionTo => _promotionTo;
    public bool IsBotThinking { get; private set; }
    public int? ReplayStep => _replayStep;
    public ReplayPerspective ReplayPerspective => _replayPerspective;
    public bool IsFinished => _board.Status is not GameStatus.InProgress;
    public bool IsReplayAvailable => !_board.History.IsEmpty;
    public bool IsCheck => Variant == GameVariant.ClassicChess && MoveGenerator.IsInCheck(_board, _board.Turn);
    public string Message { get; private set; } = "Выберите фигуру, чтобы увидеть допустимые ходы.";
    public ImmutableList<Move> History => _board.History;
    public ImmutableList<Move> LegalMoves => MoveGenerator.GetLegalMoves(_board, _board.Turn);

    public MaskedBoardState DisplayBoard
    {
        get
        {
            if (_replayStep is { } step)
            {
                return BuildReplayView(step, _replayPerspective);
            }

            if (Variant == GameVariant.ClassicChess)
            {
                return FogEngine.ProjectOmniscientBoard(_board);
            }

            var viewer = Mode == GameMode.LocalTwoPlayer ? _board.Turn : HumanColor;
            return FogEngine.ProjectBoard(_board, viewer, FogRules.Standard);
        }
    }

    public ImmutableHashSet<Position> LegalTargets
    {
        get
        {
            if (_selected is not { } selected || _replayStep is not null || !CanInteract)
            {
                return ImmutableHashSet<Position>.Empty;
            }

            var view = DisplayBoard;
            return LegalMoves
                .Where(move => move.From == selected && view.At(move.To) is SquareVisibility.Visible)
                .Select(move => move.To)
                .ToImmutableHashSet();
        }
    }

    public bool CanInteract => !IsBotThinking
        && !IsFinished
        && _replayStep is null
        && (Mode == GameMode.LocalTwoPlayer || _board.Turn == HumanColor);

    public bool IsAwaitingBot => Mode == GameMode.Bot && !IsFinished && _board.Turn != HumanColor;

    public void StartNewGame(GameVariant variant, GameMode mode, string botId, PlayerColor humanColor)
    {
        Mode = mode;
        HumanColor = humanColor;
        Bot = BotProfiles.All.FirstOrDefault(profile => profile.Id == botId) ?? BotProfiles.MistWalker;
        _initial = BoardState.Initial with { Variant = variant };
        _board = _initial;
        _captured.Clear();
        _selected = null;
        _promotionFrom = null;
        _promotionTo = null;
        _replayStep = null;
        _replayPerspective = humanColor == PlayerColor.White
            ? ReplayPerspective.WhiteFog
            : ReplayPerspective.BlackFog;
        IsBotThinking = false;
        _botSeed = (ulong)Random.Shared.NextInt64();
        Message = mode == GameMode.Bot
            ? $"Партия началась. Ваш цвет — {ColorName(humanColor)}."
            : "Партия началась. Ход белых.";
        NotifyChanged();
    }

    public BoardAction ClickSquare(Position square)
    {
        if (!CanInteract)
        {
            Message = _replayStep is not null
                ? "Закройте просмотр партии, чтобы продолжить игру."
                : IsFinished
                    ? "Партия завершена. Начните новую игру или откройте повтор."
                    : "Подождите, пока бот сделает ход.";
            NotifyChanged();
            return BoardAction.Rejected;
        }

        var view = DisplayBoard;
        if (_selected is null)
        {
            if (view.At(square) is SquareVisibility.Visible { Piece: { } piece } && piece.Color == _board.Turn)
            {
                _selected = square;
                Message = $"Выбрана фигура на {square.Algebraic}. Выберите подсвеченную клетку.";
                NotifyChanged();
                return BoardAction.Selected;
            }

            Message = "Выберите свою видимую фигуру.";
            NotifyChanged();
            return BoardAction.Rejected;
        }

        if (_selected == square)
        {
            _selected = null;
            Message = "Выбор отменён.";
            NotifyChanged();
            return BoardAction.Deselected;
        }

        if (view.At(square) is SquareVisibility.Visible { Piece: { } targetPiece } && targetPiece.Color == _board.Turn)
        {
            _selected = square;
            Message = $"Выбрана фигура на {square.Algebraic}.";
            NotifyChanged();
            return BoardAction.Selected;
        }

        var candidates = LegalMoves
            .Where(move => move.From == _selected && move.To == square)
            .ToImmutableArray();
        if (candidates.IsEmpty)
        {
            Message = "Этот ход выполнить нельзя.";
            NotifyChanged();
            return BoardAction.Rejected;
        }

        var promotions = candidates.OfType<PromotionMove>().ToImmutableArray();
        if (!promotions.IsEmpty)
        {
            _promotionFrom = _selected;
            _promotionTo = square;
            Message = "Выберите фигуру для превращения пешки.";
            NotifyChanged();
            return BoardAction.PromotionRequired;
        }

        return ApplyMove(candidates[0]) ? BoardAction.MoveApplied : BoardAction.Rejected;
    }

    public bool ChoosePromotion(PieceKind promotedTo)
    {
        if (_promotionFrom is not { } from || _promotionTo is not { } to)
        {
            return false;
        }

        var move = LegalMoves.OfType<PromotionMove>()
            .FirstOrDefault(candidate => candidate.From == from && candidate.To == to && candidate.PromotedTo == promotedTo);
        if (move is null)
        {
            Message = "Выбранный вариант превращения недоступен.";
            NotifyChanged();
            return false;
        }

        return ApplyMove(move);
    }

    public async Task PlayBotTurnAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAwaitingBot)
        {
            return;
        }

        var snapshot = _board;
        var profile = Bot;
        var seed = _botSeed++;
        IsBotThinking = true;
        Message = $"{profile.Name} оценивает позицию…";
        NotifyChanged();

        try
        {
            var choice = await Task.Run(() =>
            {
                var viewer = snapshot.Turn;
                var observation = FogEngine.ProjectBoard(snapshot, viewer, FogRules.Standard);
                var moves = MoveGenerator.GetLegalMoves(snapshot, viewer);
                return BotEngine.ChooseMove(observation, moves, profile, seed);
            }, cancellationToken);

            if (!ReferenceEquals(snapshot, _board))
            {
                return;
            }

            if (choice is Result<Move, BotError>.Ok ok)
            {
                ApplyMove(ok.Value, prefix: $"{profile.Name} сыграл {MoveText.Format(ok.Value)}.");
            }
            else
            {
                Message = "Бот не нашёл допустимый ход.";
            }
        }
        catch (OperationCanceledException)
        {
            Message = "Расчёт хода остановлен.";
        }
        finally
        {
            IsBotThinking = false;
            NotifyChanged();
        }
    }

    public bool Undo()
    {
        if (_board.History.IsEmpty || IsBotThinking)
        {
            return false;
        }

        var plies = Mode == GameMode.Bot && _board.History.Count >= 2 ? 2 : 1;
        var keep = Math.Max(0, _board.History.Count - plies);
        var replay = GameEngine.Replay(_initial, _board.History, keep);
        if (replay is not Result<BoardState, IllegalMoveError>.Ok ok)
        {
            Message = "Ходы не удалось восстановить.";
            NotifyChanged();
            return false;
        }

        _board = ok.Value;
        RebuildCapturedPieces();
        _selected = null;
        _replayStep = null;
        Message = "Последний ход отменён.";
        NotifyChanged();
        return true;
    }

    public void SetReplay(int? step, ReplayPerspective perspective)
    {
        if (step is null)
        {
            _replayStep = null;
            _selected = null;
            Message = "Возвращены к текущей позиции.";
        }
        else
        {
            _replayStep = Math.Clamp(step.Value, 0, _board.History.Count);
            _replayPerspective = perspective;
            _selected = null;
            Message = $"Повтор: ходов {_replayStep.Value}, обзор — {PerspectiveName(perspective)}.";
        }

        NotifyChanged();
    }

    public void CancelPromotion()
    {
        _promotionFrom = null;
        _promotionTo = null;
        Message = "Превращение отменено. Выберите ход ещё раз.";
        NotifyChanged();
    }

    public ImmutableArray<CapturedPiece> CapturedBy(PlayerColor color) =>
        _captured.Where(capture => capture.CapturedBy == color).ToImmutableArray();

    public string TurnLabel => _board.Status switch
    {
        GameStatus.Won { Winner: var winner } => $"Победили {ColorName(winner)}",
        GameStatus.Draw { Reason: var reason } => $"Ничья: {DrawReasonName(reason)}",
        _ when IsBotThinking => $"Ходит {Bot.Name}",
        _ => $"Ход {ColorName(_board.Turn)}"
    };

    public string GameModeLabel => Mode == GameMode.Bot
        ? $"Против бота · {Bot.Name}"
        : "Два игрока · один экран";

    private bool ApplyMove(Move move, string? prefix = null)
    {
        var capturedAt = move switch
        {
            EnPassantMove enPassant => enPassant.CapturedPawnPosition,
            CaptureMove => move.To,
            PromotionMove { IsCapture: true } => move.To,
            _ => (Position?)null
        };
        if (capturedAt is { } position && _board.PieceAt(position) is { } captured)
        {
            _captured.Add(new CapturedPiece(_board.Turn, captured));
        }

        var applied = GameEngine.ApplyMove(_board, move);
        if (applied is not Result<BoardState, IllegalMoveError>.Ok ok)
        {
            Message = "Этот ход больше недоступен. Выберите другую клетку.";
            NotifyChanged();
            return false;
        }

        _board = ok.Value;
        _selected = null;
        _promotionFrom = null;
        _promotionTo = null;
        _replayStep = null;
        Message = prefix ?? $"Ход {MoveText.Format(move)} выполнен.";
        if (_board.Status is GameStatus.Won { Winner: var winner })
        {
            Message = $"Король захвачен. Победили {ColorName(winner)}.";
        }
        else if (_board.Status is GameStatus.Draw { Reason: var reason })
        {
            Message = $"Партия завершилась вничью: {DrawReasonName(reason)}.";
        }

        NotifyChanged();
        return true;
    }

    private MaskedBoardState BuildReplayView(int step, ReplayPerspective perspective)
    {
        var replay = GameEngine.Replay(_initial, _board.History, step);
        if (replay is not Result<BoardState, IllegalMoveError>.Ok ok)
        {
            return FogEngine.ProjectOmniscientBoard(_initial);
        }

        return perspective switch
        {
            ReplayPerspective.WhiteFog => FogEngine.ProjectBoard(ok.Value, PlayerColor.White, FogRules.Standard),
            ReplayPerspective.BlackFog => FogEngine.ProjectBoard(ok.Value, PlayerColor.Black, FogRules.Standard),
            ReplayPerspective.Omniscient => FogEngine.ProjectOmniscientBoard(ok.Value),
            _ => FogEngine.ProjectOmniscientBoard(ok.Value)
        };
    }

    private void RebuildCapturedPieces()
    {
        _captured.Clear();
        var current = _initial;
        foreach (var move in _board.History)
        {
            var capturePosition = move switch
            {
                EnPassantMove enPassant => enPassant.CapturedPawnPosition,
                CaptureMove => move.To,
                PromotionMove { IsCapture: true } => move.To,
                _ => (Position?)null
            };
            if (capturePosition is { } square && current.PieceAt(square) is { } captured)
            {
                _captured.Add(new CapturedPiece(current.Turn, captured));
            }

            if (GameEngine.ApplyMove(current, move) is Result<BoardState, IllegalMoveError>.Ok ok)
            {
                current = ok.Value;
            }
        }
    }

    private void NotifyChanged() => Changed?.Invoke();

    public static string ColorName(PlayerColor color) => color == PlayerColor.White ? "белых" : "чёрных";

    public static string PerspectiveName(ReplayPerspective perspective) => perspective switch
    {
        ReplayPerspective.WhiteFog => "туман белых",
        ReplayPerspective.BlackFog => "туман чёрных",
        ReplayPerspective.Omniscient => "полный обзор",
        _ => "обзор"
    };

    private static string DrawReasonName(DrawReason reason) => reason switch
    {
        DrawReason.Stalemate => "пат",
        DrawReason.FiftyMoveRule => "правило 50 ходов",
        DrawReason.InsufficientMaterial => "недостаточно материала",
        _ => "правила игры"
    };
}

public static class MoveText
{
    public static string Format(Move move)
    {
        var notation = move switch
        {
            CastlingMove { To.File: 6 } => "O-O",
            CastlingMove => "O-O-O",
            PromotionMove promotion => $"{promotion.From.Algebraic}{(promotion.IsCapture ? "x" : "-")}{promotion.To.Algebraic}={KindLetter(promotion.PromotedTo)}",
            CaptureMove => $"{move.From.Algebraic}x{move.To.Algebraic}",
            EnPassantMove => $"{move.From.Algebraic}x{move.To.Algebraic} e.p.",
            _ => $"{move.From.Algebraic}-{move.To.Algebraic}"
        };

        return notation;
    }

    private static char KindLetter(PieceKind kind) => kind switch
    {
        PieceKind.Queen => 'Q',
        PieceKind.Rook => 'R',
        PieceKind.Bishop => 'B',
        PieceKind.Knight => 'N',
        _ => 'Q'
    };
}
