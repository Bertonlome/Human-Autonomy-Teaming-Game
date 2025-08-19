using Godot;
using Game.Autoload;

namespace Game.UI;

public partial class LevelFailedScreen : CanvasLayer
{
	private Button returnToMenuButton;
	[Export(PropertyHint.File, "*.tscn")]
	private string mainMenuScenePath;

	public override void _Ready()
	{
		returnToMenuButton = GetNode<Button>("%BackToMenuButton");

		AudioHelpers.PlayFailed();

		if(LevelManager.IsLastLevel())
		{
			returnToMenuButton.Text = "Return to Menu";
		}

		returnToMenuButton.Pressed += OnReturnToMenuButtonPressed;

	}

	private void OnReturnToMenuButtonPressed()
	{
			GetTree().ChangeSceneToFile(mainMenuScenePath);
	}
}
