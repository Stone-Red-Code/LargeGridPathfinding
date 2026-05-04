using Microsoft.Xna.Framework;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace LargeGridPathfinding;

internal class GridFiller
{
    private int currentLabel = 1;
    private int currentObstacleLabel = -1;
    public int[,] Grid { get; }
    public int[,] WeightGrid { get; }
    public ConcurrentDictionary<int, Rectangle> PlacedRectangles { get; }
    public int Width { get; }
    public int Height { get; }

    public GridFiller(int width, int height, List<Rectangle> obstacles)
    {
        Width = width;
        Height = height;
        Grid = new int[height, width];
        WeightGrid = new int[height, width];
        PlacedRectangles = [];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                WeightGrid[y, x] = 1;
            }
        }

        foreach (Rectangle rectangle in obstacles)
        {
            PlaceObstacle(rectangle);
        }
    }

    public void FillGrid(int? x1 = null, int? y1 = null, int? x2 = null, int? y2 = null, bool fillAll = false, IProgress<float>? totalProgress = null, IProgress<float>? calculatingCandidatesProgress = null, IProgress<float>? placingCandidatesProgress = null)
    {
        x1 ??= 0;
        y1 ??= 0;
        x2 ??= Width;
        y2 ??= Height;

        if (fillAll)
        {
            PlacedRectangles.Clear();
            for (int y = y1.Value; y < y2; y++)
            {
                for (int x = x1.Value; x < x2; x++)
                {
                    if (Grid[y, x] > 0)
                    {
                        Grid[y, x] = 0;
                    }
                }
            }
        }

        // Collect all valid rectangle placements
        ConcurrentBag<(int x, int y, int w, int h, int weight)> candidates = [];

        int cells = Grid.GetLength(0) * Grid.GetLength(1);
        int areaPlaced = 0;
        int maxRectangleSize = Math.Max((Grid.GetLength(0) + Grid.GetLength(1)) / 20, 100);
        int reportInterval = cells / 100;

        do
        {
            candidates.Clear();

            Debug.WriteLine("Calculating candidates...");

            // Calculate all possible rectangle placements
            _ = Parallel.For(y1.Value, y2.Value, (y, loopState) =>
            {
                for (int x = x1.Value; x < x2; x++)
                {
                    if (Grid[y, x] == 0)
                    {
                        int tileWeight = WeightGrid[y, x];
                        (int w, int h) = GetMaxRectangleSize(x, y, maxRectangleSize, maxRectangleSize, tileWeight);

                        if (w > 0 && h > 0)
                        {
                            candidates.Add((x, y, w, h, tileWeight));
                        }

                        if (w >= maxRectangleSize && h >= maxRectangleSize)
                        {
                            loopState.Break();
                            return;
                        }

                        if (candidates.Count % reportInterval == 0)
                        {
                            calculatingCandidatesProgress?.Report((float)candidates.Count / (cells - areaPlaced));
                            Debug.WriteLine($"Candidates Progress: {candidates.Count} / {cells - areaPlaced} ({(float)candidates.Count / (cells - areaPlaced):P2})");
                        }
                    }
                }
            });

            Debug.WriteLine($"Candidates: {candidates.Count}");
            Debug.WriteLine("Sorting candidates...");

            // Sort by area (largest first) and prefer less stretched rectangles on ties
            List<(int x, int y, int w, int h, int weight)> sortedCandidates = [.. candidates];
            sortedCandidates.Sort((a, b) =>
            {
                int areaComparison = (b.w * b.h).CompareTo(a.w * a.h);
                if (areaComparison != 0)
                {
                    return areaComparison;
                }

                int stretchA = GetStretchFactor(a.w, a.h);
                int stretchB = GetStretchFactor(b.w, b.h);
                int stretchComparison = stretchA.CompareTo(stretchB);
                if (stretchComparison != 0)
                {
                    return stretchComparison;
                }

                return b.h.CompareTo(a.h);
            });

            Debug.WriteLine("Placing rectangles...");

            int placed = 0;
            int reportIntervalPlacing = Math.Max(sortedCandidates.Count / 10, 1);

            // Place rectangles, ensuring no overlap
            foreach ((int x, int y, int w, int h, int weight) in sortedCandidates)
            {
                // Check if area is still free
                if (IsAreaFree(x, y, w, h, weight))
                {
                    Rectangle rectangle = new Rectangle(x, y, w, h);
                    PlaceRectangle(rectangle);
                    areaPlaced += w * h;
                }

                placed++;

                if (placed % reportIntervalPlacing == 0)
                {
                    totalProgress?.Report((float)areaPlaced / cells);
                    placingCandidatesProgress?.Report((float)placed / (sortedCandidates.Count + 1));
                    Debug.WriteLine($"Placed Progress: {placed} / {sortedCandidates.Count} ({(float)placed / sortedCandidates.Count:P2})");
                }
            }

            totalProgress?.Report((float)areaPlaced / cells);
        } while (!candidates.IsEmpty);

        totalProgress?.Report(1);
        calculatingCandidatesProgress?.Report(1);
        placingCandidatesProgress?.Report(1);
    }

    public void PlaceObstacle(Rectangle rectangle)
    {
        // Check area around the rectangle to update all adjacent rectangles

        int y = int.Clamp(rectangle.Y - 1, 0, Height);
        int x = int.Clamp(rectangle.X - 1, 0, Width);
        int h = int.Clamp(rectangle.Y + rectangle.Height + 2, 0, Height);
        int w = int.Clamp(rectangle.X + rectangle.Width + 2, 0, Width);

        List<Rectangle> removedRectangles = [new Rectangle(x, y, w - x, h - y)];

        int obstacle = currentObstacleLabel--;

        for (int dy = y; dy < h; dy++)
        {
            for (int dx = x; dx < w; dx++)
            {
                if (Grid[dy, dx] > 0 && PlacedRectangles.ContainsKey(Grid[dy, dx]))
                {
                    removedRectangles.Add(RemoveRectangle(Grid[dy, dx]));
                }

                if (Grid[dy, dx] >= 0 && dy < rectangle.Y + rectangle.Height && dx < rectangle.X + rectangle.Width && dy >= rectangle.Y && dx >= rectangle.X)
                {
                    Grid[dy, dx] = obstacle;
                    WeightGrid[dy, dx] = 1;
                }
            }
        }

        if (removedRectangles.Count == 1)
        {
            return;
        }

        int minStartX = removedRectangles.Min(r => r.X);
        int minStartY = removedRectangles.Min(r => r.Y);
        int maxEndX = removedRectangles.Max(r => r.X + r.Width);
        int maxEndY = removedRectangles.Max(r => r.Y + r.Height);

        FillGrid(minStartX, minStartY, maxEndX, maxEndY);

        Debug.WriteLine("Placed obstacle");
        Debug.WriteLine($"Placed obstacle {rectangle.X}, {rectangle.Y}, {rectangle.Width}, {rectangle.Height}");
        Debug.WriteLine($"minX: {minStartX}, minY: {minStartY}, maxX: {maxEndX}, maxY: {maxEndY}");
    }

    public void RemoveObstacle(Rectangle rectangle)
    {
        // Check area around the rectangle to update all adjacent rectangles

        int y = int.Clamp(rectangle.Y - 1, 0, Height);
        int x = int.Clamp(rectangle.X - 1, 0, Width);
        int h = int.Clamp(rectangle.Y + rectangle.Height + 2, 0, Height);
        int w = int.Clamp(rectangle.X + rectangle.Width + 2, 0, Width);

        List<Rectangle> removedRectangles = [new Rectangle(x, y, w - x, h - y)];

        for (int dy = y; dy < h; dy++)
        {
            for (int dx = x; dx < w; dx++)
            {
                if (Grid[dy, dx] > 0)
                {
                    removedRectangles.Add(RemoveRectangle(Grid[dy, dx]));
                }

                if (Grid[dy, dx] < 0 && dy < rectangle.Y + rectangle.Height && dx < rectangle.X + rectangle.Width && dy >= rectangle.Y && dx >= rectangle.X)
                {
                    Grid[dy, dx] = 0;
                    WeightGrid[dy, dx] = 1;
                }
            }
        }

        int minStartX = removedRectangles.Min(r => r.X);
        int minStartY = removedRectangles.Min(r => r.Y);
        int maxEndX = removedRectangles.Max(r => r.X + r.Width);
        int maxEndY = removedRectangles.Max(r => r.Y + r.Height);

        FillGrid(minStartX, minStartY, maxEndX, maxEndY);

        Debug.WriteLine("Removed obstacle");
        Debug.WriteLine($"Placed obstacle {rectangle.X}, {rectangle.Y}, {rectangle.Width}, {rectangle.Height}");
        Debug.WriteLine($"minX: {minStartX}, minY: {minStartY}, maxX: {maxEndX}, maxY: {maxEndY}");
    }

    public void SetTileWeight(int x, int y, int weight)
    {
        SetTileWeights([new Point(x, y)], weight);
    }

    public void ResetTileWeight(int x, int y)
    {
        ResetTileWeights([new Point(x, y)]);
    }

    public void SetTileWeights(IEnumerable<Point> points, int weight)
    {
        int clampedWeight = Math.Max(1, weight);
        HashSet<Point> changedPoints = [];

        int minX = Width;
        int minY = Height;
        int maxX = -1;
        int maxY = -1;

        foreach (Point point in points)
        {
            if (point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height || Grid[point.Y, point.X] < 0 || WeightGrid[point.Y, point.X] == clampedWeight)
            {
                continue;
            }

            _ = changedPoints.Add(point);
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        if (changedPoints.Count == 0)
        {
            return;
        }

        int x = int.Clamp(minX - 1, 0, Width);
        int y = int.Clamp(minY - 1, 0, Height);
        int w = int.Clamp(maxX + 2, 0, Width);
        int h = int.Clamp(maxY + 2, 0, Height);

        List<Rectangle> removedRectangles = [new Rectangle(x, y, w - x, h - y)];
        HashSet<int> removedLabels = [];

        for (int dy = y; dy < h; dy++)
        {
            for (int dx = x; dx < w; dx++)
            {
                int label = Grid[dy, dx];
                if (label > 0 && removedLabels.Add(label))
                {
                    removedRectangles.Add(RemoveRectangle(label));
                }
            }
        }

        foreach (Point point in changedPoints)
        {
            WeightGrid[point.Y, point.X] = clampedWeight;
        }

        int minStartX = removedRectangles.Min(r => r.X);
        int minStartY = removedRectangles.Min(r => r.Y);
        int maxEndX = removedRectangles.Max(r => r.X + r.Width);
        int maxEndY = removedRectangles.Max(r => r.Y + r.Height);

        FillGrid(minStartX, minStartY, maxEndX, maxEndY);
    }

    public void ResetTileWeights(IEnumerable<Point> points)
    {
        SetTileWeights(points, 1);
    }

    // Helper method to check if a rectangle can still be placed
    private bool IsAreaFree(int x, int y, int w, int h, int requiredWeight)
    {
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                if (Grid[y + dy, x + dx] != 0 || WeightGrid[y + dy, x + dx] != requiredWeight) // Not empty or mismatched weight
                {
                    return false;
                }
            }
        }
        return true;
    }

    private (int, int) GetMaxRectangleSize(int startX, int startY, int limitW, int limitH, int requiredWeight)
    {
        int maxHeight = Math.Min(Height - startY, limitH);
        if (maxHeight <= 0)
        {
            return (0, 0);
        }

        int initialWidth = GetRowContinuousWidth(startX, startY, limitW, requiredWeight);
        if (initialWidth <= 0)
        {
            return (0, 0);
        }

        int currentWidth = initialWidth;
        int bestWidth = 0;
        int bestHeight = 0;
        int bestArea = 0;
        int bestStretch = int.MaxValue;

        for (int h = 1; h <= maxHeight; h++)
        {
            int row = startY + h - 1;
            int rowWidth = GetRowContinuousWidth(startX, row, currentWidth, requiredWeight);
            currentWidth = Math.Min(currentWidth, rowWidth);

            if (currentWidth <= 0)
            {
                break;
            }

            int area = currentWidth * h;
            int stretch = GetStretchFactor(currentWidth, h);

            bool isBetterArea = area > bestArea;
            bool isEqualAreaButBetterShape = area == bestArea && stretch < bestStretch;
            bool isNearBestAreaMuchBetterShape = bestArea > 0 && area * 10 >= bestArea * 9 && stretch + 2 < bestStretch;

            if (isBetterArea || isEqualAreaButBetterShape || isNearBestAreaMuchBetterShape)
            {
                bestArea = area;
                bestWidth = currentWidth;
                bestHeight = h;
                bestStretch = stretch;
            }
        }

        return (bestWidth, bestHeight);
    }

    private void PlaceRectangle(Rectangle rectangle)
    {
        int label = currentLabel++;

        for (int dy = 0; dy < rectangle.Height; dy++)
        {
            for (int dx = 0; dx < rectangle.Width; dx++)
            {
                Grid[rectangle.Y + dy, rectangle.X + dx] = label;
            }
        }

        PlacedRectangles[label] = rectangle;
    }

    private Rectangle RemoveRectangle(int label)
    {
        if (!PlacedRectangles.TryRemove(label, out Rectangle rectangle))
        {
            return Rectangle.Empty;
        }

        for (int dy = 0; dy < rectangle.Height; dy++)
        {
            for (int dx = 0; dx < rectangle.Width; dx++)
            {
                Grid[rectangle.Y + dy, rectangle.X + dx] = 0;
            }
        }

        return rectangle;
    }

    private int GetRowContinuousWidth(int startX, int y, int maxWidth, int requiredWeight)
    {
        int width = 0;
        int maxX = Math.Min(Width, startX + maxWidth);

        for (int x = startX; x < maxX; x++)
        {
            if (Grid[y, x] != 0 || WeightGrid[y, x] != requiredWeight)
            {
                break;
            }

            width++;
        }

        return width;
    }

    private static int GetStretchFactor(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return int.MaxValue;
        }

        int minSide = Math.Min(width, height);
        int maxSide = Math.Max(width, height);
        return maxSide / minSide;
    }

}
