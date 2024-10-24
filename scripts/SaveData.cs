using System.Collections.Generic;

namespace Game;

public class SaveData
{
    public Dictionary<string, LevelCompletionData> LevelCompletionStatus {get; private set; } = new();

    public void SavelevelCompletion(string id, bool completed)
    {
        if(!LevelCompletionStatus.ContainsKey(id))
        {
        LevelCompletionStatus[id] = new LevelCompletionData();

        }
        LevelCompletionStatus[id].IsCompleted = completed;
    }
}