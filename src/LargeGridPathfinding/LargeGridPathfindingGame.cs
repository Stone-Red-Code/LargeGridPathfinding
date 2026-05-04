using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MonoGame.Extended;
using MonoGame.Extended.Input;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LargeGridPathfinding;

public class LargeGridPathfindingGame : Game
{
    private enum BrushMode
    {
        Weight,
        Obstacle
    }

    private enum PendingOperationKind
    {
        SetWeight,
        ResetWeight,
        PlaceObstacle,
        RemoveObstacle
    }

    private readonly record struct PendingOperation(PendingOperationKind Kind, int Weight);

    private readonly ConcurrentDictionary<Vector2, Color> temporaryIndicators = [];
    private readonly Dictionary<Point, PendingOperation> pendingOperations = [];
    private readonly object pendingOperationsLock = new();
    private readonly ProgressTracker progressTracker = new ProgressTracker();
    private readonly List<Agent> agents = [];
    private SpriteBatch spriteBatch = null!;
    private SpriteBatch uiSpriteBatch = null!;
    private SpriteFont uiFont = null!;
    private OrthographicCamera camera = null!;
    private GridFiller gridFiller = null!;
    private Pathfinder pathfinder = null!;
    private bool gridChanged;
    private bool inputBlocked = true;
    private bool showZones = true;
    private bool showGrid = false;
    private bool showPaths = false;
    private BrushMode brushMode = BrushMode.Weight;
    private int paintWeight = 5;
    private Vector2? previousMousePosition;
    private bool batchProcessingScheduled;

    public LargeGridPathfindingGame()
    {
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1920,
            PreferredBackBufferHeight = 1080
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        Window.Title = "Large Grid Pathfinding";
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        camera = new OrthographicCamera(GraphicsDevice)
        {
            MinimumZoom = 0.1f,
            MaximumZoom = 2,
            Zoom = 0.5f,
        };

        StartInitializationTask();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        uiSpriteBatch = new SpriteBatch(GraphicsDevice);
        uiFont = Content.Load<SpriteFont>("ManoloMono");
    }

    protected override void Update(GameTime gameTime)
    {
        if (!IsActive)
        {
            return;
        }

        if (pathfinder is not null)
        {
            // Update agent positions
            foreach (Agent agent in agents)
            {
                if (agent.Path is null || agent.Path.Count < 2)
                {
                    continue;
                }

                if (Vector2.Distance(agent.Position, agent.NextPosition) < 0.1)
                {
                    if (agent.NextPosition == agent.Path[^1])
                    {
                        agent.Path = null;
                        agent.Destination = null;
                    }
                    else
                    {
                        agent.NextPosition = agent.Path[agent.Path.LastIndexOf(agent.NextPosition) + 1];
                    }
                }
                else
                {
                    float distance = Vector2.Distance(agent.Position, agent.NextPosition);
                    float lerpFactor = Math.Min(10 / distance * gameTime.GetElapsedSeconds(), 1);

                    Vector2 nextPosition = Vector2.Lerp(agent.Position, agent.NextPosition, lerpFactor);

                    int newX = (int)Math.Round(nextPosition.X);
                    int newY = (int)Math.Round(nextPosition.Y);

                    int[,] grid = gridFiller.Grid;

                    if (grid[newY, newX] < 0)
                    {
                        agent.Path = null;
                        continue;
                    }

                    agent.Position = nextPosition;
                }
            }
        }

        MouseExtended.Update();
        KeyboardExtended.Update();

        MouseStateExtended mouseState = MouseExtended.GetState();
        KeyboardStateExtended keyboardState = KeyboardExtended.GetState();

        float movementSpeed = (float)Math.Pow(200, 2 - camera.Zoom);

        movementSpeed = Math.Clamp(movementSpeed, 1000, 20000);

        camera.Move(keyboardState.GetMovementDirection() * movementSpeed * gameTime.GetElapsedSeconds());

        if (mouseState.DeltaScrollWheelValue < 0)
        {
            camera.ZoomIn(0.1f);
        }
        else if (mouseState.DeltaScrollWheelValue > 0)
        {
            camera.ZoomOut(0.1f);
        }

        if (inputBlocked)
        {
            return;
        }

        // Toggle debug options

        if (keyboardState.WasKeyPressed(Keys.Z))
        {
            showZones = !showZones;
        }

        if (keyboardState.WasKeyPressed(Keys.G))
        {
            showGrid = !showGrid;
        }

        if (keyboardState.WasKeyPressed(Keys.P))
        {
            showPaths = !showPaths;
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

            bool showZonesBefore = showZones;
            bool showGridBefore = showGrid;
            bool showPathsBefore = showPaths;
            showZones = true;
            showGrid = false;
            showPaths = false;
            inputBlocked = true;

            _ = Task.Run(() =>
            {
                gridFiller.FillGrid(fillAll: true, totalProgress: fillingGridProgressReporter, calculatingCandidatesProgress: calculatingCandidatesProgressReporter, placingCandidatesProgress: placingCandidatesProgressReporter);
                gridChanged = true;
                inputBlocked = false;
                showZones = showZonesBefore;
                showGrid = showGridBefore;
                showPaths = showPathsBefore;
            });
        }

        // Paint and clear tiles via mouse input

        if (mouseState.IsButtonDown(MouseButton.Left))
        {
            Vector2 mousePosition = camera.ScreenToWorld(mouseState.Position.ToVector2());
            Vector2 gridPosition = new Vector2((int)mousePosition.X / 10, (int)mousePosition.Y / 10);

            if (gridPosition.X < 0 || gridPosition.X >= gridFiller.Width || gridPosition.Y < 0 || gridPosition.Y >= gridFiller.Height || temporaryIndicators.ContainsKey(gridPosition))
            {
                return;
            }

            int cellValue = gridFiller.Grid[(int)gridPosition.Y, (int)gridPosition.X];
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

            PendingOperation pendingOperation = currentBrushMode == BrushMode.Weight
                ? new PendingOperation(PendingOperationKind.SetWeight, currentPaintWeight)
                : new PendingOperation(PendingOperationKind.PlaceObstacle, 0);

            EnqueuePendingOperations(brushPoints, pendingOperation);
        }
        else if (mouseState.IsButtonDown(MouseButton.Right))
        {
            Vector2 mousePosition = camera.ScreenToWorld(mouseState.Position.ToVector2());
            Vector2 gridPosition = new Vector2((int)mousePosition.X / 10, (int)mousePosition.Y / 10);

            if (gridPosition.X < 0 || gridPosition.X >= gridFiller.Width || gridPosition.Y < 0 || gridPosition.Y >= gridFiller.Height || temporaryIndicators.ContainsKey(gridPosition))
            {
                return;
            }

            int cellValue = gridFiller.Grid[(int)gridPosition.Y, (int)gridPosition.X];
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

            PendingOperation pendingOperation = currentBrushMode == BrushMode.Weight
                ? new PendingOperation(PendingOperationKind.ResetWeight, 0)
                : new PendingOperation(PendingOperationKind.RemoveObstacle, 0);

            EnqueuePendingOperations(brushPoints, pendingOperation);
        }
        else
        {
            previousMousePosition = null;
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (gridFiller is null)
        {
            return;
        }

        GraphicsDevice.Clear(Color.CornflowerBlue);

        Matrix transformMatrix = camera.GetViewMatrix();

        int yStart = Math.Max(0, (int)(camera.BoundingRectangle.Top / 10) - 2);
        int xStart = Math.Max(0, (int)(camera.BoundingRectangle.Left / 10) - 2);
        int yEnd = Math.Min(gridFiller.Height, (int)(camera.BoundingRectangle.Bottom / 10) + 2);
        int xEnd = Math.Min(gridFiller.Width, (int)(camera.BoundingRectangle.Right / 10) + 2);

        int[,] grid = gridFiller.Grid;
        Rectangle cellRect = new Rectangle(0, 0, 10, 10);

        Color[] colorLookup = [
        Color.Blue, Color.Cyan, Color.Magenta, Color.Yellow, Color.Orange,
        Color.DarkMagenta, Color.DarkCyan, Color.Tan, Color.RosyBrown,
        Color.DarkKhaki, Color.DarkSalmon, Color.DarkSlateGray,
        Color.DarkTurquoise, Color.DarkGoldenrod, Color.Aqua, Color.Aquamarine,
        Color.Bisque, Color.DarkSlateBlue, Color.BlueViolet, Color.Brown,
        Color.BurlyWood, Color.CadetBlue, Color.Chartreuse, Color.Chocolate,
        Color.Coral, Color.CornflowerBlue, Color.Crimson, Color.DarkBlue
    ];

        // Draw grid cells
        spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp, transformMatrix: transformMatrix);

        spriteBatch.FillRectangle(new Rectangle(0, 0, gridFiller.Width * 10, gridFiller.Height * 10), Color.White, layerDepth: 0.4f);

        if (showZones)
        {
            Dictionary<int, Rectangle> rectangles = new Dictionary<int, Rectangle>(gridFiller.PlacedRectangles);

            foreach ((int label, Rectangle rectangle) in rectangles)
            {
                Color color = colorLookup[label % 28];

                spriteBatch.FillRectangle(new Rectangle(rectangle.X * 10, rectangle.Y * 10, rectangle.Width * 10, rectangle.Height * 10), color, layerDepth: 0.2f);
            }
        }

        for (int y = yStart; y < yEnd; y++)
        {
            for (int x = xStart; x < xEnd; x++)
            {
                cellRect.X = x * 10;
                cellRect.Y = y * 10;

                int cellValue = grid[y, x];
                int cellWeight = gridFiller.WeightGrid[y, x];

                if (cellWeight > 1 && cellValue >= 0)
                {
                    float blendFactor = Math.Clamp((cellWeight - 1) / 8f, 0f, 1f);
                    Color weightColor = Color.Lerp(Color.LightYellow, Color.DarkOrange, blendFactor);
                    spriteBatch.FillRectangle(cellRect, weightColor * 0.75f, layerDepth: 0.25f);
                }

                if (cellValue < 0)
                {
                    spriteBatch.FillRectangle(cellRect, Color.Red, layerDepth: 0.3f);
                }

                if (temporaryIndicators.TryGetValue(new Vector2(x, y), out Color indicatorColor))
                {
                    spriteBatch.DrawCircle(cellRect.Center.ToVector2(), 5, 10, indicatorColor, 2f, layerDepth: 0.2f);
                }

                if (showGrid)
                {
                    spriteBatch.DrawRectangle(cellRect, Color.Black, layerDepth: 0.2f);
                }
            }
        }
        spriteBatch.End();

        // Draw paths in a separate batch if needed
        if (showPaths)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp, transformMatrix: transformMatrix);
            foreach (List<Vector2>? path in agents.Select(a => a.Path))
            {
                if (path is null)
                {
                    continue;
                }

                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 start = (path[i] * 10) + new Vector2(5, 5);
                    Vector2 end = (path[i + 1] * 10) + new Vector2(5, 5);

                    if ((start.X < camera.BoundingRectangle.Left - 10 && end.X < camera.BoundingRectangle.Left - 10) ||
                        (start.X > camera.BoundingRectangle.Right + 10 && end.X > camera.BoundingRectangle.Right + 10) ||
                        (start.Y < camera.BoundingRectangle.Top - 10 && end.Y < camera.BoundingRectangle.Top - 10) ||
                        (start.Y > camera.BoundingRectangle.Bottom + 10 && end.Y > camera.BoundingRectangle.Bottom + 10))
                    {
                        continue;
                    }

                    spriteBatch.DrawLine(start, end, Color.Black, 2f, layerDepth: 0.1f);
                    spriteBatch.DrawCircle(start, 5, 10, i == 0 ? Color.Green : Color.Black, 2f, layerDepth: 0.1f);
                    spriteBatch.DrawCircle(end, 5, 10, i == path.Count - 2 ? Color.Red : Color.Black, 2f, layerDepth: 0.1f);
                }
            }
            spriteBatch.End();
        }

        // Draw agents
        spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp, transformMatrix: transformMatrix);

        foreach (Vector2 currentPosition in agents.Select(a => a.Position))
        {
            if (currentPosition.X < xStart || currentPosition.X > xEnd || currentPosition.Y < yStart || currentPosition.Y > yEnd)
            {
                continue;
            }

            spriteBatch.DrawCircle((currentPosition + new Vector2(0.5f, 0.5f)) * 10, 5, 10, Color.Blue, 2f, layerDepth: 0.1f);
        }

        spriteBatch.End();

        // Draw UI elements
        uiSpriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp);

        uiSpriteBatch.DrawString(uiFont, $"FPS: {1 / gameTime.GetElapsedSeconds():0}", new Vector2(10, 10), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Grid: {gridFiller.Width}x{gridFiller.Height}", new Vector2(10, 30), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Zones: {gridFiller.PlacedRectangles.Count}", new Vector2(10, 50), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Agents: {agents.Count}", new Vector2(10, 70), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Brush mode: {brushMode} [O]", new Vector2(10, 90), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Weight: {paintWeight} [Keys 1-9, Weight mode]", new Vector2(10, 110), Color.Black);

        IReadOnlyList<ProgressTracker.ProgressData> progresses = progressTracker.GetProgresses();

        for (int i = 0; i < progresses.Count; i++)
        {
            ProgressTracker.ProgressData progressData = progresses[i];

            string progress = progressData.Indeterminate ? "..." : progressData.Progress.ToString("P0");

            uiSpriteBatch.DrawString(uiFont, $"{progressData.Name}: {progress}", new Vector2(10, 130 + (i * 20)), Color.Black);
        }

        uiSpriteBatch.End();

        base.Draw(gameTime);
    }

    private void StartInitializationTask()
    {
        _ = Task.Run(() =>
        {
            // Configuration options
            int width = 1000;
            int height = 1000;
            int agentCount = 1000;

            bool pathRandomization = false; // Randomize path costs to prevent agents from following the same path
            bool penalizeStretchedRectangles = false; // (EXPERIMENTAL) Penalize paths that go through stretched rectangles to prevent too many agents in a small area

            for (int i = 0; i < agentCount; i++)
            {
                int x = Random.Shared.Next(0, width - 1);
                int y = Random.Shared.Next(0, height - 1);

                agents.Add(new(new Vector2(x, y)));
            }

            List<Rectangle> obstacles = [];

            Debug.WriteLine("Generating random obstacles...");

            // Generate room/building like obstacles with walls and doors

            for (int i = 0; i < (width + height) / 2; i++)
            {
                int x = Random.Shared.Next(0, width - 1);
                int y = Random.Shared.Next(0, height - 1);
                int w = Random.Shared.Next(5, (width + height) / 100);
                int h = Random.Shared.Next(5, (width + height) / 100);

                Rectangle wall1 = new(x, y, w, 1);            // Top wall
                Rectangle wall2 = new(x, y, 1, h);            // Left wall
                Rectangle wall3 = new(x + w - 1, y, 1, h); // Right wall
                Rectangle wall4 = new(x, y + h - 1, w, 1); // Bottom wall

                int doorSide = Random.Shared.Next(4); // Choose a random side for the door

                if (doorSide == 0) // Bottom side
                {
                    obstacles.Add(wall1);
                    obstacles.Add(wall2);
                    obstacles.Add(wall3);

                    int doorX = x + Random.Shared.Next(1, w - 2);
                    Rectangle doorWall1 = new(x, y + h - 1, doorX - x, 1);
                    Rectangle doorWall2 = new(doorX + 2, y + h - 1, x + w - (doorX + 2), 1);

                    obstacles.Add(doorWall1);
                    obstacles.Add(doorWall2);
                }
                else if (doorSide == 1) // Top side
                {
                    obstacles.Add(wall2);
                    obstacles.Add(wall3);
                    obstacles.Add(wall4);

                    int doorX = x + Random.Shared.Next(1, w - 2);
                    Rectangle doorWall1 = new(x, y, doorX - x, 1);
                    Rectangle doorWall2 = new(doorX + 2, y, x + w - (doorX + 2), 1);

                    obstacles.Add(doorWall1);
                    obstacles.Add(doorWall2);
                }
                else if (doorSide == 2) // Left side
                {
                    obstacles.Add(wall1);
                    obstacles.Add(wall3);
                    obstacles.Add(wall4);

                    int doorY = y + Random.Shared.Next(1, h - 2);
                    Rectangle doorWall1 = new(x, y, 1, doorY - y);
                    Rectangle doorWall2 = new(x, doorY + 2, 1, y + h - (doorY + 2));

                    obstacles.Add(doorWall1);
                    obstacles.Add(doorWall2);
                }
                else if (doorSide == 3) // Right side
                {
                    obstacles.Add(wall1);
                    obstacles.Add(wall2);
                    obstacles.Add(wall4);

                    int doorY = y + Random.Shared.Next(1, h - 2);
                    Rectangle doorWall1 = new(x + w - 1, y, 1, doorY - y);
                    Rectangle doorWall2 = new(x + w - 1, doorY + 2, 1, y + h - (doorY + 2));

                    obstacles.Add(doorWall1);
                    obstacles.Add(doorWall2);
                }
            }

            Debug.WriteLine("Initializing grid...");

            ProgressTracker.ProgressData progressData = progressTracker.AddProgress("Initializing grid", true, out _);
            gridFiller = new GridFiller(width, height, obstacles);
            progressTracker.RemoveProgress(progressData);

            Debug.WriteLine("Filling grid...");

            _ = progressTracker.AddProgress("Filling grid", out IProgress<float> fillingGridProgressReporter);
            _ = progressTracker.AddProgress("Calculating candidates", out IProgress<float> calculatingCandidatesProgressReporter);
            _ = progressTracker.AddProgress("Placing zones", out IProgress<float> placingCandidatesProgressReporter);

            gridFiller.FillGrid(totalProgress: fillingGridProgressReporter, calculatingCandidatesProgress: calculatingCandidatesProgressReporter, placingCandidatesProgress: placingCandidatesProgressReporter);

            pathfinder = new Pathfinder(gridFiller.PlacedRectangles, gridFiller.Grid, gridFiller.WeightGrid, pathRandomization, penalizeStretchedRectangles);
            gridChanged = true;
            inputBlocked = false;
            showZones = false;
            showPaths = false;

            StartUpdatePathTask();
        });
    }

    // Handles pathfinding calculations in a separate task
    private void StartUpdatePathTask()
    {
        _ = Task.Factory.StartNew(async () =>
        {
            while (true)
            {
                try
                {
                    if (gridChanged)
                    {
                        gridChanged = false;
                        ProgressTracker.ProgressData progressDataBuildGraph = progressTracker.AddProgress("Rebuilding graph", true, out _);
                        pathfinder.RebuildGraph();
                        progressTracker.RemoveProgress(progressDataBuildGraph);
                    }

                    Agent[] agentsRequirePath = agents.Where(a => a.Path is null).ToArray();

                    if (agentsRequirePath.Length != 0)
                    {
                        ProgressTracker.ProgressData progressDataPaths = progressTracker.AddProgress($"Calculating {agentsRequirePath.Length} paths", out IProgress<float> progress);

                        int pathsCalculated = 0;
                        int reportInterval = Math.Max(1, agentsRequirePath.Length / 10);

                        // Calculate paths for agents that require a new path
                        _ = Parallel.For(0, agentsRequirePath.Length, i =>
                        {
                            Agent agent = agentsRequirePath[i];

                            agent.Path = CalculatePath(agent.GridPosition, agent.Destination);
                            agent.Path ??= CalculatePath();
                            agent.Position = agent.Path?[0] ?? agent.Position;
                            agent.NextPosition = agent.Path?[1] ?? agent.Position;
                            agent.Destination = agent.Path?.LastOrDefault().ToPoint();

                            int localPathsCalculated = Interlocked.Increment(ref pathsCalculated);

                            if (localPathsCalculated % reportInterval == 0)
                            {
                                progress.Report((float)localPathsCalculated / agentsRequirePath.Length);
                            }
                        });

                        progressTracker.RemoveProgress(progressDataPaths);
                    }

                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }, TaskCreationOptions.LongRunning);
    }

    private List<Vector2>? CalculatePath(Point? start = null, Point? goal = null)
    {
        int width = gridFiller.Width;
        int height = gridFiller.Height;
        int startX = Random.Shared.Next(0, width - 1);
        int startY = Random.Shared.Next(0, height - 1);
        int goalX = Random.Shared.Next(0, width - 1);
        int goalY = Random.Shared.Next(0, height - 1);

        start ??= new Point(startX, startY);
        goal ??= new Point(goalX, goalY);

        return pathfinder.FindPath(start.Value, goal.Value);
    }

    private void EnqueuePendingOperations(IEnumerable<Point> points, PendingOperation operation)
    {
        lock (pendingOperationsLock)
        {
            foreach (Point point in points)
            {
                pendingOperations[point] = operation;
            }

            if (batchProcessingScheduled)
            {
                return;
            }

            batchProcessingScheduled = true;
        }

        _ = Task.Run(ProcessPendingOperations);
    }

    private void ProcessPendingOperations()
    {
        while (true)
        {
            KeyValuePair<Point, PendingOperation>[] operationsBatch;

            lock (pendingOperationsLock)
            {
                if (pendingOperations.Count == 0)
                {
                    batchProcessingScheduled = false;
                    return;
                }

                operationsBatch = [.. pendingOperations];
                pendingOperations.Clear();
            }

            IGrouping<int, Point>[] weightGroups = [.. operationsBatch
                .Where(op => op.Value.Kind == PendingOperationKind.SetWeight)
                .GroupBy(op => op.Value.Weight, op => op.Key)];

            Point[] resetWeightPoints = [.. operationsBatch
                .Where(op => op.Value.Kind == PendingOperationKind.ResetWeight)
                .Select(op => op.Key)];

            Rectangle[] placeObstacleRectangles = [.. operationsBatch
                .Where(op => op.Value.Kind == PendingOperationKind.PlaceObstacle)
                .Select(op => new Rectangle(op.Key.X, op.Key.Y, 1, 1))];

            Rectangle[] removeObstacleRectangles = [.. operationsBatch
                .Where(op => op.Value.Kind == PendingOperationKind.RemoveObstacle)
                .Select(op => new Rectangle(op.Key.X, op.Key.Y, 1, 1))];

            foreach (IGrouping<int, Point> weightGroup in weightGroups)
            {
                gridFiller.SetTileWeights(weightGroup, weightGroup.Key);
            }

            if (resetWeightPoints.Length > 0)
            {
                gridFiller.ResetTileWeights(resetWeightPoints);
            }

            if (placeObstacleRectangles.Length > 0)
            {
                gridFiller.PlaceObstacles(placeObstacleRectangles);
            }

            if (removeObstacleRectangles.Length > 0)
            {
                gridFiller.RemoveObstacles(removeObstacleRectangles);
            }

            foreach (KeyValuePair<Point, PendingOperation> operation in operationsBatch)
            {
                _ = temporaryIndicators.TryRemove(operation.Key.ToVector2(), out _);
            }

            gridChanged = true;
        }
    }

    private static List<Point> GetBrushPoints(Vector2 currentGridPosition, Vector2? previousGridPosition)
    {
        HashSet<Point> points = [];
        Point currentPoint = new Point((int)currentGridPosition.X, (int)currentGridPosition.Y);
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
            Point interpolatedPoint = new Point((int)Math.Round(interpolatedPosition.X), (int)Math.Round(interpolatedPosition.Y));
            _ = points.Add(interpolatedPoint);
        }

        return [.. points];
    }
}
