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
    private readonly ConcurrentDictionary<Vector2, Color> temporaryIndicators = [];
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
    private Vector2? previousMousePosition;

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

        // Place or remove obstacles via mouse input

        if (mouseState.IsButtonDown(MouseButton.Left))
        {
            Vector2 mousePosition = camera.ScreenToWorld(mouseState.Position.ToVector2());
            Vector2 gridPosition = new Vector2((int)mousePosition.X / 10, (int)mousePosition.Y / 10);

            if (gridPosition.X < 0 || gridPosition.X >= gridFiller.Width || gridPosition.Y < 0 || gridPosition.Y >= gridFiller.Height || gridFiller.Grid[(int)gridPosition.Y, (int)gridPosition.X] < 0 || temporaryIndicators.ContainsKey(gridPosition))
            {
                return;
            }

            Vector2? previousGridPosition = null;
            if (previousMousePosition is not null)
            {
                previousGridPosition = new Vector2((int)previousMousePosition.Value.X / 10, (int)previousMousePosition.Value.Y / 10);
            }

            // Place temporary indicators to show the obstacle is scheduled for placement
            temporaryIndicators[gridPosition] = Color.Red;

            previousMousePosition = mousePosition;

            _ = Task.Run(() =>
            {
                if (previousGridPosition is not null)
                {
                    List<Vector2> interpolatedPositions = [];

                    // Interpolate between previous and current mouse position to place obstacles in a straight line to prevent gaps
                    float distance = Vector2.Distance(previousGridPosition.Value, gridPosition);
                    float lerpSteps = distance * 10;

                    for (float t = 0; t <= 1; t += 1 / lerpSteps)
                    {
                        Vector2 interpolatedPosition = Vector2.Lerp(previousGridPosition.Value, gridPosition, t);
                        interpolatedPosition.Round();
                        interpolatedPositions.Add(interpolatedPosition);
                        temporaryIndicators[interpolatedPosition] = Color.Red;
                    }

                    foreach (Vector2 interpolatedPosition in interpolatedPositions.Distinct())
                    {
                        gridFiller.PlaceObstacle(new Rectangle((int)interpolatedPosition.X, (int)interpolatedPosition.Y, 1, 1));
                        _ = temporaryIndicators.TryRemove(interpolatedPosition, out _);
                    }
                }

                gridFiller.PlaceObstacle(new Rectangle((int)gridPosition.X, (int)gridPosition.Y, 1, 1));
                gridChanged = true;
                _ = temporaryIndicators.TryRemove(gridPosition, out _);
            });
        }
        else if (mouseState.IsButtonDown(MouseButton.Right))
        {
            Vector2 mousePosition = camera.ScreenToWorld(mouseState.Position.ToVector2());
            Vector2 gridPosition = new Vector2((int)mousePosition.X / 10, (int)mousePosition.Y / 10);

            if (gridPosition.X < 0 || gridPosition.X >= gridFiller.Width || gridPosition.Y < 0 || gridPosition.Y >= gridFiller.Height || gridFiller.Grid[(int)gridPosition.Y, (int)gridPosition.X] > 0 || temporaryIndicators.ContainsKey(gridPosition))
            {
                return;
            }

            Vector2? previousGridPosition = null;
            if (previousMousePosition is not null)
            {
                previousGridPosition = new Vector2((int)previousMousePosition.Value.X / 10, (int)previousMousePosition.Value.Y / 10);
            }

            // Place temporary indicators to show the obstacle is scheduled for removal
            temporaryIndicators[gridPosition] = Color.Yellow;

            previousMousePosition = mousePosition;

            _ = Task.Run(() =>
            {
                if (previousGridPosition is not null)
                {
                    List<Vector2> interpolatedPositions = [];

                    // Interpolate between previous and current mouse position to place obstacles in a straight line to prevent gaps

                    float distance = Vector2.Distance(previousGridPosition.Value, gridPosition);
                    float lerpSteps = distance * 10;

                    for (float t = 0; t <= 1; t += 1 / lerpSteps)
                    {
                        Vector2 interpolatedPosition = Vector2.Lerp(previousGridPosition.Value, gridPosition, t);
                        interpolatedPosition.Round();
                        interpolatedPositions.Add(interpolatedPosition);
                        temporaryIndicators[interpolatedPosition] = Color.Yellow;
                    }

                    foreach (Vector2 interpolatedPosition in interpolatedPositions.Distinct())
                    {
                        gridFiller.RemoveObstacle(new Rectangle((int)interpolatedPosition.X, (int)interpolatedPosition.Y, 1, 1));
                        _ = temporaryIndicators.TryRemove(interpolatedPosition, out _);
                    }
                }

                gridFiller.RemoveObstacle(new Rectangle((int)gridPosition.X, (int)gridPosition.Y, 1, 1));
                gridChanged = true;
                _ = temporaryIndicators.TryRemove(gridPosition, out _);
            });
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

        IReadOnlyList<ProgressTracker.ProgressData> progresses = progressTracker.GetProgresses();

        for (int i = 0; i < progresses.Count; i++)
        {
            ProgressTracker.ProgressData progressData = progresses[i];

            string progress = progressData.Indeterminate ? "..." : progressData.Progress.ToString("P0");

            uiSpriteBatch.DrawString(uiFont, $"{progressData.Name}: {progress}", new Vector2(10, 90 + (i * 20)), Color.Black);
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
            int agentCount = 10000;

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

            pathfinder = new Pathfinder(gridFiller.PlacedRectangles, pathRandomization, penalizeStretchedRectangles);
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

        int[,] grid = gridFiller.Grid;

        List<Vector2>? path = pathfinder.FindPath(ref grid[start.Value.Y, start.Value.X], ref grid[goal.Value.Y, goal.Value.X]);

        if (path is not null)
        {
            // Add start and goal positions to the path because the pathfinder only returns transitions between rectangles
            path.Insert(0, start.Value.ToVector2());
            path.Add(goal.Value.ToVector2());
        }

        return path;
    }
}