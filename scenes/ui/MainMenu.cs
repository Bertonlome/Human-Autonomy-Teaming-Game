using Game.Autoload;
using Godot;

namespace Game.UI;

public partial class MainMenu : Node
{
	[Export]
	private PackedScene optionsMenuScene;

	[Export]
	private PackedScene rulesMenuScene;

	private Button playButton;
	private Control mainMenuContainer;
	private LevelSelectScreen levelSelectScreen;
	private Button quitButton;
	private Button optionsButton;
	private Button rulesButton;

	private Button watchIntroButton;

	public override void _Ready()
	{
		playButton = GetNode<Button>("%PlayButton");
		quitButton = GetNode<Button>("%QuitButton");
		optionsButton = GetNode<Button>("%OptionsButton");
		rulesButton = GetNode<Button>("%RulesButton");
		watchIntroButton = GetNode<Button>("%IntroButton");

		AudioHelpers.RegisterButtons(new Button[] { playButton, quitButton, optionsButton, rulesButton, watchIntroButton });

		mainMenuContainer = GetNode<Control>("%MainMenuContainer");
		levelSelectScreen = GetNode<LevelSelectScreen>("%LevelSelectScreen");

		levelSelectScreen.Visible = false;
		mainMenuContainer.Visible = true;

		playButton.Pressed += OnPlayButtonPressed;
		quitButton.Pressed += OnQuitButtonPressed;
		levelSelectScreen.BackPressed += OnLevelSelectBackPressed;
		optionsButton.Pressed += OnOptionsButtonPressed;
		rulesButton.Pressed += OnRulesButtonPressed;
		watchIntroButton.Pressed += OnWatchIntroButtonPressed;
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

	private void OnWatchIntroButtonPressed()
	{
		mainMenuContainer.Visible = false;
		//var introCutscene = IntroCutsceneSceneManager.Instance.IntroCutsceneScene.Instantiate<IntroCutscene>();
		//AddChild(introCutscene);
		//introCutscene.CutsceneFinished += () =>
		//{
			//introCutscene.QueueFree();
			//mainMenuContainer.Visible = true;
		//};
	}

	private void OnOptionsButtonPressed()
	{
		mainMenuContainer.Visible = false;
		var optionsMenu = optionsMenuScene.Instantiate<OptionsMenu>();
		AddChild(optionsMenu);
		optionsMenu.DonePressed += () =>
		{
			OnOptionsDonePressed(optionsMenu);
		};
	}

	private void OnRulesButtonPressed()
	{
		mainMenuContainer.Visible = false;
		var rulesMenu = rulesMenuScene.Instantiate<RulesMenu>();
		AddChild(rulesMenu);
		rulesMenu.DonePressed += () =>
		{
			OnRulesDonePressed(rulesMenu);
		};
	}

	private void OnOptionsDonePressed(OptionsMenu optionsMenu)
	{
		optionsMenu.QueueFree();
		mainMenuContainer.Visible = true;
	}
	
	private void OnRulesDonePressed(RulesMenu rulesMenu)
	{
		rulesMenu.QueueFree();
		mainMenuContainer.Visible = true;
	}
}
