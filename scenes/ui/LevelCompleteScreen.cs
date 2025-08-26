using System;
using Game.Autoload;
using Game.Manager;
using Godot;

namespace Game.UI;

public partial class LevelCompleteScreen : CanvasLayer
{
	private Button returnToMenuButton;
	private Label timeCompleteLabel;
	private int timeElapsed;
	private Label mineralsAnalyzedLabel;
	[Export(PropertyHint.File, "*.tscn")]
	private string mainMenuScenePath;
	private BuildingManager buildingManager;

	public override void _Ready()
	{
		returnToMenuButton = GetNode<Button>("%NextLevelButton");
		timeCompleteLabel = GetNode<Label>("%TimeCompleteLabel");
		mineralsAnalyzedLabel = GetNode<Label>("%MineralsAnalyzedLabel");
		// Attempt to get the BuildingManager node from BaseLevel
		buildingManager = GetParent<BaseLevel>().GetFirstNodeOfType<BuildingManager>();

		if (buildingManager != null)
		{
			GD.Print($"First child of the root: {buildingManager.Name}");
			int mineralsAnalyzed = buildingManager.mineralAnalyzedCount;
			mineralsAnalyzedLabel.Text = $"Minerals Analyzed: {mineralsAnalyzed} /3";

		}
		else
		{
			GD.PushError("BuildingManager node not found.");
		}

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
	public void SetTimeElapsed(int seconds)
	{
		timeElapsed = seconds;
		var timeSpan = TimeSpan.FromSeconds(timeElapsed);
		timeCompleteLabel.Text = $"Completed in under {timeSpan:mm\\:ss}";
	}
}
