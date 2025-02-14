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
    public ConcurrentDictionary<int, Rectangle> PlacedRectangles { get; }
    public int Width { get; }
    public int Height { get; }

    public GridFiller(int width, int height, List<Rectangle> obstacles)
    {
        Width = width;
        Height = height;
        Grid = new int[height, width];
        PlacedRectangles = [];

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
        ConcurrentBag<(int x, int y, int w, int h)> candidates = [];

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
                        (int w, int h) = GetMaxRectangleSize(x, y, maxRectangleSize, maxRectangleSize);

                        if (w > 0 && h > 0)
                        {
                            candidates.Add((x, y, w, h));
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

            // Sort by area (largest first)
            List<(int x, int y, int w, int h)> sortedCandidates = [.. candidates];
            sortedCandidates.Sort((a, b) => (b.w * b.h).CompareTo(a.w * a.h));

            Debug.WriteLine("Placing rectangles...");

            int placed = 0;
            int reportIntervalPlacing = Math.Max(sortedCandidates.Count / 10, 1);

            // Place rectangles, ensuring no overlap
            foreach ((int x, int y, int w, int h) in sortedCandidates)
            {
                // Check if area is still free
                if (IsAreaFree(x, y, w, h))
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

    // Helper method to check if a rectangle can still be placed
    private bool IsAreaFree(int x, int y, int w, int h)
    {
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                if (Grid[y + dy, x + dx] != 0) // Not empty
                {
                    return false;
                }
            }
        }
        return true;
    }

    private (int, int) GetMaxRectangleSize(int startX, int startY, int limitW, int limitH)
    {
        int maxWidth = 0, maxHeight = 0;

        for (int x = startX; x < Width && maxWidth < limitW && Grid[startY, x] == 0; x++)
        {
            maxWidth++;
        }

        for (int h = 1; h <= Math.Min(Height - startY, limitH); h++)
        {
            for (int x = startX; x < startX + maxWidth; x++)
            {
                if (Grid[startY + h - 1, x] != 0)
                {
                    return (maxWidth, h - 1);
                }
            }
            maxHeight = h;
        }

        return (maxWidth, maxHeight);
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
}