using Godot;
using System;
using System.Collections.Generic;
using Game.Component;
using Game.Building;
using Game.Manager;

namespace Game.Building;

public partial class Rake : Control
{
	// === Rake State ===
	public enum RakeOrientation { Horizontal, Vertical }
	public enum RakeState { InDisplay, PickedUp, Placed, Pressed }
	
	public RakeOrientation Orientation { get; private set; } = RakeOrientation.Horizontal;
	public RakeState State { get; private set; } = RakeState.InDisplay;
	public int RakeDimension { get; private set; } = 1; // Number of tiles the rake spans
	public bool IsMidAir { get; private set; } = true; // true when picked up/hovering
	
	// === Visual Components ===
	[Export] private Color rakeColorPickedUp = new Color(0.8f, 0.8f, 0.8f, 0.7f);
	[Export] private Color rakeColorPlaced = new Color(0.5f, 0.5f, 1.0f, 0.5f);
	[Export] private Color rakeColorPressed = new Color(1.0f, 0.5f, 0.5f, 0.8f);
	[Export] private float tileSize = 64f;
	
	// Node references
	private Sprite2D sprite;
	
	// Sprite textures
	private Texture2D pressedTexture;
	private Texture2D defaultTexture;
	
	// === Grid and Position ===
	public Vector2I GridPosition { get; private set; }
	private BuildingComponent associatedRobot;
	
	// === Movement tracking for push detection ===
	private Vector2 lastGlobalPosition;
	private bool isFirstPositionSet = false;
	private HashSet<Vector2I> previousGridPositions = new();
	
	// === BuildingManager reference ===
	private BuildingManager buildingManager;
	
	// === Affected Tiles ===
	private List<PaintedTile> affectedPaintedTiles = new();
	
	public override void _Ready()
	{
		// Get node references from scene tree
		sprite = GetNode<Sprite2D>("Sprite2D");
		
		// Load textures
		pressedTexture = GD.Load<Texture2D>("res://assets/buildings/rake.png");
		defaultTexture = sprite.Texture; // Save the default texture
		
		// Initialize the rake with default size (1 tile)
		CustomMinimumSize = new Vector2(tileSize, tileSize);
		Size = new Vector2(tileSize, tileSize);
		CallDeferred("PutInDisplay");
	}
	
	/// <summary>
	/// Sets the BuildingManager reference (must be called after instantiation)
	/// </summary>
	public void SetBuildingManager(BuildingManager manager)
	{
		buildingManager = manager;
	}
	
	// === Public API ===
	
	/// <summary>
	/// Changes the size of the rake (number of tiles it spans)
	/// </summary>
	public void SetSize(int newSize)
	{
		if (newSize < 1) newSize = 1;
		if (newSize > 10) newSize = 10; // Max size limit
		
		// Store old size to calculate offset
		Vector2 oldSize = Size;
		
		RakeDimension = newSize;
		
		// Resize the Control node and sprite to match the rake dimension
		if (Orientation == RakeOrientation.Horizontal)
		{
			// Horizontal: width = RakeDimension tiles, height = 1 tile
			CustomMinimumSize = new Vector2(RakeDimension * tileSize, tileSize);
			Size = new Vector2(RakeDimension * tileSize, tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(RakeDimension, 1);
				// When sprite is scaled, it grows from its center point
				// Position it so the scaled sprite fills the control properly
				//sprite.Position = new Vector2(tileSize / 2, tileSize / 2);
				sprite.Offset = new Vector2(tileSize / 2.5f, tileSize / 2.5f);
			}
			
			// Adjust position to keep centered (move left by half the size difference)
			GlobalPosition = new Vector2(GlobalPosition.X + (Size.X - oldSize.X) / 2, GlobalPosition.Y);
		}
		else // Vertical
		{
			// Vertical: width = 1 tile, height = RakeDimension tiles
			CustomMinimumSize = new Vector2(tileSize, RakeDimension * tileSize);
			Size = new Vector2(tileSize, RakeDimension * tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(1, RakeDimension);
				// When sprite is scaled, it grows from its center point
				// Position it so the scaled sprite fills the control properly
				sprite.Position = new Vector2(tileSize / 2, tileSize / 2);
			}
			
			// Adjust position to keep centered (move up by half the size difference)
			GlobalPosition = new Vector2(GlobalPosition.X, GlobalPosition.Y - (Size.Y - oldSize.Y) / 2);
		}
		
		if (State == RakeState.Placed || State == RakeState.Pressed)
		{
			UpdateAffectedTiles();
		}
	}
	
	/// <summary>
	/// Toggles orientation between Horizontal and Vertical
	/// </summary>
	public void ToggleOrientation()
	{
		Orientation = Orientation == RakeOrientation.Horizontal 
			? RakeOrientation.Vertical 
			: RakeOrientation.Horizontal;
		
		// Store the center position before resizing
		Vector2 centerPos = GlobalPosition + Size / 2;
		
		// Resize the Control node and sprite based on orientation
		if (Orientation == RakeOrientation.Vertical)
		{
			CustomMinimumSize = new Vector2(tileSize, RakeDimension * tileSize);
			Size = new Vector2(tileSize, RakeDimension * tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(1, RakeDimension);
				sprite.Position = new Vector2(tileSize / 2, tileSize / 2);
			}
		}
		else
		{
			CustomMinimumSize = new Vector2(RakeDimension * tileSize, tileSize);
			Size = new Vector2(RakeDimension * tileSize, tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(RakeDimension, 1);
				sprite.Position = new Vector2(tileSize / 2, tileSize / 2);
			}
		}
		
		// Restore center position
		GlobalPosition = centerPos - Size / 2;
		
		if (State == RakeState.Placed || State == RakeState.Pressed)
		{
			UpdateAffectedTiles();
		}
	}
	
	/// <summary>
	/// Sets the orientation explicitly
	/// </summary>
	public void SetOrientation(RakeOrientation newOrientation)
	{
		Orientation = newOrientation;
		
		// Resize the Control node and sprite based on orientation
		if (Orientation == RakeOrientation.Vertical)
		{
			CustomMinimumSize = new Vector2(tileSize, RakeDimension * tileSize);
			Size = new Vector2(tileSize, RakeDimension * tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(1, RakeDimension);
				sprite.Position = new Vector2(tileSize / 2, tileSize / 2);
			}
		}
		else
		{
			CustomMinimumSize = new Vector2(RakeDimension * tileSize, tileSize);
			Size = new Vector2(RakeDimension * tileSize, tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(RakeDimension, 1);
				sprite.Position = new Vector2(tileSize / 2, tileSize / 2);
			}
		}
		
		if (State == RakeState.Placed || State == RakeState.Pressed)
		{
			UpdateAffectedTiles();
		}
	}
	
	/// <summary>
	/// Picks up the rake (makes it mid-air and follows cursor/robot)
	/// </summary>
	public void PickUp()
	{
		State = RakeState.PickedUp;
		IsMidAir = true;
		isFirstPositionSet = false;

		if (sprite != null && defaultTexture != null)
		{
			sprite.Texture = defaultTexture;
		}
		
		// Don't reset size - preserve current RakeDimension and Orientation
		// Just ensure the visual representation matches the current state
		if (Orientation == RakeOrientation.Horizontal)
		{
			CustomMinimumSize = new Vector2(RakeDimension * tileSize, tileSize);
			Size = new Vector2(RakeDimension * tileSize, tileSize);
			if (sprite != null && RakeDimension == 1)
			{
				sprite.Scale = new Vector2(RakeDimension, 1);
				sprite.Position = new Vector2(64, 64);
				sprite.Offset = new Vector2(0,0);
			}
		}
		else // Vertical
		{
			CustomMinimumSize = new Vector2(tileSize, RakeDimension * tileSize);
			Size = new Vector2(tileSize, RakeDimension * tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(1, RakeDimension);
				sprite.Position = new Vector2(64,64);
			}
		}
	}

	public void PutInDisplay()
	{
		State = RakeState.InDisplay;
		IsMidAir = true;
		isFirstPositionSet = false;

		if (sprite != null && defaultTexture != null)
		{
			sprite.Texture = defaultTexture;
		}
		
		// Don't reset size - preserve current RakeDimension and Orientation
		// Just ensure the visual representation matches the current state
		if (Orientation == RakeOrientation.Horizontal)
		{
			CustomMinimumSize = new Vector2(RakeDimension * tileSize, tileSize);
			Size = new Vector2(RakeDimension * tileSize, tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(RakeDimension, 1);
				sprite.Position = new Vector2(0,0);
			}
		}
		else // Vertical
		{
			CustomMinimumSize = new Vector2(tileSize, RakeDimension * tileSize);
			Size = new Vector2(tileSize, RakeDimension * tileSize);
			if (sprite != null)
			{
				sprite.Scale = new Vector2(1, RakeDimension);
				sprite.Position = new Vector2(0,0);
			}
		}
	}
	
	/// <summary>
	/// Places the rake at a specific grid position (on ground but not pressed)
	/// </summary>
	public void Place(Vector2I gridPosition)
	{
		State = RakeState.Placed;
		IsMidAir = false;
		GridPosition = gridPosition;
		GlobalPosition = new Vector2(gridPosition.X * tileSize, gridPosition.Y * tileSize);
	}
	
	/// <summary>
	/// Presses the rake down (activates dragging effect on tiles)
	/// </summary>
	public void Press()
	{
		State = RakeState.Pressed;
		
		// Store the initial position when pressed
		lastGlobalPosition = GlobalPosition;
		isFirstPositionSet = true;
		previousGridPositions.Clear();
		
		// Store initial grid positions
		var currentGridPositions = GetRakeGridPositionsFromPosition(GlobalPosition);
		foreach (var pos in currentGridPositions)
		{
			previousGridPositions.Add(pos);
		}
		
		// Change sprite to pressed texture
		if (sprite != null && pressedTexture != null)
		{
			sprite.Texture = pressedTexture;
		}
		
		// Update which tiles are initially under the rake
		UpdateAffectedTiles();
	}
	
	/// <summary>
	/// Releases the press (returns to picked up state)
	/// </summary>
	public void Release()
	{
		if (State != RakeState.Pressed)
		{
			return;
		}
		
		State = RakeState.PickedUp;
		isFirstPositionSet = false;
		previousGridPositions.Clear();
		
		// Restore default sprite
		if (sprite != null && defaultTexture != null)
		{
			sprite.Texture = defaultTexture;
		}
	}
	
	/// <summary>
	/// Moves the rake in a direction while pressed (drags tiles)
	/// </summary>
	public void DragInDirection(Vector2I direction)
	{
		if (State != RakeState.Pressed)
		{
			GD.PrintErr("Rake must be pressed to drag tiles!");
			return;
		}
		
		// Move affected tiles
		foreach (var paintedTile in affectedPaintedTiles)
		{
			Vector2I newGridPos = paintedTile.GridPosition + direction;
			paintedTile.GridPosition = newGridPos;
			paintedTile.GlobalPosition = new Vector2(newGridPos.X * tileSize, newGridPos.Y * tileSize);
		}
		
		// Move rake itself
		GridPosition += direction;
		GlobalPosition = new Vector2(GridPosition.X * tileSize, GridPosition.Y * tileSize);
		
		// Update which tiles are affected
		UpdateAffectedTiles();
	}
	
	/// <summary>
	/// Gets the list of painted tiles currently affected by the rake
	/// </summary>
	public List<PaintedTile> GetAffectedTiles()
	{
		return new List<PaintedTile>(affectedPaintedTiles);
	}
	
	/// <summary>
	/// Sets the global position with the rake centered on the given point
	/// </summary>
	public void SetCenteredPosition(Vector2 centerPosition)
	{
		// Offset by half the size so the center is at the target position
		Vector2 newPosition = centerPosition - Size / 2;
		
		// If rake is pressed, detect movement and push tiles
		if (State == RakeState.Pressed && isFirstPositionSet)
		{
			DetectAndPushTiles(newPosition);
		}
		
		GlobalPosition = newPosition;
		lastGlobalPosition = newPosition;
		
		if (!isFirstPositionSet)
		{
			isFirstPositionSet = true;
		}
	}
	
	/// <summary>
	/// Detects collision with painted tiles and pushes them in the movement direction
	/// </summary>
	private void DetectAndPushTiles(Vector2 newPosition)
	{
		if (buildingManager == null)
		{
			GD.PrintErr("BuildingManager reference not set on Rake!");
			return;
		}
		
		// Get current and new grid positions
		List<Vector2I> currentGridPositions = GetRakeGridPositionsFromPosition(newPosition);
		
		// Find newly entered grid cells (cells we're now in that we weren't in before)
		HashSet<Vector2I> newlyEnteredCells = new();
		foreach (var gridPos in currentGridPositions)
		{
			if (!previousGridPositions.Contains(gridPos))
			{
				newlyEnteredCells.Add(gridPos);
			}
		}
		
		// If no new cells entered, no pushing needed
		if (newlyEnteredCells.Count == 0)
		{
			return;
		}
		
		// Calculate movement direction from position delta
		Vector2 movement = newPosition - lastGlobalPosition;
		
		// Determine push direction based on which axis has more movement
		Vector2I pushDirection = Vector2I.Zero;
		if (Mathf.Abs(movement.X) > Mathf.Abs(movement.Y))
		{
			// Horizontal movement dominates
			pushDirection = movement.X > 0 ? Vector2I.Right : Vector2I.Left;
		}
		else
		{
			// Vertical movement dominates
			pushDirection = movement.Y > 0 ? Vector2I.Down : Vector2I.Up;
		}
		
		// Get all placed rakes (other rakes on the map)
		var allPlacedRakes = buildingManager.GetAllPlacedRakes();
		
		// Find all painted tiles from the BuildingManager
		var allPaintedTiles = buildingManager.GetAllPaintedTiles();
		
		// Check each newly entered cell for rakes or tiles to push
		foreach (var enteredCell in newlyEnteredCells)
		{
			bool rakeFound = false;
			
			// First check if there's a rake at this position
			foreach (var placedRake in allPlacedRakes)
			{
				// Get the grid positions occupied by this placed rake
				var rakeGridPositions = placedRake.GetRakeGridPositionsFromPosition(placedRake.GlobalPosition);
				
				if (rakeGridPositions.Contains(enteredCell))
				{
					// Push the rake
					PushRake(placedRake, pushDirection);
					rakeFound = true;
					break;
				}
			}
			
			// If no rake was found, check for painted tiles
			if (!rakeFound)
			{
				foreach (var paintedTile in allPaintedTiles)
				{
					// Check if painted tile is at this newly entered cell
					if (paintedTile.GridPosition == enteredCell)
					{
						// Push the tile in the movement direction
						PushTile(paintedTile, pushDirection, enteredCell);
						break; // Only one tile per cell
					}
				}
			}
		}
		
		// Update previous grid positions to current ones
		previousGridPositions.Clear();
		foreach (var pos in currentGridPositions)
		{
			previousGridPositions.Add(pos);
		}
	}
	
	/// <summary>
	/// Gets the grid positions currently occupied by the rake based on a given position
	/// </summary>
	public List<Vector2I> GetRakeGridPositionsFromPosition(Vector2 position)
	{
		var positions = new List<Vector2I>();
		
		// The rake's position is its top-left corner, and Size tells us its dimensions
		// We need to find all grid cells that the rake overlaps
		
		if (Orientation == RakeOrientation.Horizontal)
		{
			// For horizontal rake: width = RakeDimension tiles, height = 1 tile
			// Get the Y grid position from the center of the rake vertically
			int gridY = Mathf.FloorToInt((position.Y + Size.Y / 2) / tileSize);
			
			// Get all X grid positions from left to right edge of the rake
			int startGridX = Mathf.FloorToInt(position.X / tileSize);
			int endGridX = Mathf.FloorToInt((position.X + Size.X - 1) / tileSize);
			
			for (int x = startGridX; x <= endGridX; x++)
			{
				positions.Add(new Vector2I(x, gridY));
			}
		}
		else // Vertical
		{
			// For vertical rake: width = 1 tile, height = RakeDimension tiles
			// Get the X grid position from the center of the rake horizontally
			int gridX = Mathf.FloorToInt((position.X + Size.X / 2) / tileSize);
			
			// Get all Y grid positions from top to bottom edge of the rake
			int startGridY = Mathf.FloorToInt(position.Y / tileSize);
			int endGridY = Mathf.FloorToInt((position.Y + Size.Y - 1) / tileSize);
			
			for (int y = startGridY; y <= endGridY; y++)
			{
				positions.Add(new Vector2I(gridX, y));
			}
		}
		
		return positions;
	}
	
	/// <summary>
	/// Pushes another rake in the specified direction
	/// </summary>
	private void PushRake(Rake rake, Vector2I direction)
	{
		// Calculate the rake's center position
		Vector2 rakeCenter = rake.GlobalPosition + rake.Size / 2;
		
		// Calculate new center position (one tile in the direction)
		Vector2 newCenter = rakeCenter + new Vector2(direction.X * tileSize, direction.Y * tileSize);
		
		// Get the grid positions the rake will occupy after moving
		List<Vector2I> newRakePositions = rake.GetRakeGridPositionsFromPosition(newCenter - rake.Size / 2);
		
		// First, check if there are any other rakes in the way and push them recursively
		var allPlacedRakes = buildingManager.GetAllPlacedRakes();
		foreach (var newPos in newRakePositions)
		{
			foreach (var otherRake in allPlacedRakes)
			{
				if (otherRake == rake) continue; // Skip the rake we're currently pushing
				
				var otherRakePositions = otherRake.GetRakeGridPositionsFromPosition(otherRake.GlobalPosition);
				if (otherRakePositions.Contains(newPos))
				{
					// Recursively push the rake that's in the way
					PushRake(otherRake, direction);
					break;
				}
			}
		}
		
		// Then check if any of these positions has a painted tile
		var allPaintedTiles = buildingManager.GetAllPaintedTiles();
		foreach (var newPos in newRakePositions)
		{
			var tileAtPosition = allPaintedTiles.Find(t => t.GridPosition == newPos);
			if (tileAtPosition != null)
			{
				// If the rake is in Pressed state, push the tile
				if (rake.State == RakeState.Pressed)
				{
					PushTile(tileAtPosition, direction, newPos);
				}
			}
		}
		
		// Finally, move this rake to the new position
		rake.GlobalPosition = newCenter - rake.Size / 2;
		
		//GD.Print($"Pushed {(rake.State == RakeState.Pressed ? "pressed" : "mid-air")} rake in direction {direction}");
	}
	
	/// <summary>
	/// Handles a painted tile that the rake passes over
	/// - Middle tiles: cleared (erased)
	/// - End tile (destination): pushed in the direction of movement
	/// - Start tile: cleared without recalculation
	/// </summary>
	private void PushTile(PaintedTile tile, Vector2I direction, Vector2I collisionPos)
	{
		// Get the associated robot
		var associatedRobot = tile.AssociatedRobot;
		if (associatedRobot == null)
		{
			GD.PrintErr("Cannot remove tile: no associated robot");
			return;
		}
		
		Vector2I tilePosition = tile.GridPosition;
		
		// Get all tiles for this robot
		var robotTiles = associatedRobot.paintedTiles;
		int tileIndex = robotTiles.IndexOf(tile);
		
		// Check if this is the last tile (destination)
		if (tileIndex == robotTiles.Count - 1)
		{
			// This is the destination tile - PUSH it directly without using A*
			Vector2I oldGridPos = tile.GridPosition;
			Vector2I newGridPos = oldGridPos + direction;
			
			// Check if the new position is already in the current path
			int existingTileIndex = -1;
			for (int i = 0; i < robotTiles.Count - 1; i++) // Don't check the last tile (ourselves)
			{
				if (robotTiles[i].GridPosition == newGridPos)
				{
					existingTileIndex = i;
					break;
				}
			}
			
			if (existingTileIndex >= 0)
			{
				// The new position is already part of the path - shorten the path to that point
				// Remove all tiles after the existing tile (including the destination we're pushing)
				for (int i = robotTiles.Count - 1; i > existingTileIndex; i--)
				{
					var tileToRemove = robotTiles[i];
					robotTiles.RemoveAt(i);
					var allPaintedTiles = buildingManager.GetAllPaintedTiles();
					allPaintedTiles.Remove(tileToRemove);
					tileToRemove.QueueFree();
				}
				
				GD.Print($"Rake shortened path by pushing destination from {oldGridPos} to existing tile at {newGridPos}");
				return;
			}
			
			// Check if there's a different painted tile at the new position (not in current path)
			var allPaintedTiles2 = buildingManager.GetAllPaintedTiles();
			var existingTile = allPaintedTiles2.Find(t => t.GridPosition == newGridPos && t != tile);
			
			if (existingTile != null)
			{
				// Recursively handle the tile that's in the way FIRST
				PushTile(existingTile, direction, newGridPos);
			}
			
			// Simply move the destination tile to the new position directly
			tile.GridPosition = newGridPos;
			tile.GlobalPosition = new Vector2(newGridPos.X * tileSize, newGridPos.Y * tileSize);
			
			// Add a new tile to extend the path directly in the push direction
			// Create the new tile at the old position to maintain path continuity
			var paintedTileScene = GD.Load<PackedScene>("res://scenes/component/PaintedTile.tscn");
			var newTile = paintedTileScene.Instantiate<PaintedTile>();
			newTile.GlobalPosition = new Vector2(oldGridPos.X * tileSize, oldGridPos.Y * tileSize);
			buildingManager.AddChild(newTile);
			
			newTile.AssociatedRobot = associatedRobot;
			newTile.GridPosition = oldGridPos;
			
			if (associatedRobot.BuildingResource.IsAerial)
				newTile.SetColor(Colors.Cyan);
			else
				newTile.SetColor(Colors.Yellow);
			
			// Insert the new tile just before the destination tile
			robotTiles.Insert(robotTiles.Count - 1, newTile);
			var allTiles = buildingManager.GetAllPaintedTiles();
			allTiles.Add(newTile);
			
			GD.Print($"Rake pushed destination tile from {oldGridPos} to {newGridPos} (direct push)");
		}
		// Check if this is a middle tile (not first or last)
		else if (tileIndex > 0 && tileIndex < robotTiles.Count - 1)
		{
			// Get the start and end of the path
			Vector2I startPos = robotTiles[0].GridPosition;
			Vector2I endPos = robotTiles[robotTiles.Count - 1].GridPosition;
			
			// Remove the tile from robot's list and destroy it
			robotTiles.Remove(tile);
			var allPaintedTiles = buildingManager.GetAllPaintedTiles();
			allPaintedTiles.Remove(tile);
			tile.QueueFree();
			
			// Use the erase logic to recalculate the path avoiding this position
			buildingManager.CallDeferred("RecalculatePathAfterErase", associatedRobot, tilePosition, startPos, endPos);
			
			GD.Print($"Rake cleared tile at {tilePosition}, recalculating path");
		}
		else if (tileIndex == 0)
		{
			// If it's the first tile, recalculate from robot position
			if (robotTiles.Count > 1)
			{
				// Get robot's current position
				Vector2I robotPos = associatedRobot.GetGridCellPosition();
				// The new start should be the second tile
				Vector2I newStartPos = robotTiles[1].GridPosition;
				Vector2I endPos = robotTiles[robotTiles.Count - 1].GridPosition;
				
				// Remove the first tile
				robotTiles.Remove(tile);
				var allPaintedTiles = buildingManager.GetAllPaintedTiles();
				allPaintedTiles.Remove(tile);
				tile.QueueFree();
				
				// Use the erase logic to recalculate the path from robot to remaining tiles
				buildingManager.CallDeferred("RecalculatePathAfterErase", associatedRobot, tilePosition, robotPos, endPos);
				
				GD.Print($"Rake cleared start tile at {tilePosition}, recalculating from robot position");
			}
			else
			{
				// Only one tile, just remove it
				robotTiles.Remove(tile);
				var allPaintedTiles = buildingManager.GetAllPaintedTiles();
				allPaintedTiles.Remove(tile);
				tile.QueueFree();
				
				GD.Print($"Rake cleared only tile at {tilePosition}");
			}
		}
		else
		{
			// Shouldn't reach here, but handle as fallback
			robotTiles.Remove(tile);
			var allPaintedTiles = buildingManager.GetAllPaintedTiles();
			allPaintedTiles.Remove(tile);
			tile.QueueFree();
			
			GD.Print($"Rake cleared tile at {tilePosition}");
		}
	}
	
	/// <summary>
	/// Deferred method to push tile through BuildingManager (avoids modifying collection during iteration)
	/// </summary>
	private void DeferredPushTile(Vector2I oldPos, Vector2I newPos, string annotation)
	{
		// This method is no longer used but kept for compatibility
	}
	
	/// <summary>
	/// Gets the grid positions covered by the rake
	/// </summary>
	public List<Vector2I> GetCoveredGridPositions()
	{
		var positions = new List<Vector2I>();
		
		if (Orientation == RakeOrientation.Horizontal)
		{
			for (int i = 0; i < RakeDimension; i++)
			{
				positions.Add(new Vector2I(GridPosition.X + i, GridPosition.Y));
			}
		}
		else // Vertical
		{
			for (int i = 0; i < RakeDimension; i++)
			{
				positions.Add(new Vector2I(GridPosition.X, GridPosition.Y + i));
			}
		}
		
		return positions;
	}
	
	// === Private Methods ===
	
	private void UpdateAffectedTiles()
	{
		affectedPaintedTiles.Clear();
		
		if (State == RakeState.PickedUp)
		{
			return;
		}
		
		// Find painted tiles that overlap with rake positions
		var coveredPositions = GetCoveredGridPositions();
		var allPaintedTiles = GetTree().GetNodesInGroup("painted_tiles");
		
		foreach (var node in allPaintedTiles)
		{
			if (node is PaintedTile paintedTile)
			{
				if (coveredPositions.Contains(paintedTile.GridPosition))
				{
					affectedPaintedTiles.Add(paintedTile);
				}
			}
		}
	}
	
	public override void _Process(double delta)
	{
	}
}
