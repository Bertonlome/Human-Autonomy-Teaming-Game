using Game.Autoload;
using Godot;

namespace Game.UI;

public partial class LevelCompleteScreen : CanvasLayer
{
	private Button returnToMenuButton;
	[Export(PropertyHint.File, "*.tscn")]
	private string mainMenuScenePath;

	public override void _Ready()
	{
		returnToMenuButton = GetNode<Button>("%NextLevelButton");

		AudioHelpers.PlayVictory();

		if(LevelManager.IsLastLevel())
		{
			returnToMenuButton.Text = "Return to Menu";
		}

		returnToMenuButton.Pressed += OnNextLevelButtonPressed;

	}

	private void OnNextLevelButtonPressed()
	{
		if(!LevelManager.IsLastLevel())
		{
		LevelManager.ChangeToNextLevel();
		}
		else
		{
			GetTree().ChangeSceneToFile(mainMenuScenePath);
		}
	}
}
