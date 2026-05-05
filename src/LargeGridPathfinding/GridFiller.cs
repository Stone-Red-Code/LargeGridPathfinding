using Microsoft.Xna.Framework;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace LargeGridPathfinding;

/// <summary>
/// Maintains grid labels/weights and generates rectangular walkable zones.
/// </summary>
public class GridFiller
{
    private const int RecalculationRadius = 24;
    private readonly object mutationLock = new();
    private int currentLabel = 1;
    private int currentObstacleLabel = -1;
    public int[,] Grid { get; }
    public int[,] WeightGrid { get; }
    public ConcurrentDictionary<int, Rectangle> PlacedRectangles { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Initializes an empty grid with default tile weights.
    /// </summary>
    public GridFiller(int width, int height)
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
    }

    /// <summary>
    /// Fills the selected region with maximal same-weight rectangles.
    /// </summary>
    public void FillGrid(int? x1 = null, int? y1 = null, int? x2 = null, int? y2 = null, bool fillAll = false, IProgress<float>? totalProgress = null, IProgress<float>? calculatingCandidatesProgress = null, IProgress<float>? placingCandidatesProgress = null)
    {
        lock (mutationLock)
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

            ConcurrentBag<(int x, int y, int w, int h, int weight)> candidates = [];

            int cells = Grid.GetLength(0) * Grid.GetLength(1);
            int areaPlaced = 0;
            int maxRectangleSize = Math.Max((Grid.GetLength(0) + Grid.GetLength(1)) / 20, 100);
            int reportInterval = Math.Max(cells / 100, 1);

            do
            {
                candidates.Clear();

                Debug.WriteLine("Calculating candidates...");

                ParallelOptions parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

                _ = Parallel.For(y1.Value, y2.Value, parallelOptions, (y, loopState) =>
                {
                    int x = x1.Value;
                    while (x < x2)
                    {
                        if (Grid[y, x] == 0)
                        {
                            int tileWeight = WeightGrid[y, x];
                            (int w, int h) = GetMaxRectangleSize(x, y, maxRectangleSize, maxRectangleSize, tileWeight);

                            if (w > 0 && h > 0)
                            {
                                candidates.Add((x, y, w, h, tileWeight));

                                // This does technically create some inefficiency by skipping some potential candidates,
                                // but it drastically reduces the number of candidates and thus speeds up the overall process.
                                // Using x += 1 here instead would be the semantically correct way to find all candidates.
                                // The tradeoff is either slower filling with more candidates or faster filling with fewer candidates.
                                // Fewer candidates lead to better pathfinding performance.
                                x += Math.Max(1, Math.Min(w, h));
                            }
                            else
                            {
                                x++;
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
                        else
                        {
                            x++;
                        }
                    }
                });

                Debug.WriteLine($"Candidates: {candidates.Count}");

                if (candidates.Count == 0)
                {
                    break;
                }

                Debug.WriteLine("Sorting candidates...");

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

                foreach ((int x, int y, int w, int h, int weight) in sortedCandidates)
                {
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
    }

    /// <summary>
    /// Places a single obstacle rectangle.
    /// </summary>
    public void PlaceObstacle(Rectangle rectangle)
    {
        PlaceObstacles([rectangle]);
    }

    /// <summary>
    /// Places obstacle rectangles and optionally recalculates affected zones.
    /// </summary>
    public void PlaceObstacles(IEnumerable<Rectangle> rectangles, bool recalculate = true)
    {
        _ = PlaceObstaclesWithAffected(rectangles, recalculate);
    }

    /// <summary>
    /// Places obstacles and returns changed zone labels.
    /// </summary>
    public HashSet<int> PlaceObstaclesWithAffected(IEnumerable<Rectangle> rectangles, bool recalculate = true)
    {
        lock (mutationLock)
        {
            List<Rectangle> clampedRectangles = [.. rectangles
                .Select(ClampToGrid)
                .Where(r => r != Rectangle.Empty)];

            if (clampedRectangles.Count == 0)
            {
                return [];
            }

            int minX = Width;
            int minY = Height;
            int maxX = -1;
            int maxY = -1;
            bool changed = false;

            foreach (Rectangle rectangle in clampedRectangles)
            {
                int endY = rectangle.Bottom;
                for (int dy = rectangle.Top; dy < endY; dy++)
                {
                    for (int dx = rectangle.Left; dx < rectangle.Right; dx++)
                    {
                        if (Grid[dy, dx] >= 0)
                        {
                            changed = true;
                        }
                    }
                }

                minX = Math.Min(minX, rectangle.Left);
                minY = Math.Min(minY, rectangle.Top);
                maxX = Math.Max(maxX, rectangle.Right);
                maxY = Math.Max(maxY, rectangle.Bottom);
            }

            if (changed)
            {
                // Capture zones and rectangles before recalculation
                HashSet<int> zonesBefore = [.. PlacedRectangles.Keys];
                Dictionary<int, Rectangle> rectBefore = new Dictionary<int, Rectangle>(PlacedRectangles);

                RecalculateAroundArea(minX, minY, maxX, maxY, recalculate, () =>
                {
                    foreach (Rectangle rectangle in clampedRectangles)
                    {
                        int obstacle = currentObstacleLabel--;
                        int endY = rectangle.Bottom;

                        for (int dy = rectangle.Top; dy < endY; dy++)
                        {
                            for (int dx = rectangle.Left; dx < rectangle.Right; dx++)
                            {
                                if (Grid[dy, dx] >= 0)
                                {
                                    Grid[dy, dx] = obstacle;
                                    WeightGrid[dy, dx] = 1;
                                }
                            }
                        }
                    }
                });

                // Capture zones after recalculation
                HashSet<int> zonesAfter = [.. PlacedRectangles.Keys];

                Debug.WriteLine("Placed obstacle");

                // Symmetric diff captures both split and merge operations from recalculation.
                HashSet<int> affected = [.. zonesBefore];
                affected.SymmetricExceptWith(zonesAfter);  // zones removed OR added

                // Also include zones whose rectangles changed
                foreach (int zoneId in zonesBefore.Where(zonesAfter.Contains))
                {
                    if (PlacedRectangles[zoneId] != rectBefore[zoneId])
                    {
                        _ = affected.Add(zoneId);
                    }
                }

                return affected;
            }

            return [];
        }
    }

    /// <summary>
    /// Removes a single obstacle rectangle.
    /// </summary>
    public void RemoveObstacle(Rectangle rectangle)
    {
        RemoveObstacles([rectangle]);
    }

    /// <summary>
    /// Removes obstacle rectangles and optionally recalculates affected zones.
    /// </summary>
    public void RemoveObstacles(IEnumerable<Rectangle> rectangles, bool recalculate = true)
    {
        _ = RemoveObstaclesWithAffected(rectangles, recalculate);
    }

    /// <summary>
    /// Removes obstacles and returns changed zone labels.
    /// </summary>
    public HashSet<int> RemoveObstaclesWithAffected(IEnumerable<Rectangle> rectangles, bool recalculate = true)
    {
        lock (mutationLock)
        {
            List<Rectangle> clampedRectangles = [.. rectangles
                .Select(ClampToGrid)
                .Where(r => r != Rectangle.Empty)];

            if (clampedRectangles.Count == 0)
            {
                return [];
            }

            int minX = Width;
            int minY = Height;
            int maxX = -1;
            int maxY = -1;
            bool changed = false;

            foreach (Rectangle rectangle in clampedRectangles)
            {
                int endY = rectangle.Bottom;
                for (int dy = rectangle.Top; dy < endY; dy++)
                {
                    for (int dx = rectangle.Left; dx < rectangle.Right; dx++)
                    {
                        if (Grid[dy, dx] < 0)
                        {
                            changed = true;
                        }
                    }
                }

                minX = Math.Min(minX, rectangle.Left);
                minY = Math.Min(minY, rectangle.Top);
                maxX = Math.Max(maxX, rectangle.Right);
                maxY = Math.Max(maxY, rectangle.Bottom);
            }

            if (changed)
            {
                // Capture zones and rectangles before recalculation
                HashSet<int> zonesBefore = [.. PlacedRectangles.Keys];
                Dictionary<int, Rectangle> rectBefore = new Dictionary<int, Rectangle>(PlacedRectangles);

                RecalculateAroundArea(minX, minY, maxX, maxY, recalculate, () =>
                {
                    foreach (Rectangle rectangle in clampedRectangles)
                    {
                        int endY = rectangle.Bottom;
                        for (int dy = rectangle.Top; dy < endY; dy++)
                        {
                            for (int dx = rectangle.Left; dx < rectangle.Right; dx++)
                            {
                                if (Grid[dy, dx] < 0)
                                {
                                    Grid[dy, dx] = 0;
                                    WeightGrid[dy, dx] = 1;
                                }
                            }
                        }
                    }
                });

                // Capture zones after recalculation
                HashSet<int> zonesAfter = [.. PlacedRectangles.Keys];

                Debug.WriteLine("Removed obstacle");

                // Return zones that changed (added, removed, or had their rectangle modified)
                HashSet<int> affected = [.. zonesBefore];
                affected.SymmetricExceptWith(zonesAfter);

                foreach (int zoneId in zonesBefore.Where(zonesAfter.Contains))
                {
                    if (PlacedRectangles[zoneId] != rectBefore[zoneId])
                    {
                        _ = affected.Add(zoneId);
                    }
                }

                return affected;
            }

            return [];
        }
    }

    /// <summary>
    /// Sets a single tile weight.
    /// </summary>
    public void SetTileWeight(int x, int y, int weight)
    {
        SetTileWeights([new Point(x, y)], weight);
    }

    /// <summary>
    /// Resets a single tile weight to default.
    /// </summary>
    public void ResetTileWeight(int x, int y)
    {
        ResetTileWeights([new Point(x, y)]);
    }

    /// <summary>
    /// Sets weights for tiles and optionally recalculates affected zones.
    /// </summary>
    public void SetTileWeights(IEnumerable<Point> points, int weight, bool recalculate = true)
    {
        _ = SetTileWeightsWithAffected(points, weight, recalculate);
    }

    /// <summary>
    /// Sets weights and returns changed zone labels.
    /// </summary>
    public HashSet<int> SetTileWeightsWithAffected(IEnumerable<Point> points, int weight, bool recalculate = true)
    {
        lock (mutationLock)
        {
            int clampedWeight = Math.Max(1, weight);
            int minX = Width;
            int minY = Height;
            int maxX = -1;
            int maxY = -1;
            bool changed = false;

            foreach (Point point in points)
            {
                if (point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height || Grid[point.Y, point.X] < 0 || WeightGrid[point.Y, point.X] == clampedWeight)
                {
                    continue;
                }

                changed = true;
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X + 1);
                maxY = Math.Max(maxY, point.Y + 1);
            }

            if (changed)
            {
                // Capture zones and rectangles before recalculation
                HashSet<int> zonesBefore = [.. PlacedRectangles.Keys];
                Dictionary<int, Rectangle> rectBefore = new Dictionary<int, Rectangle>(PlacedRectangles);

                RecalculateAroundArea(minX, minY, maxX, maxY, recalculate, () =>
                {
                    foreach (Point point in points)
                    {
                        if (point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height || Grid[point.Y, point.X] < 0)
                        {
                            continue;
                        }

                        WeightGrid[point.Y, point.X] = clampedWeight;
                    }
                });

                // Capture zones after recalculation
                HashSet<int> zonesAfter = [.. PlacedRectangles.Keys];

                // Return zones that changed (added, removed, or had their rectangle modified)
                HashSet<int> affected = [.. zonesBefore];
                affected.SymmetricExceptWith(zonesAfter);

                foreach (int zoneId in zonesBefore.Where(zonesAfter.Contains))
                {
                    if (PlacedRectangles[zoneId] != rectBefore[zoneId])
                    {
                        _ = affected.Add(zoneId);
                    }
                }

                return affected;
            }

            return [];
        }
    }

    public HashSet<int> ResetTileWeightsWithAffected(IEnumerable<Point> points, bool recalculate = true)
    {
        return SetTileWeightsWithAffected(points, 1, recalculate);
    }

    /// <summary>
    /// Resets weights for multiple tiles to default.
    /// </summary>
    public void ResetTileWeights(IEnumerable<Point> points, bool recalculate = true)
    {
        SetTileWeights(points, 1, recalculate);
    }

    private bool IsAreaFree(int x, int y, int w, int h, int requiredWeight)
    {
        int gridRow = y;
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                if (Grid[gridRow, x + dx] != 0 || WeightGrid[gridRow, x + dx] != requiredWeight)
                {
                    return false;
                }
            }
            gridRow++;
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
        int noImprovementCount = 0;
        const int maxNoImprovement = 5;

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
                noImprovementCount = 0;
            }
            else
            {
                noImprovementCount++;
                if (noImprovementCount >= maxNoImprovement)
                {
                    //break;
                }
            }
        }

        return (bestWidth, bestHeight);
    }

    private void PlaceRectangle(Rectangle rectangle)
    {
        int label = currentLabel++;
        int endY = rectangle.Y + rectangle.Height;
        int endX = rectangle.X + rectangle.Width;

        for (int dy = rectangle.Y; dy < endY; dy++)
        {
            for (int dx = rectangle.X; dx < endX; dx++)
            {
                Grid[dy, dx] = label;
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

        int endY = rectangle.Y + rectangle.Height;
        int endX = rectangle.X + rectangle.Width;

        for (int dy = rectangle.Y; dy < endY; dy++)
        {
            for (int dx = rectangle.X; dx < endX; dx++)
            {
                Grid[dy, dx] = 0;
            }
        }

        return rectangle;
    }

    public HashSet<int> GetAffectedZonesAroundArea(int minX, int minY, int maxX, int maxY)
    {
        int left = int.Clamp(minX - RecalculationRadius, 0, Width);
        int top = int.Clamp(minY - RecalculationRadius, 0, Height);
        int right = int.Clamp(maxX + RecalculationRadius, 0, Width);
        int bottom = int.Clamp(maxY + RecalculationRadius, 0, Height);

        HashSet<int> affected = [];
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int label = Grid[y, x];
                if (label > 0)
                {
                    _ = affected.Add(label);
                }
            }
        }

        return affected;
    }

    private void RecalculateAroundArea(int minX, int minY, int maxX, int maxY, bool enabled, Action applyChanges)
    {
        int left = int.Clamp(minX - RecalculationRadius, 0, Width);
        int top = int.Clamp(minY - RecalculationRadius, 0, Height);
        int right = int.Clamp(maxX + RecalculationRadius, 0, Width);
        int bottom = int.Clamp(maxY + RecalculationRadius, 0, Height);

        if (left >= right || top >= bottom || !enabled)
        {
            applyChanges();
            return;
        }

        HashSet<int> labelsToRemove = [];
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int label = Grid[y, x];
                if (label > 0)
                {
                    _ = labelsToRemove.Add(label);
                }
            }
        }

        List<Rectangle> removedRectangles = [];
        foreach (int label in labelsToRemove)
        {
            Rectangle removed = RemoveRectangle(label);
            if (removed != Rectangle.Empty)
            {
                removedRectangles.Add(removed);
            }
        }

        if (removedRectangles.Count == 0)
        {
            applyChanges();
            // Even if no existing zone was removed, edits may have opened space that needs fresh zoning.
            FillGrid(left, top, right, bottom);
            return;
        }

        int fillLeft = Math.Min(left, removedRectangles.Min(r => r.Left));
        int fillTop = Math.Min(top, removedRectangles.Min(r => r.Top));
        int fillRight = Math.Max(right, removedRectangles.Max(r => r.Right));
        int fillBottom = Math.Max(bottom, removedRectangles.Max(r => r.Bottom));

        // Expanding fill bounds to include removed rectangle extents avoids edge artifacts
        // where old zone boundaries would otherwise leave suboptimal splits near the edit area.
        applyChanges();
        FillGrid(fillLeft, fillTop, fillRight, fillBottom);
    }

    private Rectangle ClampToGrid(Rectangle rectangle)
    {
        int left = int.Clamp(rectangle.Left, 0, Width);
        int top = int.Clamp(rectangle.Top, 0, Height);
        int right = int.Clamp(rectangle.Right, 0, Width);
        int bottom = int.Clamp(rectangle.Bottom, 0, Height);

        if (right <= left || bottom <= top)
        {
            return Rectangle.Empty;
        }

        return new Rectangle(left, top, right - left, bottom - top);
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

    // Public methods for benchmarking
    public int GetRowContinuousWidthPublic(int startX, int y, int maxWidth, int requiredWeight)
    {
        return GetRowContinuousWidth(startX, y, maxWidth, requiredWeight);
    }

    public bool IsAreaFreePublic(int x, int y, int w, int h, int requiredWeight)
    {
        return IsAreaFree(x, y, w, h, requiredWeight);
    }

    public int GetStretchFactorPublic(int width, int height)
    {
        return GetStretchFactor(width, height);
    }
}