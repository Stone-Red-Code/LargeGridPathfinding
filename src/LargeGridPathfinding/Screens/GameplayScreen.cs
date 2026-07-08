using LargeGridPathfinding.Components;
using LargeGridPathfinding.Services;
using LargeGridPathfinding.Systems;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MonoGame.Extended;
using MonoGame.Extended.ECS;
using MonoGame.Extended.Input;
using MonoGame.Extended.Screens;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LargeGridPathfinding.Screens;

public class GameplayScreen : GameScreen
{
    private readonly ProgressTracker progressTracker;
    private readonly int startupGridWidth;
    private readonly int startupGridHeight;
    private readonly int startupAgentCount;
    private readonly int startupObstacleDivisor;
    private readonly bool startupPathRandomization;
    private readonly bool startupPenalizeStretchedRectangles;
    private readonly StartupMenuScreen.StartupMapPreset startupMapPreset;

    private SpriteBatch spriteBatch = null!;
    private SpriteBatch uiSpriteBatch = null!;
    private SpriteFont uiFont = null!;
    private OrthographicCamera camera = null!;
    private GridService? gridService;
    private PathfindingService? pathfindingService;
    private List<AgentComponent> agents = [];
    private World? world;
    private GridEditSystem? gridEditSystem;
    private readonly ConcurrentDictionary<Vector2, Color> temporaryIndicators = [];
    private bool inputBlocked = true;
    private bool showZones = true;
    private bool showGrid;
    private bool showPaths;
    private bool initializationComplete;
    private bool worldBuilt;

    public GameplayScreen(
        Game game,
        ProgressTracker progressTracker,
        int gridWidth,
        int gridHeight,
        int agentCount,
        StartupMenuScreen.StartupMapPreset mapPreset,
        int obstacleDivisor,
        bool pathRandomization,
        bool penalizeStretchedRectangles)
        : base(game)
    {
        this.progressTracker = progressTracker;
        startupGridWidth = gridWidth;
        startupGridHeight = gridHeight;
        startupAgentCount = agentCount;
        startupMapPreset = mapPreset;
        startupObstacleDivisor = obstacleDivisor;
        startupPathRandomization = pathRandomization;
        startupPenalizeStretchedRectangles = penalizeStretchedRectangles;
    }

    public override void Initialize()
    {
        camera = new OrthographicCamera(GraphicsDevice)
        {
            MinimumZoom = 0.1f,
            MaximumZoom = 2,
            Zoom = 0.5f,
        };

        base.Initialize();
    }

    public override void LoadContent()
    {
        base.LoadContent();
        spriteBatch = new SpriteBatch(GraphicsDevice);
        uiSpriteBatch = new SpriteBatch(GraphicsDevice);
        uiFont = Content.Load<SpriteFont>("ManoloMono");

        StartInitializationTask();
    }

    public override void Update(GameTime gameTime)
    {
        if (!Game.IsActive)
        {
            return;
        }

        if (!initializationComplete)
        {
            return;
        }

        if (!worldBuilt)
        {
            return;
        }

        // Drain pending path results from background worker
        if (pathfindingService is not null)
        {
            while (pathfindingService.PendingPathResults.TryDequeue(out (AgentComponent agent, List<Vector2>? path) result))
            {
                AgentComponent agent = result.agent;
                List<Vector2>? path = result.path;
                agent.Path = path;
                if (path?.Count > 0)
                {
                    agent.Position = path[0];
                    agent.NextPosition = path.Count > 1 ? path[1] : path[0];
                    agent.PathIndex = path.Count > 1 ? 1 : 0;
                    agent.Destination = new Point((int)path[^1].X, (int)path[^1].Y);
                }
            }
        }

        // Process pending grid operations in background
        ProcessPendingOperations();

        // Camera movement
        KeyboardExtended.Update();
        MouseExtended.Update();

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

        // Run ECS world update (calls all registered IUpdateSystems)
        world!.Update(gameTime);
    }

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        if (!initializationComplete || gridService is null)
        {
            uiSpriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp);
            uiSpriteBatch.DrawString(uiFont, "Initializing simulation...", new Vector2(40, 30), Color.Black);

            IReadOnlyList<ProgressTracker.ProgressData> progresses = progressTracker.GetProgresses();
            for (int i = 0; i < progresses.Count; i++)
            {
                ProgressTracker.ProgressData progressData = progresses[i];
                string progress = progressData.Indeterminate ? "..." : progressData.Progress.ToString("P0");
                uiSpriteBatch.DrawString(uiFont, $"{progressData.Name}: {progress}", new Vector2(40, 300 + (i * 24)), Color.Black);
            }

            uiSpriteBatch.End();
            return;
        }

        // Run ECS world draw (calls all registered IDrawSystems)
        if (worldBuilt)
        {
            world!.Draw(gameTime);
        }

        // Draw UI overlay
        uiSpriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp);

        uiSpriteBatch.DrawString(uiFont, $"FPS: {1 / gameTime.GetElapsedSeconds():0}", new Vector2(10, 10), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Grid: {gridService.Width}x{gridService.Height}", new Vector2(10, 30), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Zones: {gridService.Filler.PlacedRectangles.Count}", new Vector2(10, 50), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Agents: {agents.Count}", new Vector2(10, 70), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Brush mode: {gridEditSystem?.CurrentBrushMode ?? GridEditSystem.BrushMode.Obstacle} [O]", new Vector2(10, 90), Color.Black);
        uiSpriteBatch.DrawString(uiFont, $"Weight: {gridEditSystem?.PaintWeight ?? 0} [Keys 1-9, Weight mode]", new Vector2(10, 110), Color.Black);
        uiSpriteBatch.DrawString(uiFont, "Area fill: hold Shift + left click twice", new Vector2(10, 130), Color.Black);

        IReadOnlyList<ProgressTracker.ProgressData> currentProgresses = progressTracker.GetProgresses();
        for (int i = 0; i < currentProgresses.Count; i++)
        {
            ProgressTracker.ProgressData progressData = currentProgresses[i];
            string progress = progressData.Indeterminate ? "..." : progressData.Progress.ToString("P0");
            uiSpriteBatch.DrawString(uiFont, $"{progressData.Name}: {progress}", new Vector2(10, 150 + (i * 20)), Color.Black);
        }

        uiSpriteBatch.End();
    }

    private void BuildECSWorld()
    {
        if (gridService is null)
        {
            return;
        }

        AgentMovementSystem agentMovementSystem = new AgentMovementSystem(gridService, camera, agents);

        gridEditSystem = new GridEditSystem(
            gridService,
            camera,
            temporaryIndicators,
            progressTracker,
            () => inputBlocked,
            v => inputBlocked = v,
            () => showZones,
            v => showZones = v,
            () => showGrid,
            v => showGrid = v,
            () => showPaths,
            v => showPaths = v
        );

        GridRenderSystem gridRenderSystem = new GridRenderSystem(
            spriteBatch,
            gridService,
            camera,
            temporaryIndicators,
            () => showZones,
            () => showGrid,
            () => gridEditSystem.FillSelectionStart,
            () => gridEditSystem.FillSelectionCurrent
        );

        PathRenderSystem pathRenderSystem = new PathRenderSystem(
            spriteBatch,
            camera,
            () => showPaths,
            () => agentMovementSystem.SectorsX,
            () => agentMovementSystem.SectorsY,
            () => agentMovementSystem.AgentSectors
        );

        AgentRenderSystem agentRenderSystem = new AgentRenderSystem(
            spriteBatch,
            camera,
            () => agentMovementSystem.SectorsX,
            () => agentMovementSystem.SectorsY,
            () => agentMovementSystem.AgentSectors
        );

        world = new WorldBuilder()
            .AddSystem(agentMovementSystem)
            .AddSystem(gridEditSystem)
            .AddSystem(gridRenderSystem)
            .AddSystem(pathRenderSystem)
            .AddSystem(agentRenderSystem)
            .Build();

        // Create ECS entities for each agent
        foreach (AgentComponent agent in agents)
        {
            world.CreateEntity().Attach(agent);
        }

        worldBuilt = true;
    }

    private void StartInitializationTask()
    {
        _ = Task.Run(() =>
        {
            List<AgentComponent> agentList = new(startupAgentCount);

            for (int i = 0; i < startupAgentCount; i++)
            {
                int x = Random.Shared.Next(0, startupGridWidth - 1);
                int y = Random.Shared.Next(0, startupGridHeight - 1);
                agentList.Add(new AgentComponent { Position = new Vector2(x, y) });
            }

            List<Rectangle> obstacles = [];

            if (startupMapPreset == StartupMenuScreen.StartupMapPreset.Rooms)
            {
                Debug.WriteLine("Generating random obstacles...");

                int obstacleIterations = Math.Max(0, (startupGridWidth + startupGridHeight) / Math.Max(1, startupObstacleDivisor));
                int minObstacleSize = 5;
                int minDimension = Math.Min(startupGridWidth, startupGridHeight);
                int scaledByDimension = (int)Math.Round(Math.Pow(minDimension, 0.4) * 2.1);
                int maxByDimension = Math.Max(minObstacleSize + 1, minDimension / 4);
                int maxObstacleSize = Math.Max(minObstacleSize + 1, Math.Min(scaledByDimension, maxByDimension));

                for (int i = 0; i < obstacleIterations; i++)
                {
                    int x = Random.Shared.Next(0, startupGridWidth - 1);
                    int y = Random.Shared.Next(0, startupGridHeight - 1);
                    int w = Random.Shared.Next(minObstacleSize, maxObstacleSize);
                    int h = Random.Shared.Next(minObstacleSize, maxObstacleSize);

                    Rectangle wall1 = new(x, y, w, 1);
                    Rectangle wall2 = new(x, y, 1, h);
                    Rectangle wall3 = new(x + w - 1, y, 1, h);
                    Rectangle wall4 = new(x, y + h - 1, w, 1);

                    int doorSide = Random.Shared.Next(4);
                    if (doorSide == 0)
                    {
                        obstacles.Add(wall1); obstacles.Add(wall2); obstacles.Add(wall3);
                        int doorX = x + Random.Shared.Next(1, w - 2);
                        obstacles.Add(new Rectangle(x, y + h - 1, doorX - x, 1));
                        obstacles.Add(new Rectangle(doorX + 2, y + h - 1, x + w - (doorX + 2), 1));
                    }
                    else if (doorSide == 1)
                    {
                        obstacles.Add(wall2); obstacles.Add(wall3); obstacles.Add(wall4);
                        int doorX = x + Random.Shared.Next(1, w - 2);
                        obstacles.Add(new Rectangle(x, y, doorX - x, 1));
                        obstacles.Add(new Rectangle(doorX + 2, y, x + w - (doorX + 2), 1));
                    }
                    else if (doorSide == 2)
                    {
                        obstacles.Add(wall1); obstacles.Add(wall3); obstacles.Add(wall4);
                        int doorY = y + Random.Shared.Next(1, h - 2);
                        obstacles.Add(new Rectangle(x, y, 1, doorY - y));
                        obstacles.Add(new Rectangle(x, doorY + 2, 1, y + h - (doorY + 2)));
                    }
                    else
                    {
                        obstacles.Add(wall1); obstacles.Add(wall2); obstacles.Add(wall4);
                        int doorY = y + Random.Shared.Next(1, h - 2);
                        obstacles.Add(new Rectangle(x + w - 1, y, 1, doorY - y));
                        obstacles.Add(new Rectangle(x + w - 1, doorY + 2, 1, y + h - (doorY + 2)));
                    }
                }
            }
            else if (startupMapPreset == StartupMenuScreen.StartupMapPreset.WorstCase)
            {
                Debug.WriteLine("Generating connected worst-case 3x3 rooms with offset doors...");
                const int roomSize = 3;
                const int pitch = roomSize + 1;

                for (int y = pitch; y < startupGridHeight; y += pitch)
                {
                    int runStart = 0;
                    int rowIndex = y / pitch;

                    for (int x = 0; x < startupGridWidth; x++)
                    {
                        int local = x % pitch;
                        int baseX = x - local;
                        bool oddSegment = (baseX / pitch % 2) == 1;
                        int offset = ((rowIndex % 2 == 0) ^ oddSegment) ? 1 : 3;
                        bool isDoor = local == offset;

                        if (isDoor)
                        {
                            if (x > runStart)
                            {
                                obstacles.Add(new Rectangle(runStart, y, x - runStart, 1));
                            }
                            runStart = x + 1;
                        }
                    }

                    if (runStart < startupGridWidth)
                    {
                        obstacles.Add(new Rectangle(runStart, y, startupGridWidth - runStart, 1));
                    }
                }

                for (int x = pitch; x < startupGridWidth; x += pitch)
                {
                    int runStart = 0;
                    int colIndex = x / pitch;

                    for (int y = 0; y < startupGridHeight; y++)
                    {
                        int local = y % pitch;
                        int baseY = y - local;
                        bool oddSegment = (baseY / pitch % 2) == 1;
                        int offset = ((colIndex % 2 == 0) ^ oddSegment) ? 1 : 3;
                        bool isDoor = local == offset;

                        if (isDoor)
                        {
                            if (y > runStart)
                            {
                                obstacles.Add(new Rectangle(x, runStart, 1, y - runStart));
                            }
                            runStart = y + 1;
                        }
                    }

                    if (runStart < startupGridHeight)
                    {
                        obstacles.Add(new Rectangle(x, runStart, 1, startupGridHeight - runStart));
                    }
                }
            }
            else
            {
                Debug.WriteLine("Using empty map preset.");
            }

            Debug.WriteLine("Initializing grid...");

            ProgressTracker.ProgressData progressData = progressTracker.AddProgress("Initializing grid", true, out _);
            GridFiller gf = new GridFiller(startupGridWidth, startupGridHeight);
            gf.PlaceObstacles(obstacles, false);
            progressTracker.RemoveProgress(progressData);

            Debug.WriteLine("Filling grid...");

            _ = progressTracker.AddProgress("Filling grid", out IProgress<float> fillingGridProgressReporter);
            _ = progressTracker.AddProgress("Calculating candidates", out IProgress<float> calculatingCandidatesProgressReporter);
            _ = progressTracker.AddProgress("Placing zones", out IProgress<float> placingCandidatesProgressReporter);

            gf.FillGrid(totalProgress: fillingGridProgressReporter, calculatingCandidatesProgress: calculatingCandidatesProgressReporter, placingCandidatesProgress: placingCandidatesProgressReporter);

            GridService gs = new GridService(gf);
            Pathfinder pf = new Pathfinder(gf.PlacedRectangles, gf.Grid, gf.WeightGrid, startupPathRandomization, startupPenalizeStretchedRectangles);
            PathfindingService pfs = new PathfindingService(pf, gs, agentList, progressTracker);

            gridService = gs;
            pathfindingService = pfs;
            agents = agentList;
            gs.GridChanged = true;
            inputBlocked = false;
            showZones = false;
            showPaths = false;

            initializationComplete = true;

            // Build the ECS world (must be on main thread — deferred to next Update)
            BuildECSWorld();

            pfs.Start();
        });
    }

    private void ProcessPendingOperations()
    {
        if (gridService is null)
        {
            return;
        }

        if (gridService.TryDequeueBatch(out GridService.PendingOperationEntry[]? batch))
        {
            _ = Task.Run(() =>
            {
                gridService.ProcessOperationBatch(batch);

                foreach (GridService.PendingOperationEntry entry in batch)
                {
                    _ = temporaryIndicators.TryRemove(entry.Point.ToVector2(), out _);
                }
            });
        }
    }
}
