namespace Compositor.Core;

public enum GraySelectionOperation { Replace, Add, Subtract }

public sealed class GrayTileRaster
{
    private readonly Dictionary<int, byte[]> tiles;
    private int Columns => (Width + TileRaster.TileSize - 1) / TileRaster.TileSize;
    public int Width { get; }
    public int Height { get; }

    public GrayTileRaster(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        tiles = [];
    }

    private GrayTileRaster(int width, int height, Dictionary<int, byte[]> tiles)
    {
        Width = width;
        Height = height;
        this.tiles = tiles;
    }

    public (int Width, int Height) TileDimensions(int column, int row)
    {
        if (column < 0 || row < 0 || (long)column * TileRaster.TileSize >= Width || (long)row * TileRaster.TileSize >= Height)
            throw new ArgumentOutOfRangeException(nameof(column));
        return (Math.Min(TileRaster.TileSize, Width - column * TileRaster.TileSize),
            Math.Min(TileRaster.TileSize, Height - row * TileRaster.TileSize));
    }

    public byte[] ReadTileCopy(int column, int row)
    {
        var size = TileDimensions(column, row);
        return tiles.TryGetValue(row * Columns + column, out var tile)
            ? (byte[])tile.Clone() : new byte[size.Width * size.Height];
    }

    public GrayTileRaster ReplaceTile(int column, int row, ReadOnlySpan<byte> coverage)
    {
        var size = TileDimensions(column, row);
        if (coverage.Length != size.Width * size.Height)
            throw new ArgumentException("Mask tile data length does not match its dimensions.", nameof(coverage));
        var next = new Dictionary<int, byte[]>(tiles)
        {
            [row * Columns + column] = coverage.ToArray()
        };
        return new GrayTileRaster(Width, Height, next);
    }

    public static GrayTileRaster Rectangle(int width, int height, int left, int top, int right, int bottom)
    {
        if (width < 1 || height < 1 || left < 0 || top < 0 || right > width || bottom > height || left >= right || top >= bottom)
            throw new ArgumentOutOfRangeException(nameof(left));
        var result = new GrayTileRaster(width, height);
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            int tileLeft = column * TileRaster.TileSize, tileTop = row * TileRaster.TileSize;
            byte[] coverage = new byte[size.Width * size.Height];
            int fillLeft = Math.Max(left, tileLeft), fillTop = Math.Max(top, tileTop);
            int fillRight = Math.Min(right, tileLeft + size.Width), fillBottom = Math.Min(bottom, tileTop + size.Height);
            if (fillLeft < fillRight && fillTop < fillBottom)
                for (int y = fillTop; y < fillBottom; y++)
                    coverage.AsSpan((y - tileTop) * size.Width + fillLeft - tileLeft, fillRight - fillLeft).Fill(255);
            result = result.ReplaceTile(column, row, coverage);
        }
        return result;
    }

    public static GrayTileRaster FromCoverage(int width, int height, ReadOnlySpan<byte> coverage)
    {
        if (coverage.Length != (long)width * height)
            throw new ArgumentException("Mask data length does not match its dimensions.", nameof(coverage));
        var result = new GrayTileRaster(width, height);
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            byte[] tile = new byte[size.Width * size.Height];
            for (int y = 0; y < size.Height; y++)
                coverage.Slice((row * TileRaster.TileSize + y) * width + column * TileRaster.TileSize, size.Width)
                    .CopyTo(tile.AsSpan(y * size.Width, size.Width));
            result = result.ReplaceTile(column, row, tile);
        }
        return result;
    }

    public static GrayTileRaster Polygon(int width, int height, IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 3) throw new ArgumentException("A polygon needs at least three points.", nameof(points));
        var result = new GrayTileRaster(width, height);
        double left = points.Min(point => point.X), top = points.Min(point => point.Y);
        double right = points.Max(point => point.X), bottom = points.Max(point => point.Y);
        int x0 = Math.Max(0, (int)Math.Floor(left)), y0 = Math.Max(0, (int)Math.Floor(top));
        int x1 = Math.Min(width, (int)Math.Ceiling(right)), y1 = Math.Min(height, (int)Math.Ceiling(bottom));
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            int tileLeft = column * TileRaster.TileSize, tileTop = row * TileRaster.TileSize;
            byte[] coverage = new byte[size.Width * size.Height];
            int fillLeft = Math.Max(x0, tileLeft), fillTop = Math.Max(y0, tileTop);
            int fillRight = Math.Min(x1, tileLeft + size.Width), fillBottom = Math.Min(y1, tileTop + size.Height);
            for (int y = fillTop; y < fillBottom; y++)
            for (int x = fillLeft; x < fillRight; x++)
                if (Contains(points, x + 0.5, y + 0.5)) coverage[(y - tileTop) * size.Width + x - tileLeft] = 255;
            result = result.ReplaceTile(column, row, coverage);
        }
        return result;
    }

    public static GrayTileRaster Ellipse(int width, int height, int left, int top, int right, int bottom)
    {
        if (width < 1 || height < 1 || left < 0 || top < 0 || right > width || bottom > height || left >= right || top >= bottom)
            throw new ArgumentOutOfRangeException(nameof(left));
        var result = new GrayTileRaster(width, height);
        double centerX = (left + right) / 2.0, centerY = (top + bottom) / 2.0;
        double radiusX = (right - left) / 2.0, radiusY = (bottom - top) / 2.0;
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            int tileLeft = column * TileRaster.TileSize, tileTop = row * TileRaster.TileSize;
            byte[] coverage = new byte[size.Width * size.Height];
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                double dx = (tileLeft + x + 0.5 - centerX) / radiusX;
                double dy = (tileTop + y + 0.5 - centerY) / radiusY;
                if (dx * dx + dy * dy <= 1) coverage[y * size.Width + x] = 255;
            }
            result = result.ReplaceTile(column, row, coverage);
        }
        return result;
    }

    public GrayTileRaster Combine(GrayTileRaster other, GraySelectionOperation operation)
    {
        if (Width != other.Width || Height != other.Height)
            throw new ArgumentException("Selection dimensions must match.", nameof(other));
        var result = new GrayTileRaster(Width, Height);
        for (int row = 0; row * TileRaster.TileSize < Height; row++)
        for (int column = 0; column * TileRaster.TileSize < Width; column++)
        {
            byte[] first = ReadTileCopy(column, row), second = other.ReadTileCopy(column, row);
            for (int i = 0; i < first.Length; i++)
            {
                int a = first[i], b = second[i];
                first[i] = operation switch
                {
                    GraySelectionOperation.Replace => (byte)b,
                    GraySelectionOperation.Add => (byte)(a + (b * (255 - a) + 127) / 255),
                    GraySelectionOperation.Subtract => (byte)((a * (255 - b) + 127) / 255),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };
            }
            result = result.ReplaceTile(column, row, first);
        }
        return result;
    }

    private static bool Contains(IReadOnlyList<(double X, double Y)> points, double x, double y)
    {
        bool inside = false;
        for (int i = 0, previous = points.Count - 1; i < points.Count; previous = i++)
        {
            var currentPoint = points[i]; var previousPoint = points[previous];
            if ((currentPoint.Y > y) != (previousPoint.Y > y) &&
                x < (previousPoint.X - currentPoint.X) * (y - currentPoint.Y) /
                    (previousPoint.Y - currentPoint.Y) + currentPoint.X)
                inside = !inside;
        }
        return inside;
    }

    public long CoveredPixels
    {
        get
        {
            long count = 0;
            for (int row = 0; row * TileRaster.TileSize < Height; row++)
            for (int column = 0; column * TileRaster.TileSize < Width; column++)
                count += ReadTileCopy(column, row).LongCount(value => value != 0);
            return count;
        }
    }
}
