using System.Collections;

namespace Lunet;

public readonly record struct Location
{
    public Location(int row, int col)
    {
        StartRow = row;
        StartCol = col;
        EndRow = row;
        EndCol = col;
    }

    public Location(int startRow, int startCol, int endRow, int endCol)
    {
        StartRow = startRow;
        StartCol = startCol;
        EndRow = endRow;
        EndCol = endCol;
    }

    public int StartRow { get; }
    public int StartCol { get; }
    public int EndRow { get; }
    public int EndCol { get; }

    public static Location Combine(Location startLoc, Location endLoc)
    {
        return new Location(
            startRow: startLoc.StartRow,
            startCol: startLoc.StartCol,
            endRow: endLoc.EndRow,
            endCol: endLoc.EndCol
        );
    }
}

public enum DiagnosticSeverity
{
    Error, Warning
}

public record struct Diagnostic(DiagnosticSeverity Severity, Location Location, string Message);

public class Diagnostics : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    public bool HasError => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _diagnostics.GetEnumerator();

    public void Add(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    public void AddError(Location location, string message)
    {
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, location, message));
    }
}