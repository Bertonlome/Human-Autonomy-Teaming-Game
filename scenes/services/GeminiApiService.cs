using Godot;
using System;
using System.Threading.Tasks;
using Google.GenAI;
using Google.GenAI.Types;

namespace Game.Services;

/// <summary>
/// Service for communicating with Google's Gemini API using official SDK
/// </summary>
public partial class GeminiApiService : Node
{
	private Client geminiClient;
	private string apiKey = "";
	private const string MODEL_NAME = "gemini-2.0-flash-exp";
	
	public override void _Ready()
	{
		// Load API key from environment variable or config
		LoadApiKey();
		
		GD.Print($"[GeminiApiService] API key loaded: {(!string.IsNullOrEmpty(apiKey) ? "YES" : "NO")}");
		
		// Initialize Gemini client if API key is available
		if (!string.IsNullOrEmpty(apiKey))
		{
			try
			{
				geminiClient = new Client(apiKey: apiKey);
				GD.Print("[GeminiApiService] Gemini client initialized successfully");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[GeminiApiService] Failed to initialize Gemini client: {ex.Message}");
				GD.PrintErr($"[GeminiApiService] Stack trace: {ex.StackTrace}");
			}
		}
		else
		{
			GD.PrintErr("[GeminiApiService] No API key found!");
		}
	}
	
	/// <summary>
	/// Load API key from environment variable or user://config file
	/// </summary>
	private void LoadApiKey()
	{
		// Try environment variable first
		apiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");
		
		if (string.IsNullOrEmpty(apiKey))
		{
			// Try loading from config file
			string configPath = "user://gemini_config.txt";
			string resolvedPath = ProjectSettings.GlobalizePath(configPath);
			GD.Print($"[GeminiApiService] Looking for config file at: {configPath}");
			GD.Print($"[GeminiApiService] Resolved to absolute path: {resolvedPath}");
			GD.Print($"[GeminiApiService] File exists: {FileAccess.FileExists(configPath)}");
			
			if (FileAccess.FileExists(configPath))
			{
				using var file = FileAccess.Open(configPath, FileAccess.ModeFlags.Read);
				if (file != null)
				{
					apiKey = file.GetAsText().Trim();
					GD.Print($"[GeminiApiService] Loaded API key from config file (length: {apiKey.Length})");
				}
				else
				{
					GD.PrintErr("[GeminiApiService] Failed to open config file");
				}
			}
			else
			{
				GD.PrintErr($"[GeminiApiService] Config file not found at: {resolvedPath}");
			}
		}
		else
		{
			GD.Print("[GeminiApiService] Loaded API key from environment variable");
		}
		
		if (string.IsNullOrEmpty(apiKey))
		{
			GD.PrintErr("Gemini API key not found! Set GEMINI_API_KEY environment variable or create user://gemini_config.txt");
		}
		else
		{
			GD.Print("Gemini API key loaded successfully");
		}
	}
	
	/// <summary>
	/// Save API key to user://gemini_config.txt
	/// </summary>
	public void SaveApiKey(string key)
	{
		apiKey = key;
		string configPath = "user://gemini_config.txt";
		using var file = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
		if (file != null)
		{
			file.StoreString(key);
			GD.Print("Gemini API key saved to config file");
		}
		
		// Reinitialize client with new key
		try
		{
			geminiClient = new Client(apiKey: apiKey);
			GD.Print("Gemini client reinitialized with new API key");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to initialize Gemini client: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Send a request to Gemini API to optimize a path
	/// </summary>
	/// <param name="pathJson">The JSON representation of the current path and context</param>
	/// <returns>The LLM's response as JSON string</returns>
	public async Task<string> OptimizePathAsync(string pathJson)
	{
		if (string.IsNullOrEmpty(apiKey) || geminiClient == null)
		{
			GD.PrintErr("Cannot call Gemini API: API key not configured or client not initialized");
			return GenerateErrorResponse("API key not configured");
		}
		
		try
		{
			// Construct the prompt for Gemini
			string prompt = ConstructOptimizationPrompt(pathJson);
			
			GD.Print("Sending request to Gemini API...");
			
			// Use the official SDK to generate content
			var response = await geminiClient.Models.GenerateContentAsync(
				model: MODEL_NAME,
				contents: prompt
			);
			
			// Extract the text from the response
			if (response?.Candidates != null && response.Candidates.Count > 0)
			{
				var candidate = response.Candidates[0];
				if (candidate?.Content?.Parts != null && candidate.Content.Parts.Count > 0)
				{
					string text = candidate.Content.Parts[0].Text;
					
					// Clean up markdown code blocks if present
					text = text.Trim();
					if (text.StartsWith("```json"))
					{
						text = text.Substring(7);
					}
					if (text.StartsWith("```"))
					{
						text = text.Substring(3);
					}
					if (text.EndsWith("```"))
					{
						text = text.Substring(0, text.Length - 3);
					}
					
					GD.Print("Received response from Gemini API");
					return text.Trim();
				}
			}
			
			GD.PrintErr("Empty or invalid response from Gemini API");
			return GenerateErrorResponse("Invalid response from API");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Exception calling Gemini API: {ex.Message}");
			GD.PrintErr($"Stack trace: {ex.StackTrace}");
			return GenerateErrorResponse($"Exception: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Construct the optimization prompt for the LLM
	/// </summary>
	private string ConstructOptimizationPrompt(string pathJson)
	{
		return $@"You are an AI assistant helping to optimize robot paths in a strategy game. 

The user has painted a path for their robot, and you need to analyze it and suggest an optimized version.

Here is the current path data (includes painted tiles, robot info, and contextual tiles showing reachability):
{pathJson}

Your task:
1. Analyze the current path for inefficiencies (unnecessary detours, zigzags, etc.)
2. Consider the reachability information from contextTiles
3. Keep the start and end points the same
4. Optimize the middle of the path for efficiency
5. Preserve any important annotations

Respond with ONLY a JSON object in this exact format (no markdown, no extra text):
{{
	""success"": true,
	""message"": ""Path optimized successfully"",
	""suggestedPath"": {{
		""robotName"": ""string"",
		""isAerial"": boolean,
		""tiles"": [
			{{
				""tileNumber"": 1,
				""gridX"": number,
				""gridY"": number,
				""annotation"": ""string"",
				""robotName"": ""string"",
				""isAerial"": boolean
			}}
		],
		""totalTiles"": number
	}},
	""reasoning"": ""Brief explanation of optimizations made""
}}";
	}
	
	/// <summary>
	/// Generate an error response in the expected format
	/// </summary>
	private string GenerateErrorResponse(string errorMessage)
	{
		return @"{
			""success"": false,
			""message"": """ + errorMessage + @""",
			""suggestedPath"": null,
			""reasoning"": ""Error occurred during path optimization""
		}";
	}
	
	/// <summary>
	/// Check if API key is configured
	/// </summary>
	public bool IsConfigured()
	{
		return !string.IsNullOrEmpty(apiKey) && geminiClient != null;
	}
}
