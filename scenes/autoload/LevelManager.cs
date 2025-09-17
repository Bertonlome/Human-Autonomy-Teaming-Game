using System.Linq;
using Game.Resources.Level;
using Godot;

namespace Game.Autoload;

public partial class LevelManager : Node
{
	private static LevelManager instance;

	[Export]
	private LevelDefinitionResource[] levelDefinitions;

	[Export(PropertyHint.File, "*.tscn")]
	private string introCutScenePath;
	[Export(PropertyHint.File, "*.tscn")]
	private string mainMenuScenePath;

	private static int currentLevelIndex;

	public override void _Notification(int what)
	{
		if (what == NotificationSceneInstantiated)
		{
			instance = this;
		}
	}

	public static LevelDefinitionResource[] GetLevelDefinitions()
	{
		return instance.levelDefinitions.ToArray();
	}

	public static void ChangeToLevel(int levelIndex)
	{
		if (levelIndex >= instance.levelDefinitions.Length || levelIndex < 0) return;
		currentLevelIndex = levelIndex;

		var levelDefinition = instance.levelDefinitions[currentLevelIndex];
		instance.GetTree().ChangeSceneToFile(levelDefinition.LevelScenePath);
	}

	public static void ChangeToIntroCutScene()
	{
		instance.GetTree().ChangeSceneToFile(instance.introCutScenePath);
	}

	public static void ChangeToMainMenu()
	{
		instance.GetTree().ChangeSceneToFile(instance.mainMenuScenePath);
	}

	public static void ChangeToNextLevel()
	{
		ChangeToLevel(currentLevelIndex + 1);
	}
	public static bool IsLastLevel()
	{
		return currentLevelIndex == instance.levelDefinitions.Length - 1;
	}
}
