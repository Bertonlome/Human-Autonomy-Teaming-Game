using Godot;
using Game.Component;

namespace Game.Building;

/// <summary>
/// Represents a contextual tile in the grid that provides surrounding information
/// for painted tiles, helping LLMs understand the broader context of a path.
/// </summary>
public class ContextTile
{
	/// <summary>
	/// Grid position of this context tile
	/// </summary>
	public Vector2I GridPosition { get; set; }
	
	/// <summary>
	/// Whether this tile is reachable by the associated robot
	/// </summary>
	public bool IsReachable { get; set; }
	
	/// <summary>
	/// Whether this tile is part of the user's painted path
	/// </summary>
	public bool IsPaintedTile { get; set; }
	
	/// <summary>
	/// The robot that would need to reach this tile (for reachability calculation)
	/// </summary>
	public BuildingComponent AssociatedRobot { get; set; }
	
	/// <summary>
	/// Optional reference to the painted tile if this context tile corresponds to one
	/// </summary>
	public PaintedTile PaintedTileReference { get; set; }

	public ContextTile(Vector2I gridPosition, BuildingComponent robot)
	{
		GridPosition = gridPosition;
		AssociatedRobot = robot;
		IsReachable = false;
		IsPaintedTile = false;
		PaintedTileReference = null;
	}
}
