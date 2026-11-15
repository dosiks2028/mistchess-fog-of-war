using MistChess.Core.Functional;

namespace MistChess.Core.Domain;

public readonly record struct Position(int File, int Rank) : IComparable<Position>
{
    public bool IsOnBoard => File is >= 0 and < 8 && Rank is >= 0 and < 8;
    public string Algebraic => IsOnBoard ? $"{(char)('a' + File)}{Rank + 1}" : "??";

    public static Option<Position> Parse(string? algebraic)
    {
        if (algebraic is not { Length: 2 })
        {
            return Option<Position>.None;
        }

        var file = char.ToLowerInvariant(algebraic[0]) - 'a';
        var rank = algebraic[1] - '1';
        var position = new Position(file, rank);
        return position.IsOnBoard ? Option<Position>.SomeOf(position) : Option<Position>.None;
    }

    public int CompareTo(Position other)
    {
        var rankComparison = Rank.CompareTo(other.Rank);
        return rankComparison != 0 ? rankComparison : File.CompareTo(other.File);
    }

    public Position Offset(int fileDelta, int rankDelta) => new(File + fileDelta, Rank + rankDelta);
    public override string ToString() => Algebraic;
}
