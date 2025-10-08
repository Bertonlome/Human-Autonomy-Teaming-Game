using System.Collections;
using Game.Autoload;
using Game.UI;
using Godot;


namespace Game.Ui;

public partial class EscapeMenu : CanvasLayer
{
	private readonly StringName ESCAPE_ACTION = "escape";

	[Export(PropertyHint.File, "*.tscn")]
	private string mainMenuScenePath;
	[Export]
	private PackedScene optionsMenuScene;
	[Export]
	private PackedScene controlsMenuScene;
	private Button quitButton;
	private Button resumeButton;
	private Button optionsButton;
	private Button controlsButton;
	private MarginContainer marginContainer;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		quitButton = GetNode<Button>("%QuitButton");
		resumeButton = GetNode<Button>("%ResumeButton");
		optionsButton = GetNode<Button>("%OptionsButton");
		marginContainer = GetNode<MarginContainer>("MarginContainer");
		controlsButton = GetNode<Button>("%ControlsButton");

		AudioHelpers.RegisterButtons(new Button[] { quitButton, resumeButton, optionsButton, controlsButton });

		quitButton.Pressed += OnQuitButtonPressed;
		resumeButton.Pressed += OnResumeButtonPressed;
		optionsButton.Pressed += OnOptionsButtonPressed;
		controlsButton.Pressed += OnControlsButtonPressed;
	}

	public override void _UnhandledInput(InputEvent evt)
	{
		if (evt.IsActionPressed(ESCAPE_ACTION))
		{
			QueueFree();
			GetViewport().SetInputAsHandled();
		}
	}

	private void OnOptionsButtonPressed()
	{
		marginContainer.Visible = false;
		var optionsMenu = optionsMenuScene.Instantiate<OptionsMenu>();
		AddChild(optionsMenu);
		optionsMenu.DonePressed += () =>
		{
			OnOptionsDonePressed(optionsMenu);
		};
	}

	private void OnResumeButtonPressed()
	{
		QueueFree();
	}

	private void OnQuitButtonPressed()
	{
		GetTree().ChangeSceneToFile(mainMenuScenePath);
	}

	private void OnOptionsDonePressed(OptionsMenu optionsMenu)
	{
		marginContainer.Visible = true;
		optionsMenu.QueueFree();
	}

	private void OnControlsButtonPressed()
	{
		marginContainer.Visible = false;
		var controlsMenu = controlsMenuScene.Instantiate<ControlsMenu>();
		AddChild(controlsMenu);
		controlsMenu.DonePressed += () =>
		{
			OnControlsDonePressed(controlsMenu);
		};
	}

	private void OnControlsDonePressed(ControlsMenu controlsMenu)
	{
		marginContainer.Visible = true;
		controlsMenu.QueueFree();
	}

}
