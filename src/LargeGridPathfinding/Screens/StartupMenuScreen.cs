using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MonoGame.Extended.Input;
using MonoGame.Extended.Screens;

using System;
using System.Collections.Generic;

namespace LargeGridPathfinding.Screens;

public class StartupMenuScreen : GameScreen
{
    private enum StartupMenuItem
    {
        GridWidth,
        GridHeight,
        AgentCount,
        MapPreset,
        ObstacleDivisor,
        PathRandomization,
        PenalizeStretchedRectangles
    }

    public enum StartupMapPreset
    {
        Rooms,
        Empty,
        WorstCase
    }

    private SpriteFont uiFont = null!;
    private readonly ProgressTracker progressTracker = new();
    private StartupMenuItem startupSelectedItem;
    private int startupGridWidth = 1000;
    private int startupGridHeight = 1000;
    private int startupAgentCount = 1000;
    private StartupMapPreset startupMapPreset = StartupMapPreset.Rooms;
    private int startupObstacleDivisor = 2;
    private bool startupPathRandomization;
    private bool startupPenalizeStretchedRectangles;
    private bool initialized;

    public int GridWidth => startupGridWidth;
    public int GridHeight => startupGridHeight;
    public int AgentCount => startupAgentCount;
    public int ObstacleDivisor => startupObstacleDivisor;
    public bool PathRandomization => startupPathRandomization;
    public bool PenalizeStretchedRectangles => startupPenalizeStretchedRectangles;
    public ProgressTracker ProgressTracker => progressTracker;
    public bool Initialized => initialized;

    public StartupMapPreset MapPreset => startupMapPreset;

    public StartupMenuScreen(Game game) : base(game)
    {
    }

    public override void LoadContent()
    {
        base.LoadContent();
        uiFont = Content.Load<SpriteFont>("ManoloMono");
    }

    public override void Update(GameTime gameTime)
    {
        KeyboardExtended.Update();

        KeyboardStateExtended keyboardState = KeyboardExtended.GetState();
        bool shiftPressed = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);
        bool ctrlPressed = keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl);

        if (keyboardState.WasKeyPressed(Keys.Up))
        {
            startupSelectedItem = (StartupMenuItem)Math.Max(0, (int)startupSelectedItem - 1);
        }
        else if (keyboardState.WasKeyPressed(Keys.Down))
        {
            startupSelectedItem = (StartupMenuItem)Math.Min((int)StartupMenuItem.PenalizeStretchedRectangles, (int)startupSelectedItem + 1);
        }

        int direction = 0;
        if (keyboardState.WasKeyPressed(Keys.Left))
        {
            direction = shiftPressed ? ctrlPressed ? -100 : -10 : -1;
        }
        else if (keyboardState.WasKeyPressed(Keys.Right))
        {
            direction = shiftPressed ? ctrlPressed ? 100 : 10 : 1;
        }

        if (direction != 0)
        {
            switch (startupSelectedItem)
            {
                case StartupMenuItem.GridWidth:
                    startupGridWidth = Math.Clamp(startupGridWidth + (direction * 100), 500, 30000);
                    break;

                case StartupMenuItem.GridHeight:
                    startupGridHeight = Math.Clamp(startupGridHeight + (direction * 100), 500, 30000);
                    break;

                case StartupMenuItem.AgentCount:
                    startupAgentCount = Math.Clamp(startupAgentCount + (direction * 1000), 100, 1000000);
                    break;

                case StartupMenuItem.MapPreset:
                    startupMapPreset = (StartupMapPreset)Math.Clamp((int)startupMapPreset + direction, (int)StartupMapPreset.Rooms, (int)StartupMapPreset.WorstCase);
                    break;

                case StartupMenuItem.ObstacleDivisor:
                    startupObstacleDivisor = Math.Clamp(startupObstacleDivisor + direction, 1, 50);
                    break;

                case StartupMenuItem.PathRandomization:
                    startupPathRandomization = direction > 0 || (direction >= 0 && startupPathRandomization);
                    break;

                case StartupMenuItem.PenalizeStretchedRectangles:
                    startupPenalizeStretchedRectangles = direction > 0 || (direction >= 0 && startupPenalizeStretchedRectangles);
                    break;
            }
        }

        if (keyboardState.WasKeyPressed(Keys.Enter))
        {
            initialized = true;
        }
    }

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        var spriteBatch = new SpriteBatch(GraphicsDevice);
        spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: SamplerState.PointClamp);

        spriteBatch.DrawString(uiFont, "Startup Configuration", new Vector2(40, 30), Color.Black);
        spriteBatch.DrawString(uiFont, "Arrow Up/Down: Select  Arrow Left/Right: Change  Enter: Start", new Vector2(40, 60), Color.Black);

        string[] lines =
        [
            $"Grid Width: {startupGridWidth}",
            $"Grid Height: {startupGridHeight}",
            $"Agent Count: {startupAgentCount}",
            $"Map Preset: {startupMapPreset}",
            $"Obstacle Divisor: {startupObstacleDivisor} (lower = more obstacles)",
            $"Path Randomization: {startupPathRandomization}",
            $"Penalize Stretched Rectangles: {startupPenalizeStretchedRectangles}"
        ];

        for (int i = 0; i < lines.Length; i++)
        {
            Color color = i == (int)startupSelectedItem ? Color.DarkBlue : Color.Black;
            string prefix = i == (int)startupSelectedItem ? "# " : "  ";
            spriteBatch.DrawString(uiFont, $"{prefix}{lines[i]}", new Vector2(40, 100 + (i * 28)), color);
        }

        IReadOnlyList<ProgressTracker.ProgressData> startupProgresses = progressTracker.GetProgresses();
        for (int i = 0; i < startupProgresses.Count; i++)
        {
            ProgressTracker.ProgressData progressData = startupProgresses[i];
            string progress = progressData.Indeterminate ? "..." : progressData.Progress.ToString("P0");
            spriteBatch.DrawString(uiFont, $"{progressData.Name}: {progress}", new Vector2(40, 300 + (i * 24)), Color.Black);
        }

        spriteBatch.End();
    }
}
