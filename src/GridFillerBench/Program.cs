using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using LargeGridPathfinding;
using Microsoft.Xna.Framework;

// Check if we should run tests or benchmarks
if (args.Length > 0 && args[0] == "--test")
{
    RunFunctionalTests();
}
else
{
    BenchmarkRunner.Run<GridFillerBenchmarks>();
}

static void RunFunctionalTests()
{
    Console.WriteLine("Running GridFiller Functional Tests...\n");

    TestBasicGridFill();
    TestObstaclePlacement();
    TestObstacleRemoval();
    TestWeightAdjustment();

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
