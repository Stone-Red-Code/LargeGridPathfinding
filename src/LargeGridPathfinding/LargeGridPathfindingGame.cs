using Microsoft.Xna.Framework;

using MonoGame.Extended.Screens;

using LargeGridPathfinding.Screens;

namespace LargeGridPathfinding;

public class LargeGridPathfindingGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly ScreenManager screenManager;
    private readonly ProgressTracker progressTracker = new();

    public LargeGridPathfindingGame()
    {
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1920,
            PreferredBackBufferHeight = 1080
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        Window.Title = "Large Grid Pathfinding";
        Window.AllowUserResizing = true;

        screenManager = new ScreenManager();
        Components.Add(screenManager);
    }

    protected override void LoadContent()
    {
        screenManager.ShowScreen(new StartupMenuScreen(this));
        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        // Check if startup screen has been confirmed and transition to gameplay
        if (screenManager.ActiveScreen is StartupMenuScreen startupScreen && startupScreen.Initialized)
        {
            screenManager.ReplaceScreen(new GameplayScreen(
                this,
                startupScreen.ProgressTracker,
                startupScreen.GridWidth,
                startupScreen.GridHeight,
                startupScreen.AgentCount,
                startupScreen.MapPreset,
                startupScreen.ObstacleDivisor,
                startupScreen.PathRandomization,
                startupScreen.PenalizeStretchedRectangles
            ));
        }

        base.Update(gameTime);
    }
}
