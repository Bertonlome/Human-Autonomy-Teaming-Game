using Game.Autoload;
using Godot;

namespace Game.UI;

public partial class LevelCompleteScreen : CanvasLayer
{
	private Button nextLevelButton;
	[Export(PropertyHint.File, "*.tscn")]
	private string mainMenuScenePath;

	public override void _Ready()
	{
		nextLevelButton = GetNode<Button>("%NextLevelButton");

		AudioHelpers.PlayVictory();

		if(LevelManager.IsLastLevel())
		{
			nextLevelButton.Text = "Return to Menu";
		}

		nextLevelButton.Pressed += OnNextLevelButtonPressed;

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
