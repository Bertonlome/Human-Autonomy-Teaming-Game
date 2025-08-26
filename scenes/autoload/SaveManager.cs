using System;
using System.Collections.Specialized;
using Game;
using Game.Resources.Level;
using Godot;
using Newtonsoft.Json;

public partial class SaveManager : Node
{
	public static SaveManager Instance {get; private set;}

	public static SaveData saveData = new();

	private static readonly string SAVE_FILE_PATH = "user://save.json";

		public override void _Notification(int what)
	{
		if (what == NotificationSceneInstantiated)
		{
			Instance = this;
			LoadSaveData();
		}
	}

	public static bool IsLevelCompleted(string levelId)
	{
		saveData.LevelCompletionStatus.TryGetValue(levelId, out var data);
		return data?.IsCompleted == true;
	}

	public static TimeSpan GetBestTimeForLevel(string levelId)
	{
		saveData.LevelCompletionStatus.TryGetValue(levelId, out var data);
		if(data == null || data.TimeCompletedInSeconds <= 0)
		{
			return TimeSpan.Zero;
		}
		return TimeSpan.FromSeconds(data.TimeCompletedInSeconds);
	}

	public static int GetMineralsAnalyzedForLevel(string levelId)
	{
		saveData.LevelCompletionStatus.TryGetValue(levelId, out var data);
		return data?.MineralsAnalyzed ?? 0;
	}

	public static void SavelevelCompletion(LevelDefinitionResource levelDefinitionResource, int timeCompletedInSeconds, int mineralsAnalyzed)
	{
		saveData.SavelevelCompletion(levelDefinitionResource.Id, true, timeCompletedInSeconds, mineralsAnalyzed);
		WriteSaveData();
	}

	private static void WriteSaveData()
	{
		var dataString = JsonConvert.SerializeObject(saveData);

		using var saveFile = FileAccess.Open(SAVE_FILE_PATH, FileAccess.ModeFlags.Write);
		saveFile.StoreLine(dataString);
	}

	private static void LoadSaveData()
	{
		if(!FileAccess.FileExists(SAVE_FILE_PATH))
		{
			return;
		}

		using var saveFile = FileAccess.Open(SAVE_FILE_PATH, FileAccess.ModeFlags.Read);
		var dataString = saveFile.GetLine();
		try
		{
		saveData = JsonConvert.DeserializeObject<SaveData>(dataString);
		}
		catch(Exception _)
		{
			GD.PushWarning("Save JSON file was corrupted");
		}
	}
}
