using LargeGridPathfinding;
using Microsoft.Xna.Framework;

public class GridFillerFunctionalTests
{
    public static void Main()
    {
        Console.WriteLine("Running GridFiller Functional Tests...\n");

        TestBasicGridFill();
        TestPartialGridFill();
        TestObstaclePlacement();
        TestObstacleRemoval();
        TestWeightAdjustment();
        TestRectanglePlacement();

        Console.WriteLine("\n✅ All functional tests passed!");
    }

    private static void TestBasicGridFill()
    {
        Console.WriteLine("Test 1: Basic Grid Fill");
        var filler = new GridFiller(100, 100);
        
        filler.FillGrid(fillAll: true);
        
        // Check that grid has been populated with valid labels
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
        Assert(filledCells > 0, "Grid should have filled cells");
        Console.WriteLine("  ✓ Passed\n");
    }

    private static void TestPartialGridFill()
    {
        Console.WriteLine("Test 2: Partial Grid Fill");
        var filler = new GridFiller(100, 100);
        
        filler.FillGrid(0, 0, 50, 50, fillAll: true);
        
        // Check that only the partial region has been filled
        int filledInRegion = 0;
        int filledOutsideRegion = 0;
        
        for (int y = 0; y < 100; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                if (filler.Grid[y, x] > 0)
                {
                    if (x < 50 && y < 50)
                        filledInRegion++;
                    else
                        filledOutsideRegion++;
                }
            }
        }
        
        Console.WriteLine($"  Filled in region (0,0,50,50): {filledInRegion}");
        Console.WriteLine($"  Filled outside region: {filledOutsideRegion}");
        Assert(filledInRegion > 0, "Region should have filled cells");
        Assert(filledOutsideRegion == 0, "Outside region should be empty");
        Console.WriteLine("  ✓ Passed\n");
    }

    private static void TestObstaclePlacement()
    {
        Console.WriteLine("Test 3: Obstacle Placement");
        var filler = new GridFiller(100, 100);
        
        var obstacleRect = new Rectangle(10, 10, 20, 20);
        filler.PlaceObstacle(obstacleRect);
        
        // Check that obstacle has negative labels
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
        Assert(obstaclesPlaced == 400, "Obstacle should cover entire rectangle");
        Console.WriteLine("  ✓ Passed\n");
    }

    private static void TestObstacleRemoval()
    {
        Console.WriteLine("Test 4: Obstacle Removal");
        var filler = new GridFiller(100, 100);
        
        var obstacleRect = new Rectangle(10, 10, 20, 20);
        filler.PlaceObstacle(obstacleRect);
        filler.RemoveObstacle(obstacleRect);
        
        // Check that obstacle has been removed
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
        Assert(obstaclesRemaining == 0, "Obstacle should be completely removed");
        Console.WriteLine("  ✓ Passed\n");
    }

    private static void TestWeightAdjustment()
    {
        Console.WriteLine("Test 5: Weight Adjustment");
        var filler = new GridFiller(100, 100);
        
        var points = new List<Point> { new(10, 10), new(11, 11) };
        filler.SetTileWeights(points, 5);
        
        Assert(filler.WeightGrid[10, 10] == 5, "Weight at (10,10) should be 5");
        Assert(filler.WeightGrid[11, 11] == 5, "Weight at (11,11) should be 5");
        
        filler.ResetTileWeights(points);
        
        Assert(filler.WeightGrid[10, 10] == 1, "Weight should be reset to 1");
        Assert(filler.WeightGrid[11, 11] == 1, "Weight should be reset to 1");
        Console.WriteLine("  ✓ Passed\n");
    }

    private static void TestRectanglePlacement()
    {
        Console.WriteLine("Test 6: Rectangle Placement Tracking");
        var filler = new GridFiller(100, 100);
        
        filler.FillGrid(fillAll: true);
        
        // Check that PlacedRectangles collection is populated
        int placedCount = filler.PlacedRectangles.Count;
        Console.WriteLine($"  Rectangles placed: {placedCount}");
        Assert(placedCount > 0, "Should have placed rectangles");
        
        // Verify each rectangle in the collection matches the grid
        foreach (var kvp in filler.PlacedRectangles)
        {
            int label = kvp.Key;
            var rect = kvp.Value;
            
            // Check a few cells from this rectangle
            for (int y = rect.Top; y < Math.Min(rect.Top + 2, rect.Bottom); y++)
            {
                for (int x = rect.Left; x < Math.Min(rect.Left + 2, rect.Right); x++)
                {
                    Assert(filler.Grid[y, x] == label, $"Grid cell ({x},{y}) should have label {label}");
                }
            }
        }
        
        Console.WriteLine("  ✓ Passed\n");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"Assertion failed: {message}");
        }
    }
}
