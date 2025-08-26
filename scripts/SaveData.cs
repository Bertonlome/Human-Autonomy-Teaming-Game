using System.Collections.Generic;

namespace Game;

public class SaveData
{
    public Dictionary<string, LevelCompletionData> LevelCompletionStatus {get; private set; } = new();

    public void SavelevelCompletion(string id, bool completed, int timeCompletedInSeconds, int mineralsAnalyzed)
    {
        if(!LevelCompletionStatus.ContainsKey(id))
        {
        LevelCompletionStatus[id] = new LevelCompletionData();

        }
        LevelCompletionStatus[id].IsCompleted = completed;
        LevelCompletionStatus[id].TimeCompletedInSeconds = timeCompletedInSeconds;
        LevelCompletionStatus[id].MineralsAnalyzed = mineralsAnalyzed;
    }
}