using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

using MonoGame.Extended;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using MonoGame.Extended.Input;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

using LargeGridPathfinding.Services;

namespace LargeGridPathfinding.Systems;

public class GridEditSystem : IUpdateSystem
{
    public enum BrushMode
    {
        Weight,
        Obstacle
    }

    private readonly GridService gridService;
    private readonly OrthographicCamera camera;
    private readonly ConcurrentDictionary<Vector2, Color> temporaryIndicators;
    private readonly ProgressTracker progressTracker;
    private readonly Func<bool> isInputBlocked;
    private readonly Action<bool> setInputBlocked;
    private readonly Func<bool> getShowZones;
    private readonly Action<bool> setShowZones;
    private readonly Func<bool> getShowGrid;
    private readonly Action<bool> setShowGrid;
    private readonly Func<bool> getShowPaths;
    private readonly Action<bool> setShowPaths;

    private BrushMode brushMode = BrushMode.Obstacle;
    private int paintWeight = 5;
    private Vector2? previousMousePosition;
    private bool wasShiftLeftDown;
    private bool wasShiftRightDown;
    private Point? fillSelectionStart;
    private Point? fillSelectionCurrent;

    public BrushMode CurrentBrushMode => brushMode;
    public int PaintWeight => paintWeight;
    public Point? FillSelectionStart => fillSelectionStart;
    public Point? FillSelectionCurrent => fillSelectionCurrent;

    public GridEditSystem(
        GridService gridService,
        OrthographicCamera camera,
        ConcurrentDictionary<Vector2, Color> temporaryIndicators,
        ProgressTracker progressTracker,
        Func<bool> isInputBlocked,
        Action<bool> setInputBlocked,
        Func<bool> getShowZones,
        Action<bool> setShowZones,
        Func<bool> getShowGrid,
        Action<bool> setShowGrid,
        Func<bool> getShowPaths,
        Action<bool> setShowPaths)
    {
        this.gridService = gridService;
        this.camera = camera;
        this.temporaryIndicators = temporaryIndicators;
        this.progressTracker = progressTracker;
        this.isInputBlocked = isInputBlocked;
        this.setInputBlocked = setInputBlocked;
        this.getShowZones = getShowZones;
        this.setShowZones = setShowZones;
        this.getShowGrid = getShowGrid;
        this.setShowGrid = setShowGrid;
        this.getShowPaths = getShowPaths;
        this.setShowPaths = setShowPaths;
    }

    public void Initialize(World world) { }

    public void Dispose() { }

    public void Update(GameTime gameTime)
    {
        MouseStateExtended mouseState = MouseExtended.GetState();
        KeyboardStateExtended keyboardState = KeyboardExtended.GetState();
        bool shiftPressed = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);
        bool shiftLeftDown = shiftPressed && mouseState.IsButtonDown(MouseButton.Left);
        bool shiftLeftClicked = shiftLeftDown && !wasShiftLeftDown;
        bool shiftRightDown = shiftPressed && mouseState.IsButtonDown(MouseButton.Right);
        bool shiftRightClicked = shiftRightDown && !wasShiftRightDown;
        wasShiftLeftDown = shiftLeftDown;
        wasShiftRightDown = shiftRightDown;

        if (isInputBlocked())
        {
            return;
        }

        if (keyboardState.WasKeyPressed(Keys.Z))
        {
            setShowZones(!getShowZones());
        }

        if (keyboardState.WasKeyPressed(Keys.G))
        {
            setShowGrid(!getShowGrid());
        }

        if (keyboardState.WasKeyPressed(Keys.P))
        {
            setShowPaths(!getShowPaths());
        }

        if (keyboardState.WasKeyPressed(Keys.D1) || keyboardState.WasKeyPressed(Keys.NumPad1))
        {
            paintWeight = 1;
        }
        else if (keyboardState.WasKeyPressed(Keys.D2) || keyboardState.WasKeyPressed(Keys.NumPad2))
        {
            paintWeight = 2;
        }
        else if (keyboardState.WasKeyPressed(Keys.D3) || keyboardState.WasKeyPressed(Keys.NumPad3))
        {
            paintWeight = 3;
        }
        else if (keyboardState.WasKeyPressed(Keys.D4) || keyboardState.WasKeyPressed(Keys.NumPad4))
        {
            paintWeight = 4;
        }
        else if (keyboardState.WasKeyPressed(Keys.D5) || keyboardState.WasKeyPressed(Keys.NumPad5))
        {
            paintWeight = 5;
        }
        else if (keyboardState.WasKeyPressed(Keys.D6) || keyboardState.WasKeyPressed(Keys.NumPad6))
        {
            paintWeight = 6;
        }
        else if (keyboardState.WasKeyPressed(Keys.D7) || keyboardState.WasKeyPressed(Keys.NumPad7))
        {
            paintWeight = 7;
        }
        else if (keyboardState.WasKeyPressed(Keys.D8) || keyboardState.WasKeyPressed(Keys.NumPad8))
        {
            paintWeight = 8;
        }
        else if (keyboardState.WasKeyPressed(Keys.D9) || keyboardState.WasKeyPressed(Keys.NumPad9))
        {
            paintWeight = 9;
        }

        if (keyboardState.WasKeyPressed(Keys.O))
        {
            brushMode = brushMode == BrushMode.Weight ? BrushMode.Obstacle : BrushMode.Weight;
        }

        if (keyboardState.WasKeyPressed(Keys.F))
        {
            Debug.WriteLine("Filling grid...");

            _ = progressTracker.AddProgress("Filling grid", out IProgress<float> fillingGridProgressReporter);
            _ = progressTracker.AddProgress("Calculating candidates", out IProgress<float> calculatingCandidatesProgressReporter);
            _ = progressTracker.AddProgress("Placing zones", out IProgress<float> placingCandidatesProgressReporter);

            bool showZonesBefore = getShowZones();
            bool showGridBefore = getShowGrid();
            bool showPathsBefore = getShowPaths();
            setShowZones(true);
            setShowGrid(false);
            setShowPaths(false);
            setInputBlocked(true);

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                gridService.Filler.FillGrid(fillAll: true, totalProgress: fillingGridProgressReporter, calculatingCandidatesProgress: calculatingCandidatesProgressReporter, placingCandidatesProgress: placingCandidatesProgressReporter);
                gridService.GridChanged = true;
                setInputBlocked(false);
                setShowZones(showZonesBefore);
                setShowGrid(showGridBefore);
                setShowPaths(showPathsBefore);
            });
        }

        if (shiftPressed)
        {
            if (fillSelectionStart.HasValue && TryGetMouseGridPoint(mouseState, out Point hoverGridPoint))
            {
                fillSelectionCurrent = hoverGridPoint;
            }
            else
            {
                fillSelectionCurrent = null;
            }

            if ((shiftLeftClicked || shiftRightClicked) && TryGetMouseGridPoint(mouseState, out Point clickedGridPoint))
            {
                if (!fillSelectionStart.HasValue)
                {
                    fillSelectionStart = clickedGridPoint;
                    fillSelectionCurrent = clickedGridPoint;
                }
                else
                {
                    List<Point> selectionPoints = GetRectanglePoints(fillSelectionStart.Value, clickedGridPoint);
                    Color indicatorColor = brushMode == BrushMode.Weight ? Color.Orange : Color.Red;

                    foreach (Point point in selectionPoints)
                    {
                        temporaryIndicators[point.ToVector2()] = indicatorColor;
                    }

                    GridService.PendingOperation operation;
                    if (brushMode == BrushMode.Weight && shiftLeftClicked)
                    {
                        operation = new GridService.PendingOperation(GridService.PendingOperationKind.SetWeight, paintWeight);
                    }
                    else if (brushMode == BrushMode.Weight && shiftRightClicked)
                    {
                        operation = new GridService.PendingOperation(GridService.PendingOperationKind.ResetWeight, 0);
                    }
                    else if (brushMode == BrushMode.Obstacle && shiftLeftClicked)
                    {
                        operation = new GridService.PendingOperation(GridService.PendingOperationKind.PlaceObstacle, 0);
                    }
                    else if (brushMode == BrushMode.Obstacle && shiftRightClicked)
                    {
                        operation = new GridService.PendingOperation(GridService.PendingOperationKind.RemoveObstacle, 0);
                    }
                    else
                    {
                        throw new InvalidOperationException("Invalid brush mode or mouse button state.");
                    }

                    gridService.EnqueueOperations(selectionPoints, operation);
                    fillSelectionStart = null;
                    fillSelectionCurrent = null;
                }
            }

            previousMousePosition = null;
            return;
        }
        else
        {
            fillSelectionStart = null;
            fillSelectionCurrent = null;
        }

        if (mouseState.IsButtonDown(MouseButton.Left))
        {
            Vector2 mousePosition = camera.ScreenToWorld(mouseState.Position.ToVector2());
            Vector2 gridPosition = new((int)mousePosition.X / 10, (int)mousePosition.Y / 10);

            if (gridPosition.X < 0 || gridPosition.X >= gridService.Width || gridPosition.Y < 0 || gridPosition.Y >= gridService.Height || temporaryIndicators.ContainsKey(gridPosition))
            {
                return;
            }

            int cellValue = gridService.Grid[(int)gridPosition.Y, (int)gridPosition.X];
            if ((brushMode == BrushMode.Weight && cellValue < 0) || (brushMode == BrushMode.Obstacle && cellValue < 0))
            {
                return;
            }

            Vector2? previousGridPosition = null;
            if (previousMousePosition is not null)
            {
                previousGridPosition = new Vector2((int)previousMousePosition.Value.X / 10, (int)previousMousePosition.Value.Y / 10);
            }

            Color indicatorColor = brushMode == BrushMode.Weight ? Color.Orange : Color.Red;
            BrushMode currentBrushMode = brushMode;
            int currentPaintWeight = paintWeight;
            previousMousePosition = mousePosition;

            List<Point> brushPoints = GetBrushPoints(gridPosition, previousGridPosition);
            foreach (Point point in brushPoints)
            {
                temporaryIndicators[point.ToVector2()] = indicatorColor;
            }

            GridService.PendingOperation pendingOperation = currentBrushMode == BrushMode.Weight
                ? new GridService.PendingOperation(GridService.PendingOperationKind.SetWeight, currentPaintWeight)
                : new GridService.PendingOperation(GridService.PendingOperationKind.PlaceObstacle, 0);

            gridService.EnqueueOperations(brushPoints, pendingOperation);
        }
        else if (mouseState.IsButtonDown(MouseButton.Right))
        {
            Vector2 mousePosition = camera.ScreenToWorld(mouseState.Position.ToVector2());
            Vector2 gridPosition = new((int)mousePosition.X / 10, (int)mousePosition.Y / 10);

            if (gridPosition.X < 0 || gridPosition.X >= gridService.Width || gridPosition.Y < 0 || gridPosition.Y >= gridService.Height || temporaryIndicators.ContainsKey(gridPosition))
            {
                return;
            }

            int cellValue = gridService.Grid[(int)gridPosition.Y, (int)gridPosition.X];
            if ((brushMode == BrushMode.Weight && cellValue < 0) || (brushMode == BrushMode.Obstacle && cellValue >= 0))
            {
                return;
            }

            Vector2? previousGridPosition = null;
            if (previousMousePosition is not null)
            {
                previousGridPosition = new Vector2((int)previousMousePosition.Value.X / 10, (int)previousMousePosition.Value.Y / 10);
            }

            Color indicatorColor = brushMode == BrushMode.Weight ? Color.LightGray : Color.Yellow;
            BrushMode currentBrushMode = brushMode;
            previousMousePosition = mousePosition;

            List<Point> brushPoints = GetBrushPoints(gridPosition, previousGridPosition);
            foreach (Point point in brushPoints)
            {
                temporaryIndicators[point.ToVector2()] = indicatorColor;
            }

            GridService.PendingOperation pendingOperation = currentBrushMode == BrushMode.Weight
                ? new GridService.PendingOperation(GridService.PendingOperationKind.ResetWeight, 0)
                : new GridService.PendingOperation(GridService.PendingOperationKind.RemoveObstacle, 0);

            gridService.EnqueueOperations(brushPoints, pendingOperation);
        }
        else
        {
            previousMousePosition = null;
        }
    }

    private bool TryGetMouseGridPoint(MouseStateExtended mouseState, out Point gridPoint)
    {
        Vector2 mousePosition = camera.ScreenToWorld(mouseState.Position.ToVector2());
        Point point = new((int)mousePosition.X / 10, (int)mousePosition.Y / 10);

        if (point.X < 0 || point.X >= gridService.Width || point.Y < 0 || point.Y >= gridService.Height)
        {
            gridPoint = default;
            return false;
        }

        gridPoint = point;
        return true;
    }

    private static List<Point> GetRectanglePoints(Point start, Point end)
    {
        int minX = Math.Min(start.X, end.X);
        int minY = Math.Min(start.Y, end.Y);
        int maxX = Math.Max(start.X, end.X);
        int maxY = Math.Max(start.Y, end.Y);

        List<Point> points = new((maxX - minX + 1) * (maxY - minY + 1));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                points.Add(new Point(x, y));
            }
        }

        return points;
    }

    private static List<Point> GetBrushPoints(Vector2 currentGridPosition, Vector2? previousGridPosition)
    {
        HashSet<Point> points = [];
        Point currentPoint = new((int)currentGridPosition.X, (int)currentGridPosition.Y);
        _ = points.Add(currentPoint);

        if (previousGridPosition is null)
        {
            return [.. points];
        }

        float distance = Vector2.Distance(previousGridPosition.Value, currentGridPosition);
        int steps = Math.Max(1, (int)Math.Ceiling(distance * 10));

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 interpolatedPosition = Vector2.Lerp(previousGridPosition.Value, currentGridPosition, t);
            Point interpolatedPoint = new((int)Math.Round(interpolatedPosition.X), (int)Math.Round(interpolatedPosition.Y));
            _ = points.Add(interpolatedPoint);
        }

        return [.. points];
    }
}
