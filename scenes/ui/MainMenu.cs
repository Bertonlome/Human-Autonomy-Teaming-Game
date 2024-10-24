using Game.Autoload;
using Godot;

namespace Game.UI;

public partial class MainMenu : Node
{
	[Export]
	private PackedScene optionsMenuScene;

	private Button playButton;
	private Control mainMenuContainer;
	private LevelSelectScreen levelSelectScreen;
	private Button quitButton;
	private Button optionsButton;

	public override void _Ready()
	{
		playButton = GetNode<Button>("%PlayButton");
		quitButton = GetNode<Button>("%QuitButton");
		optionsButton = GetNode<Button>("%OptionsButton");


		AudioHelpers.RegisterButtons(new Button[] {playButton, quitButton});
		
		mainMenuContainer = GetNode<Control>("%MainMenuContainer");
		levelSelectScreen = GetNode<LevelSelectScreen>("%LevelSelectScreen");

		levelSelectScreen.Visible = false;
		mainMenuContainer.Visible = true;

		playButton.Pressed += OnPlayButtonPressed;
		quitButton.Pressed += OnQuitButtonPressed;
		levelSelectScreen.BackPressed += OnLevelSelectBackPressed;
		optionsButton.Pressed += OnOptionsButtonPressed;
	}

	private void OnPlayButtonPressed()
	{
		mainMenuContainer.Visible = false;
		levelSelectScreen.Visible = true;
	}

	private void OnLevelSelectBackPressed()
	{
		mainMenuContainer.Visible = true;
		levelSelectScreen.Visible = false;
	}

	private void OnQuitButtonPressed()
	{
		GetTree().Quit();
	}

	private void OnOptionsButtonPressed()
	{
		mainMenuContainer.Visible = false;
		var optionsMenu = optionsMenuScene.Instantiate<OptionsMenu>();
		AddChild(optionsMenu);
		optionsMenu.DonePressed += () => {
			OnOptionsDonePressed(optionsMenu);
		};
	}

	private void OnOptionsDonePressed(OptionsMenu optionsMenu)
	{
		optionsMenu.QueueFree();
		mainMenuContainer.Visible = true;
	}
}
