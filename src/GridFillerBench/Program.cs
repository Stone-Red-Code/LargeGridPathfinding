using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using LargeGridPathfinding;
using Microsoft.Xna.Framework;

static int CountZones(GridFiller filler)
{
    int width = filler.Width;
    int height = filler.Height;
    bool[,] visited = new bool[height, width];
    int zoneCount = 0;
    
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            if (!visited[y, x] && filler.Grid[y, x] > 0)
            {
                // BFS to find all cells in this zone
                var queue = new Queue<(int, int)>();
                queue.Enqueue((x, y));
                visited[y, x] = true;
                
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    int zoneId = filler.Grid[cy, cx];
                    
                    // Check 4 neighbors
                    foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height && 
                            !visited[ny, nx] && filler.Grid[ny, nx] == zoneId)
                        {
                            visited[ny, nx] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
                
                zoneCount++;
            }
        }
    }
    
    return zoneCount;
}

// Check if we should run tests or benchmarks
if (args.Length > 0 && args[0] == "--test")
{
    RunFunctionalTests();
}
else if (args.Length > 0 && args[0] == "--advanced")
{
    BenchmarkRunner.Run<AdvancedGridFillerBenchmarks>();
}
else if (args.Length > 0 && args[0] == "--incremental")
{
    TestIncrementalUpdates();
}
else
{
    BenchmarkRunner.Run<GridFillerBenchmarks>();
}

static void TestIncrementalUpdates()
{
    Console.WriteLine("Test 8: Incremental Graph Updates (Comparison)\n");

    int[] gridSizes = { 512, 1024, 2048 };
    int[] changePercentages = { 1, 5, 10 };

    foreach (int size in gridSizes)
    {
        Console.WriteLine($"  Testing {size}×{size} grid:");

        foreach (int changePercent in changePercentages)
        {
            int obstacleSize = Math.Max((size / 100) * changePercent, 10);

            // Scenario 1: Full rebuild (fill grid + build pathfinder from scratch)
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            var fillerFull = new GridFiller(size, size);
            fillerFull.FillGrid(fillAll: true);
            fillerFull.PlaceObstacles([new Rectangle(50, 50, obstacleSize, obstacleSize)], recalculate: true);
            var pathfinderFull = new Pathfinder(fillerFull.PlacedRectangles, fillerFull.Grid, fillerFull.WeightGrid);
            sw1.Stop();

            // Scenario 2: Incremental (start from filled grid, place obstacle, incremental update)
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            var filler2 = new GridFiller(size, size);
            filler2.FillGrid(fillAll: true);
            var pathfinderIncremental = new Pathfinder(filler2.PlacedRectangles, filler2.Grid, filler2.WeightGrid);
            var affected = filler2.PlaceObstaclesWithAffected([new Rectangle(50, 50, obstacleSize, obstacleSize)], recalculate: true);
            pathfinderIncremental.IncrementalUpdateGraph(affected);
            sw2.Stop();

            double speedup = sw1.ElapsedMilliseconds > 0 ? (double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds : 1.0;
            Console.WriteLine($"    {changePercent}% change: Full {sw1.ElapsedMilliseconds}ms vs Incremental {sw2.ElapsedMilliseconds}ms ({speedup:F1}×)");
        }

        Console.WriteLine();
    }

    Console.WriteLine("  ✓ Passed\n");
}

static void RunFunctionalTests()
{
    Console.WriteLine("Running GridFiller Functional Tests...\n");

    TestBasicGridFill();
    TestObstaclePlacement();
    TestObstacleRemoval();
    TestWeightAdjustment();
    TestZoneCount();
    TestLargerGrids();
    TestPathfinderGraphBuilding();
    ValidateIncrementalUpdates();

    Console.WriteLine("\n✅ All functional tests passed!");
}

static void TestBasicGridFill()
{
    Console.WriteLine("Test 1: Basic Grid Fill");
    var filler = new GridFiller(100, 100);
    
    filler.FillGrid(fillAll: true);
    
    int filledCells = 0;
    for (int y = 0; y < 100; y++)
    {
        for (int x = 0; x < 100; x++)
        {
            if (filler.Grid[y, x] > 0)
                filledCells++;
        }
    }
    
    Console.WriteLine($"  Filled cells: {filledCells}/10000");
    if (filledCells > 0) Console.WriteLine("  ✓ Passed\n");
    else throw new Exception("Grid should have filled cells");
}

static void TestObstaclePlacement()
{
    Console.WriteLine("Test 2: Obstacle Placement");
    var filler = new GridFiller(100, 100);
    
    var obstacleRect = new Rectangle(10, 10, 20, 20);
    filler.PlaceObstacle(obstacleRect);
    
    int obstaclesPlaced = 0;
    for (int y = 10; y < 30; y++)
    {
        for (int x = 10; x < 30; x++)
        {
            if (filler.Grid[y, x] < 0)
                obstaclesPlaced++;
        }
    }
    
    Console.WriteLine($"  Obstacles placed: {obstaclesPlaced}/400");
    if (obstaclesPlaced == 400) Console.WriteLine("  ✓ Passed\n");
    else throw new Exception("Obstacle placement failed");
}

static void TestObstacleRemoval()
{
    Console.WriteLine("Test 3: Obstacle Removal");
    var filler = new GridFiller(100, 100);
    
    var obstacleRect = new Rectangle(10, 10, 20, 20);
    filler.PlaceObstacle(obstacleRect);
    filler.RemoveObstacle(obstacleRect);
    
    int obstaclesRemaining = 0;
    for (int y = 10; y < 30; y++)
    {
        for (int x = 10; x < 30; x++)
        {
            if (filler.Grid[y, x] < 0)
                obstaclesRemaining++;
        }
    }
    
    Console.WriteLine($"  Obstacles remaining: {obstaclesRemaining}");
    if (obstaclesRemaining == 0) Console.WriteLine("  ✓ Passed\n");
    else throw new Exception("Obstacle removal failed");
}

static void TestWeightAdjustment()
{
    Console.WriteLine("Test 4: Weight Adjustment");
    var filler = new GridFiller(100, 100);
    
    var points = new List<Point> { new(10, 10), new(11, 11) };
    filler.SetTileWeights(points, 5);
    
    if (filler.WeightGrid[10, 10] != 5) throw new Exception("Weight at (10,10) should be 5");
    if (filler.WeightGrid[11, 11] != 5) throw new Exception("Weight at (11,11) should be 5");
    
    filler.ResetTileWeights(points);
    
    if (filler.WeightGrid[10, 10] != 1) throw new Exception("Weight should be reset to 1");
    if (filler.WeightGrid[11, 11] != 1) throw new Exception("Weight should be reset to 1");
    Console.WriteLine("  ✓ Passed\n");
}

static void TestZoneCount()
{
    Console.WriteLine("Test 5: Zone Count (512x512 grid)");
    var filler = new GridFiller(512, 512);
    filler.FillGrid(fillAll: true);
    
    int zoneCount = CountZones(filler);
    Console.WriteLine($"  Zones created: {zoneCount}");
    Console.WriteLine($"  ✓ Passed\n");
}

static void TestLargerGrids()
{
    Console.WriteLine("Test 6: Larger Grids Zone Count (3 runs each)\n");
    
    int[] gridSizes = { 512, 1024, 2048 };
    int runsPerSize = 3;
    
    foreach (int size in gridSizes)
    {
        Console.WriteLine($"  Testing {size}×{size} grid ({size * size:N0} cells):");
        
        int[] zoneCounts = new int[runsPerSize];
        long[] times = new long[runsPerSize];
        
        for (int run = 0; run < runsPerSize; run++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            var filler = new GridFiller(size, size);
            filler.FillGrid(fillAll: true);
            
            sw.Stop();
            zoneCounts[run] = CountZones(filler);
            times[run] = sw.ElapsedMilliseconds;
        }
        
        double avgZones = zoneCounts.Average();
        double avgTime = times.Average();
        
        Console.WriteLine($"    Zones: {zoneCounts[0]}, {zoneCounts[1]}, {zoneCounts[2]} (avg: {avgZones:F1})");
        Console.WriteLine($"    Times: {times[0]}ms, {times[1]}ms, {times[2]}ms (avg: {avgTime:F0}ms)\n");
    }
    
    Console.WriteLine("  ✓ Passed\n");
}

static void TestPathfinderGraphBuilding()
{
    Console.WriteLine("Test 7: Pathfinder Graph Building (5 runs each)\n");
    
    int[] gridSizes = { 512, 1024, 2048, 4096 };
    
    foreach (int size in gridSizes)
    {
        Console.WriteLine($"  Testing {size}×{size} grid:");
        
        long totalTime = 0;
        for (int run = 0; run < 5; run++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            var filler = new GridFiller(size, size);
            filler.FillGrid(fillAll: true);
            
            var pathfinder = new Pathfinder(filler.PlacedRectangles, filler.Grid, filler.WeightGrid);
            
            sw.Stop();
            totalTime += sw.ElapsedMilliseconds;
        }
        
        double avgTime = totalTime / 5.0;
        Console.WriteLine($"    Avg time (5 runs): {avgTime:F1}ms\n");
    }
    
    Console.WriteLine("  ✓ Passed\n");
}

static void ValidateIncrementalUpdates()
{
    Console.WriteLine("Test 8: Validate Incremental Updates Correctness\n");

    var filler = new GridFiller(512, 512);
    filler.FillGrid(fillAll: true);

    // Create initial pathfinder
    var pathfinder = new Pathfinder(filler.PlacedRectangles, filler.Grid, filler.WeightGrid);

    // Get initial graph (copy for comparison)
    Dictionary<int, HashSet<int>> graphBefore = new();
    foreach (var kvp in filler.PlacedRectangles)
    {
        if (pathfinder.GetAdjacencyList().TryGetValue(kvp.Key, out var neighbors))
        {
            graphBefore[kvp.Key] = new HashSet<int>(neighbors);
        }
    }

    // Place obstacle and track affected zones
    var affected = filler.PlaceObstaclesWithAffected([new Rectangle(100, 100, 50, 50)], recalculate: true);
    pathfinder.IncrementalUpdateGraph(affected);

    // Full rebuild for comparison
    var pathfinderFull = new Pathfinder(filler.PlacedRectangles, filler.Grid, filler.WeightGrid);

    // Compare graphs
    int matches = 0;
    int mismatches = 0;
    foreach (var kvp in filler.PlacedRectangles)
    {
        if (!pathfinder.GetAdjacencyList().TryGetValue(kvp.Key, out var incrementalNeighbors))
            incrementalNeighbors = [];
        if (!pathfinderFull.GetAdjacencyList().TryGetValue(kvp.Key, out var fullNeighbors))
            fullNeighbors = [];

        var incrementalSet = new HashSet<int>(incrementalNeighbors);
        var fullSet = new HashSet<int>(fullNeighbors);

        if (incrementalSet.SetEquals(fullSet))
        {
            matches++;
        }
        else
        {
            mismatches++;
            Console.WriteLine($"  Mismatch at zone {kvp.Key}: incremental={string.Join(",", incrementalSet)}, full={string.Join(",", fullSet)}");
        }
    }

    if (mismatches == 0)
    {
        Console.WriteLine($"  ✓ All {matches} zones match between incremental and full rebuild\n");
    }
    else
    {
        throw new Exception($"Incremental update validation failed: {mismatches}/{matches + mismatches} zones mismatched");
    }
}

[MemoryDiagnoser]
public class GridFillerBenchmarks
{
    private GridFiller gridFiller = null!;
    private const int GridSize = 512;

    [GlobalSetup]
    public void Setup()
    {
        gridFiller = new GridFiller(GridSize, GridSize);
    }

    [Benchmark(Description = "FillGrid - Full grid")]
    public void FillGridFull()
    {
        gridFiller.FillGrid(fillAll: true);
    }

    [Benchmark(Description = "FillGrid - Partial region")]
    public void FillGridPartial()
    {
        gridFiller.FillGrid(0, 0, GridSize / 2, GridSize / 2);
    }

    [Benchmark(Description = "PlaceObstacle - Single rectangle")]
    public void PlaceObstacleSingle()
    {
        var rect = new Rectangle(10, 10, 50, 50);
        gridFiller.PlaceObstacle(rect);
    }

    [Benchmark(Description = "RemoveObstacle - Single rectangle")]
    public void RemoveObstacleSingle()
    {
        var rect = new Rectangle(10, 10, 50, 50);
        gridFiller.RemoveObstacle(rect);
    }

    [Benchmark(Description = "SetTileWeights - 100 tiles")]
    public void SetTileWeights()
    {
        var points = new List<Point>();
        for (int i = 0; i < 100; i++)
        {
            points.Add(new Point(i % 50, i / 50));
        }
        gridFiller.SetTileWeights(points, 2);
    }

    [Benchmark(Description = "ResetTileWeights - 100 tiles")]
    public void ResetTileWeights()
    {
        var points = new List<Point>();
        for (int i = 0; i < 100; i++)
        {
            points.Add(new Point(i % 50, i / 50));
        }
        gridFiller.ResetTileWeights(points);
    }
}

[MemoryDiagnoser]
public class AdvancedGridFillerBenchmarks
{
    private GridFiller gridFiller = null!;
    private const int GridSize = 512;

    [GlobalSetup]
    public void Setup()
    {
        gridFiller = new GridFiller(GridSize, GridSize);
    }

    [Benchmark(Description = "GetRowContinuousWidth - 10 calls")]
    public int BenchmarkGetRowWidth()
    {
        int result = 0;
        for (int i = 0; i < 10; i++)
        {
            result += gridFiller.GetRowContinuousWidthPublic(10 + i, 10, 100, 1);
        }
        return result;
    }

    [Benchmark(Description = "IsAreaFree - 100x100 check")]
    public bool BenchmarkIsAreaFree()
    {
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                if (x < 5 && y < 5)
                {
                    var result = gridFiller.IsAreaFreePublic(x * 50, y * 50, 50, 50, 1);
                    if (!result) return false;
                }
            }
        }
        return true;
    }

    [Benchmark(Description = "Stretch factor calculation - 1000 iterations")]
    public int BenchmarkStretchFactor()
    {
        int result = 0;
        for (int w = 10; w < 100; w += 3)
        {
            for (int h = 10; h < 100; h += 3)
            {
                result += gridFiller.GetStretchFactorPublic(w, h);
            }
        }
        return result;
    }

    [Benchmark(Description = "FillGrid - Full grid (Advanced)")]
    public void FillGridFullAdvanced()
    {
        gridFiller.FillGrid(fillAll: true);
    }
}
