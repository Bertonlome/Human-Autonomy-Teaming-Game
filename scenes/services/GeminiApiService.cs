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
	private string apiKey;
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
	/// <param name="isMultiRobot">Whether this is a multi-robot request</param>
	/// <returns>The LLM's response as JSON string</returns>
	public async Task<string> OptimizePathAsync(string pathJson, bool isMultiRobot = false)
	{
		if (string.IsNullOrEmpty(apiKey) || geminiClient == null)
		{
			GD.PrintErr("Cannot call Gemini API: API key not configured or client not initialized");
			return GenerateErrorResponse("API key not configured");
		}
		
		try
		{
			// Construct the prompt for Gemini
			string prompt = ConstructOptimizationPrompt(pathJson, isMultiRobot);
			
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
	private string ConstructOptimizationPrompt(string pathJson, bool isMultiRobot)
	{
		if (isMultiRobot)
		{
			return $@"You are a strategic exploration planner for autonomous robots.

Your role is NOT to generate complete paths (A* pathfinding will handle that automatically).
Instead, provide STRATEGIC GUIDANCE:

1. **Waypoints**: Mandatory checkpoints the robot(s) must visit 
2. **Exclusion Zones**: Tiles to completely avoid during pathfinding (e.g., dangerous areas, redundant zones, collision risks)

The A* algorithm will automatically find the optimal path connecting these waypoints while avoiding exclusion zones.

**CRITICAL MULTI-ROBOT COLLABORATION RULES:**
- If you see annotations containing ""lift"", ""pick up"", ""carry"", ""transport"" a rover/robot: the drone must LIFT the ground robot
- If you see annotations containing ""drop"", ""release"", ""place"", ""deposit"" a rover/robot: the drone must DROP the ground robot
- When lifting/dropping is detected:
  1. The waypoint where lifting happens MUST have priority 1 and reason ""LIFT""
  2. The waypoint where dropping happens MUST have priority 2 and reason ""DROP""
  3. Do NOT add additional exploration waypoints - only LIFT and DROP
  4. Do NOT improvise extra exploration unless explicitly requested
- The system uses these exact keywords to trigger lift/drop operations - do not paraphrase them
- Example: If user writes ""lift rover from here"" at (17,4) and ""to here"" at (20,4), respond:
  waypoints: [{{gridX:17, gridY:4, priority:1, reason:""LIFT""}}, {{gridX:20, gridY:4, priority:2, reason:""DROP""}}]

Current painted paths show the general exploration area:
{pathJson}

Consider:
- FIRST: Check all annotations for lift/drop operations - these take absolute priority
- Which tiles are CRITICAL checkpoints that must be visited?
- Are there tiles that should be AVOIDED entirely?
- For multiple robots: coordinate to prevent collisions 
- Prioritize waypoints (lower priority number = visit first)

**CREATIVE EXPLORATION PATTERNS:**
When users provide creative/exploratory annotations (e.g., ""form a flower"", ""draw a circle"", ""make a spiral"", ""write HAT"", ""explore in a star pattern""):
- Generate waypoints that create the requested shape/pattern
- Use 8-15 waypoints to form recognizable shapes (flowers need ~8-12 points, text needs ~10-20)
- For flowers: create a central point + petal points radiating outward (typically 5-8 petals)
- For text: trace the letter outlines with waypoints
- For geometric shapes: place waypoints along the perimeter
- For spiral patterns: gradually increase radius while rotating
- Space waypoints 2-4 tiles apart for smooth shapes
- Be creative and proactive - don't just return empty waypoints when asked to explore creatively

Respond with ONLY a JSON object (no markdown, no extra text):
{{
  ""success"": true,
  ""message"": ""Strategic plan generated"",
  ""reasoning"": ""Explanation of your strategic decisions"",
  ""strategicPlans"": [
    {{
      ""robotName"": ""Rover"",
      ""waypoints"": [
        {{""gridX"": 10, ""gridY"": 5, ""priority"": 1, ""reason"": ""Resource location""}},
        {{""gridX"": 15, ""gridY"": 8, ""priority"": 2, ""reason"": ""Survey point""}}
      ],
      ""exclusionZones"": [
        {{""gridX"": 12, ""gridY"": 6, ""reason"": ""Hazardous terrain""}}
      ]
    }},
    {{
      ""robotName"": ""Drone"",
      ""waypoints"": [
        {{""gridX"": 20, ""gridY"": 10, ""priority"": 1, ""reason"": ""Aerial survey point""}}
      ],
      ""exclusionZones"": [
        {{""gridX"": 10, ""gridY"": 5, ""reason"": ""Rover territory - avoid collision""}}
      ]
    }}
  ]
}}";
		}
		else
		{
			return $@"You are a strategic exploration planner for an autonomous robot.

Your role is NOT to generate a complete path (A* pathfinding will handle that automatically).
Instead, provide STRATEGIC GUIDANCE:

1. **Waypoints**: Mandatory checkpoints the robot must visit (e.g., high-value targets, key locations)
2. **Exclusion Zones**: Tiles to completely avoid during pathfinding (e.g., dangerous areas, inefficient routes)

The A* algorithm will automatically find the optimal path connecting these waypoints while avoiding exclusion zones.

**CRITICAL LIFT/DROP OPERATION RULES:**
- If you see annotations containing ""lift"", ""pick up"", ""carry"", ""transport"" a rover/robot: the drone must LIFT the ground robot
- If you see annotations containing ""drop"", ""release"", ""place"", ""deposit"" a rover/robot: the drone must DROP the ground robot
- When lifting/dropping is detected:
  1. The waypoint where lifting happens MUST have priority 1 and reason ""LIFT""
  2. The waypoint where dropping happens MUST have priority 2 and reason ""DROP""
  3. Do NOT add additional exploration waypoints - only LIFT and DROP
  4. Do NOT improvise extra exploration unless explicitly requested
- The system uses these exact keywords to trigger lift/drop operations - do not paraphrase them
- Example: If user writes ""lift rover from here"" at (17,4) and ""to here"" at (20,4), respond:
  waypoints: [{{gridX:17, gridY:4, priority:1, reason:""LIFT""}}, {{gridX:20, gridY:4, priority:2, reason:""DROP""}}]

Current painted path shows the general exploration area:
{pathJson}

Consider:
- FIRST: Check all annotations for lift/drop operations - these take absolute priority
- Which tiles are CRITICAL checkpoints that must be visited?
- Are there tiles that should be AVOIDED entirely?
- Prioritize waypoints (lower priority number = visit first)

**CREATIVE EXPLORATION PATTERNS:**
When users provide creative/exploratory annotations (e.g., ""form a flower"", ""draw a circle"", ""make a spiral"", ""write HAT"", ""explore in a star pattern""):
- Generate waypoints that create the requested shape/pattern
- Use 8-15 waypoints to form recognizable shapes (flowers need ~8-12 points, text needs ~10-20)
- For flowers: create a central point + petal points radiating outward (typically 5-8 petals)
- For text: trace the letter outlines with waypoints (e.g., ""H"" = vertical left line, horizontal middle, vertical right line)
- For geometric shapes (circles, squares, stars): place waypoints along the perimeter
- For spiral patterns: gradually increase radius while rotating around center
- Space waypoints 2-4 tiles apart for smooth, recognizable shapes
- Be creative and proactive - interpret vague requests generously and generate interesting patterns
- If a single tile is marked with creative intent, use it as the CENTER of your pattern

Respond with ONLY a JSON object (no markdown, no extra text):
{{
  ""success"": true,
  ""message"": ""Strategic plan generated"",
  ""reasoning"": ""Explanation of your strategic decisions"",
  ""strategicPlan"": {{
    ""robotName"": ""Rover"",
    ""waypoints"": [
      {{""gridX"": 10, ""gridY"": 5, ""priority"": 1, ""reason"": ""Resource location""}},
      {{""gridX"": 15, ""gridY"": 8, ""priority"": 2, ""reason"": ""Survey checkpoint""}}
    ],
    ""exclusionZones"": [
      {{""gridX"": 12, ""gridY"": 6, ""reason"": ""Hazardous area""}}
    ]
  }}
}}";
		}
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
