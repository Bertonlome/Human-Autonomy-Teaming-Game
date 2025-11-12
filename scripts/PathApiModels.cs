using Godot;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Game.API;

/// <summary>
/// Data Transfer Object for a single painted tile in the path
/// </summary>
public class PaintedTileDto
{
	[JsonPropertyName("tileNumber")]
	public int TileNumber { get; set; }
	
	[JsonPropertyName("gridX")]
	public int GridX { get; set; }
	
	[JsonPropertyName("gridY")]
	public int GridY { get; set; }
	
	[JsonPropertyName("annotation")]
	public string Annotation { get; set; }
	
	[JsonPropertyName("robotName")]
	public string RobotName { get; set; }
	
	[JsonPropertyName("isAerial")]
	public bool IsAerial { get; set; }
}

/// <summary>
/// Complete path data for API communication with LLM
/// </summary>
public class PathDataDto
{
	[JsonPropertyName("robotName")]
	public string RobotName { get; set; }
	
	[JsonPropertyName("isAerial")]
	public bool IsAerial { get; set; }
	
	[JsonPropertyName("tiles")]
	public List<PaintedTileDto> Tiles { get; set; } = new();
	
	[JsonPropertyName("totalTiles")]
	public int TotalTiles { get; set; }
}

/// <summary>
/// Request sent to LLM containing game state and path
/// </summary>
public class PathApiRequest
{
	[JsonPropertyName("currentPath")]
	public PathDataDto CurrentPath { get; set; }
	
	[JsonPropertyName("mapWidth")]
	public int MapWidth { get; set; }
	
	[JsonPropertyName("mapHeight")]
	public int MapHeight { get; set; }
	
	[JsonPropertyName("robotStartX")]
	public int RobotStartX { get; set; }
	
	[JsonPropertyName("robotStartY")]
	public int RobotStartY { get; set; }
	
	[JsonPropertyName("context")]
	public string Context { get; set; }
}

/// <summary>
/// Response from LLM with optimized/alternative path
/// </summary>
public class PathApiResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }
	
	[JsonPropertyName("message")]
	public string Message { get; set; }
	
	[JsonPropertyName("suggestedPath")]
	public PathDataDto SuggestedPath { get; set; }
	
	[JsonPropertyName("reasoning")]
	public string Reasoning { get; set; }
}
