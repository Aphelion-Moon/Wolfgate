using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Spawning;

/// <summary>
/// Virtualised grid of spawn tiles. It reports the size of the whole result set so the scrollbar is honest, but
/// only ever holds the tiles for the rows on screen; the window refills those as the scroll moves.
/// </summary>
public sealed class WolfgateSpawnGrid : Container
{
    public const float Separation = 4;
    public const float MinCellWidth = 176;
    public const float CellHeight = 42;

    private int _totalItemCount;
    private int _itemOffset;

    /// <summary>How many tiles fit across, decided by the last measure.</summary>
    public int Columns { get; private set; } = 1;

    /// <summary>Width of one tile, stretched so a row fills the viewport exactly.</summary>
    public float CellWidth { get; private set; } = MinCellWidth;

    /// <summary>Number of entries in the full result set, on screen or not.</summary>
    public int TotalItemCount
    {
        get => _totalItemCount;
        set
        {
            if (_totalItemCount == value)
                return;

            _totalItemCount = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Index of the first child in the result set. Always a whole number of rows.</summary>
    public int ItemOffset
    {
        get => _itemOffset;
        set
        {
            if (_itemOffset == value)
                return;

            _itemOffset = value;
            InvalidateArrange();
        }
    }

    public int RowCount => Columns <= 0 ? 0 : (TotalItemCount + Columns - 1) / Columns;

    public static float RowHeight => CellHeight + Separation;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) && availableSize.X > 0 ? availableSize.X : MinCellWidth;

        Columns = Math.Max(1, (int) ((width + Separation) / (MinCellWidth + Separation)));
        CellWidth = Math.Max(MinCellWidth, (width - (Columns - 1) * Separation) / Columns);

        var cell = new Vector2(CellWidth, CellHeight);
        foreach (var child in Children)
        {
            child.Measure(cell);
        }

        return new Vector2(width, Math.Max(0, RowCount * RowHeight - Separation));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var startRow = Columns <= 0 ? 0 : ItemOffset / Columns;
        var index = 0;

        foreach (var child in Children)
        {
            var row = startRow + index / Columns;
            var column = index % Columns;

            child.Arrange(UIBox2.FromDimensions(
                column * (CellWidth + Separation),
                row * RowHeight,
                CellWidth,
                CellHeight));

            index++;
        }

        return finalSize;
    }
}
