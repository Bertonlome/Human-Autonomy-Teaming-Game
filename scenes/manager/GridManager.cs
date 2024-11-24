using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Game.Autoload;
using Game.Component;
using Game.Level.Util;
using Godot;

namespace Game.Manager;

public partial class GridManager : Node
{
	private const string IS_BUILDABLE = "is_buildable";
	private const string IS_WOOD = "is_wood";
	private const string IS_IGNORED = "is_ignored";
	private const string IS_ROUGH_TERRAIN = "is_rough_terrain";

	[Signal]
	public delegate void ResourceTilesUpdatedEventHandler(int collectedTiles);
	[Signal]
	public delegate void DiscoveredTileUpdatedEventHandler(Vector2I tile, string type);
	[Signal]
	public delegate void GridStateUpdatedEventHandler();
	[Signal]
	public delegate void GroundRobotTouchingMonolithEventHandler();
	[Signal]
	public delegate void AerialRobotHasVisionOfMonolithEventHandler();

	private HashSet<Vector2I> allTilesBuildableOnTheMap = new();
	private HashSet<Vector2I> validBuildableTiles = new();
	private HashSet<Vector2I> validBuildableAttackTiles = new();
	private HashSet<Vector2I> allTilesInBuildingRadius = new();
	private HashSet<Vector2I> collectedResourceTiles = new();
	private HashSet<Vector2I> discoveredElementsTiles = new();
	private HashSet<Vector2I> occupiedTiles = new();
	private HashSet<Vector2I> dangerOccupiedTiles = new();
	private HashSet<Vector2I> attackTiles = new();
	private HashSet<Vector2I> baseAntennaCoveredTiles = new();
	private HashSet<Vector2I> baseProximityTiles = new();
	private HashSet<Vector2I> monolithTiles = new();

	private Monolith monolith;
	public Vector2I monolithPosition = new();

	private List<Vector2I> allTilesBaseLayer;

	[Export]
	private TileMapLayer highlightTilemapLayer;
	[Export]
	private TileMapLayer baseTerrainTilemapLayer;
	[Export]
	private GravitationalAnomalyMap gravitationalAnomalyMap;

	private List<TileMapLayer> allTilemapLayers = new();
	private Dictionary<TileMapLayer, ElevationLayer> tileMapLayerToElevationLayer = new();
	private Dictionary<BuildingComponent, HashSet<Vector2I>> buildingToBuildableTiles = new();
	private Dictionary<BuildingComponent, HashSet<Vector2I>> dangerBuildingToTiles = new();
	private Dictionary<BuildingComponent, HashSet<Vector2I>> attackBuildingToTiles = new();
	private Dictionary<BuildingComponent, HashSet<Vector2I>> buildingStuckToTiles = new();
	private Dictionary<Vector2I, int> positionToGravitationAnomaly = new();

	public override void _Ready()
	{
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingPlaced, Callable.From<BuildingComponent>(OnBuildingPlaced));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingDestroyed, Callable.From<BuildingComponent>(OnBuildingDestroyed));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingEnabled, Callable.From<BuildingComponent>(OnBuildingEnabled));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingDisabled, Callable.From<BuildingComponent>(OnBuildingDisabled));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingMoved, Callable.From<BuildingComponent>(OnBuildingMoved));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingStuck, Callable.From<BuildingComponent>(OnBuildingStuck));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingUnStuck, Callable.From<BuildingComponent>(OnBuildingUnStuck));

		monolith = GetNode<Monolith>("%Monolith");
		SetMonolithPosition(ConvertWorldPositionToTilePosition(monolith.GlobalPosition));

		allTilemapLayers = GetAllTilemapLayers(baseTerrainTilemapLayer);
		allTilesBuildableOnTheMap = GetAllBuildableBaseTerrainTiles(baseTerrainTilemapLayer).ToHashSet();
		allTilesBaseLayer = baseTerrainTilemapLayer.GetUsedCells().ToList();
		MapTileMapLayersToElevationLayers();
	}

	public void SetMonolithPosition(Vector2I position)
	{
		monolithPosition = position;
		monolithTiles.Add(position);
		//SetGravitationAnomalyGradient(position);
	}

	public (TileMapLayer, bool) GetTileCustomData(Vector2I tilePosition, string dataName)
	{
		foreach (var layer in allTilemapLayers)
		{
			var customData = layer.GetCellTileData(tilePosition);
			if (customData == null || (bool)customData.GetCustomData(IS_IGNORED)) continue;
			return (layer, (bool)customData.GetCustomData(dataName));
		}
		return (null, false);
	}

	public string GetTileDiscoveredElements(Vector2I tilePosition)
	{
		foreach (var layer in allTilemapLayers)
		{
			var customData = layer.GetCellTileData(tilePosition);
			if (customData == null || (bool)customData.GetCustomData(IS_IGNORED)) continue;
			return (string)customData.GetCustomData("landscape_type");
		}
		return null;
	}

	public bool IsTilePositionInAnyBuildingRadius(Vector2I tilePosition)
	{
		return allTilesInBuildingRadius.Contains(tilePosition);
	}

	public bool IsTileAreaBuildable(Rect2I tileArea, bool isAttackTiles = false, bool isBase = false)
	{
		IEnumerable<Vector2I> tileSetToCheck;
		var tiles = tileArea.ToTiles();
		if (tiles.Count == 0) return false;

		(TileMapLayer firstTileMapLayer, _) = GetTileCustomData(tiles[0], IS_BUILDABLE);
		var targetElevationLayer = firstTileMapLayer != null ? tileMapLayerToElevationLayer[firstTileMapLayer] : null;

		if(BuildingManager.selectedBuildingComponent != null)
		{
			tileSetToCheck = GetBuildableTileSet(isAttackTiles).Except(BuildingManager.selectedBuildingComponent.GetOccupiedCellPositions());
		}
		else if (isBase)
		{
			tileSetToCheck = allTilesBuildableOnTheMap;
		}
		else
		{
			tileSetToCheck = GetBuildableTileSet(isAttackTiles);
		}
		if (isAttackTiles)
		{
			tileSetToCheck = tileSetToCheck.Except(occupiedTiles).ToHashSet();
		}

		return tiles.All((tilePosition) =>
		{
			(TileMapLayer tileMapLayer, bool isBuildable) = GetTileCustomData(tilePosition, IS_BUILDABLE);
			var elevationLayer = tileMapLayer != null ? tileMapLayerToElevationLayer[tileMapLayer] : null;
			return isBuildable && tileSetToCheck.Contains(tilePosition) && elevationLayer == targetElevationLayer;
		});
	}

	public bool IsBuildingMovable(BuildingComponent buildingComponent, Rect2I originArea, Rect2I destinationArea)
	{
		IEnumerable<Vector2I> tileSetToCheckGround;
		IEnumerable<Vector2I> tileSetToCheckAerial;

		var tilesDestination = destinationArea.ToTiles();
		var tilesOrigin = originArea.ToTiles();

		if (tilesDestination.Count == 0) return false;

		(TileMapLayer firstTileMapLayer, _) = GetTileCustomData(tilesDestination[0], IS_ROUGH_TERRAIN);
		var targetElevationLayer = firstTileMapLayer != null ? tileMapLayerToElevationLayer[firstTileMapLayer] : null;

		(firstTileMapLayer, _) = GetTileCustomData(tilesOrigin[0], IS_ROUGH_TERRAIN);
		var OriginElevationLayer = firstTileMapLayer != null ? tileMapLayerToElevationLayer[firstTileMapLayer] : null;

		var transitionTile = originArea.ToTiles().Intersect(destinationArea.ToTiles()).ToHashSet();

		//tileSetToCheckGround = GetBuildableTileSet().Union(transitionTile).ToHashSet(); //Buildable takes into account rocks and plants
		tileSetToCheckGround = occupiedTiles.ToHashSet();
		tileSetToCheckAerial = occupiedTiles.ToHashSet(); //UAV can fly over rocks and plants

		return tilesDestination.All((tilePosition) =>
		{
			(TileMapLayer tileMapLayer, bool isRoulable) = GetTileCustomData(tilePosition, IS_ROUGH_TERRAIN);
			var elevationLayer = tileMapLayer != null ? tileMapLayerToElevationLayer[tileMapLayer] : null;
			(tileMapLayer, bool isWood) = GetTileCustomData(tilePosition, IS_WOOD);
			//(tileMapLayer, bool isRoulable) = GetTileCustomData(tilePosition, IS_ROUGH_TERRAIN);
			
			//Check for ground vehicle
			var check1 = tileSetToCheckGround.Contains(tilePosition) ? false: true;
			var check2 = elevationLayer == targetElevationLayer ? true: false;
			var check3 = OriginElevationLayer == targetElevationLayer ? true: false;
			var check7 = !isRoulable;
			var check8 = !buildingComponent.BuildingResource.IsAerial;
			//Check for aerial vehicle
			var check4 = buildingComponent.BuildingResource.IsAerial;
			var check5 = tileSetToCheckAerial.Contains(tilePosition) ? false: true;
			var check6 = !isWood;
			return (check1 && check2 && check3 && check7 && check8) || (check4 && check5 && check6);
		});
	}

	public bool IsGettingOutOfACoverage(BuildingComponent buildingComponent, Rect2I destinationArea)
	{
		var tilesInRadiusofRobotArrival = GetValidTilesInRadius(destinationArea, buildingComponent.BuildingResource.BuildableRadius);
		
		buildingToBuildableTiles.Remove(buildingComponent);

		var allTilesFromDictionary = new HashSet<Vector2I>();
		foreach(var tileSet in buildingToBuildableTiles.Values)
		{
			allTilesFromDictionary.UnionWith(tileSet);
		}

		var anyTilesInRadius = tilesInRadiusofRobotArrival		
			.Any((tilePosition) => 
			{
				return allTilesFromDictionary.Contains(tilePosition);
			});
	
		bool testfinal = (tilesInRadiusofRobotArrival.Intersect(baseAntennaCoveredTiles).Count() > 0) || anyTilesInRadius == true;

		if((tilesInRadiusofRobotArrival.Intersect(baseAntennaCoveredTiles).Count() > 0) || anyTilesInRadius) return true;
		else return true;
	}

	public void HighlightDangerOccupiedTiles()
	{
		var atlasCoords = new Vector2I(2, 0);
		foreach (var tilePosition in dangerOccupiedTiles)
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, atlasCoords);
		}
	}

	public void HighlightBuildableTiles(bool isAttackTiles = false)
	{
		foreach (var tilePosition in GetValidTileSet())
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, Vector2I.Zero);
		}
	}

	public void HighlightExpandedBuildableTiles(Rect2I tileArea, int radius)
	{
		var validTiles = GetValidTilesInRadius(tileArea, radius).ToHashSet();
		var expandedTiles = validTiles.Except(validBuildableTiles).Except(occupiedTiles);
		var atlasCoords = new Vector2I(1, 0);
		foreach (var tilePosition in expandedTiles)
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, atlasCoords);
		}
	}

	public void HighlightAttackTiles(Rect2I tileArea, int radius)
	{
		var buildingAreaTiles = tileArea.ToTiles();
		var validTiles = GetValidTilesInRadius(tileArea, radius).ToHashSet()
			.Except(validBuildableAttackTiles)
			.Except(buildingAreaTiles);

		var atlasCoords = new Vector2I(1, 0);
		foreach (var tilePosition in validTiles)
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, atlasCoords);
		}
	}

	public void HighlightResourceTiles(Rect2I tileArea, int radius)
	{
		var resourceTiles = GetResourceTilesInRadius(tileArea, radius);
		var atlasCoords = new Vector2I(1, 0);
		foreach (var tilePosition in resourceTiles)
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, atlasCoords);
		}
	}

	public void ClearHighlightedTiles()
	{
		highlightTilemapLayer.Clear();
	}

	public Vector2I GetMouseGridCellPositionWithDimensionOffset(Vector2 dimensions)
	{
		var mouseGridPosition = highlightTilemapLayer.GetGlobalMousePosition() / 64;
		mouseGridPosition -= dimensions / 2;
		mouseGridPosition = mouseGridPosition.Round();
		return new Vector2I((int)mouseGridPosition.X, (int)mouseGridPosition.Y);
	}

	public Vector2I GetMouseGridCellPosition()
	{
		var mousePosition = highlightTilemapLayer.GetGlobalMousePosition();
		return ConvertWorldPositionToTilePosition(mousePosition);
	}

	public Vector2I ConvertWorldPositionToTilePosition(Vector2 worldPosition)
	{
		var tilePosition = worldPosition / 64;
		tilePosition = tilePosition.Floor();
		return new Vector2I((int)tilePosition.X, (int)tilePosition.Y);
	}

	public bool CanMoveBuilding(BuildingComponent toMoveBuildingComponent, Rect2I destinationArea = new Rect2I())
	{
		if(destinationArea.Area == 0)
		{
			destinationArea = toMoveBuildingComponent.GetAreaOccupied(ConvertWorldPositionToTilePosition(toMoveBuildingComponent.GlobalPosition));
		}
		
		var tilesInRadiusofRobotArrival = GetValidTilesInRadius(destinationArea, toMoveBuildingComponent.BuildingResource.BuildableRadius);

		if(toMoveBuildingComponent.BuildingResource.BuildableRadius > 0)
		{
			return IsRobotNetworkConnected(toMoveBuildingComponent, tilesInRadiusofRobotArrival) && IsGettingOutOfACoverage(toMoveBuildingComponent, destinationArea);
		}
		return false;
	}

	public bool CanDestroyBuilding(BuildingComponent toDestroyBuildingComponent)
	{
		if (toDestroyBuildingComponent.BuildingResource.BuildableRadius > 0)
		{
			return !WillBuildingDestructionCreateOrphanBuildings(toDestroyBuildingComponent) &&
				IsBuildingNetworkConnected(toDestroyBuildingComponent);
		}
		else if (toDestroyBuildingComponent.BuildingResource.IsAttackBuilding())
		{
			return CanDestroyBarracks(toDestroyBuildingComponent);
		}
		return true;
	}

	private bool CanDestroyBarracks(BuildingComponent toDestroyBuildingComponent)
	{
		var disabledDangerBuildings = BuildingComponent.GetDangerBuildingComponents(this)
			.Where((buildingComponent) => buildingComponent.GetOccupiedCellPositions().Any((tilePosition) =>
			{
				return attackBuildingToTiles[toDestroyBuildingComponent].Contains(tilePosition);
			}));

		if (!disabledDangerBuildings.Any()) return true;

		var allDangerBuildingsStillDisabled = disabledDangerBuildings.All((dangerBuilding) =>
		{
			return dangerBuilding.GetOccupiedCellPositions().Any((tilePosition) =>
			{
				return attackBuildingToTiles.Keys.Where((attackBuilding) => attackBuilding != toDestroyBuildingComponent)
					.Any((attackBuilding) => attackBuildingToTiles[attackBuilding].Contains(tilePosition));
			});
		});

		if (allDangerBuildingsStillDisabled) return true;

		var nonDangerBuildings = BuildingComponent.GetNonDangerBuildingComponents(this).Where((nonDangerBuilding) =>
		{
			return nonDangerBuilding != toDestroyBuildingComponent;
		});
		var anyDangerBuildingContainsPlayerBuilding = disabledDangerBuildings.Any((dangerBuilding) =>
		{
			var dangerTiles = dangerBuildingToTiles[dangerBuilding];
			return nonDangerBuildings.Any((nonDangerBuilding) =>
			{
				return nonDangerBuilding.GetOccupiedCellPositions().Any((tilePosition) => dangerTiles.Contains(tilePosition));
			});
		});

		return !anyDangerBuildingContainsPlayerBuilding;
	}

	public HashSet<Vector2I> GetCollectedResourcetiles()
	{
		return collectedResourceTiles.ToHashSet();
	}

	public HashSet<Vector2I> GetDiscoveredResourceTiles()
	{
		return discoveredElementsTiles.ToHashSet();
	}

	public bool IsInBaseProximity(Vector2I position)
	{
		return baseProximityTiles.Contains(position);
	}

	private bool WillBuildingDestructionCreateOrphanBuildings(BuildingComponent toDestroyBuildingComponent)
	{
		var dependentBuildings = BuildingComponent.GetNonDangerBuildingComponents(this)
			.Where((buildingComponent) =>
			{
				if (buildingComponent == toDestroyBuildingComponent) return false;
				if (buildingComponent.BuildingResource.IsBase) return false;

				var anyTilesInRadius = buildingComponent.GetOccupiedCellPositions()
					.Any((tilePosition) => buildingToBuildableTiles[toDestroyBuildingComponent].Contains(tilePosition));
				return anyTilesInRadius;
			});

		var allBuildingsStillValid = dependentBuildings.All((dependentBuilding) =>
		{
			var tilesForBuilding = dependentBuilding.GetOccupiedCellPositions();
			var buildingsToCheck = buildingToBuildableTiles.Keys
				.Where((key) => key != toDestroyBuildingComponent && key != dependentBuilding);

			return tilesForBuilding.All((tilePosition) =>
			{
				var tileIsInSet = buildingsToCheck
					.Any((buildingComponent) => buildingToBuildableTiles[buildingComponent].Contains(tilePosition));
				return tileIsInSet;
			});
		});

		if (!allBuildingsStillValid)
		{
			return true;
		}

		return false;
	}

	private bool IsBuildingNetworkConnected(BuildingComponent toMoveBuildingComponent)
	{
		var baseBuilding = BuildingComponent.GetValidBuildingComponents(this)
			.First((buildingComponent) => buildingComponent.BuildingResource.IsBase);

		var visitedBuildings = new HashSet<BuildingComponent>();
		VisitAllConnectedBuildings(baseBuilding, toMoveBuildingComponent, visitedBuildings);

		var totalBuildingsToVisit = BuildingComponent.GetValidBuildingComponents(this)
			.Count((buildingComponent) =>
			{
				return buildingComponent != toMoveBuildingComponent && buildingComponent.BuildingResource.BuildableRadius > 0;
			});

		return totalBuildingsToVisit == visitedBuildings.Count;
	}

	private bool IsRobotNetworkConnected(BuildingComponent toMoveBuildingComponent, List<Vector2I> robotCoverageAtDestination)
	{
		var baseBuilding = BuildingComponent.GetValidBuildingComponents(this)
			.First((buildingComponent) => buildingComponent.BuildingResource.IsBase);

		var visitedBuildings = new HashSet<BuildingComponent>();
		VisitAllConnectedRobots(baseBuilding, toMoveBuildingComponent, robotCoverageAtDestination, visitedBuildings);

		if(visitedBuildings.Contains(baseBuilding))
		{
			return true;
		}
		else
		{
			return false;
		}
	}

	private void VisitAllConnectedRobots(BuildingComponent rootBuilding, BuildingComponent toMoveRobot ,List<Vector2I> robotCoverageAtDestination, HashSet<BuildingComponent> visitedBuildings)
	{
		var connectedRobots = BuildingComponent.GetNonDangerBuildingComponents(this)
			.Where((buildingComponent) =>
			{
				if (buildingComponent.BuildingResource.BuildableRadius == 0) return false;
				if (visitedBuildings.Contains(buildingComponent)) return false;

				var anyTilesInRadius = GetValidTilesInRadius(buildingComponent.GetTileArea(), buildingComponent.BuildingResource.BuildableRadius)
					.Any((tilePosition) => robotCoverageAtDestination.Contains(tilePosition));
				return buildingComponent != toMoveRobot && anyTilesInRadius;
			}).ToList();


		visitedBuildings.UnionWith(connectedRobots);
		if (visitedBuildings.Contains(rootBuilding)) return;

		foreach (var connectedRobot in connectedRobots)
		{
			VisitAllConnectedRobots(rootBuilding, connectedRobot, GetValidTilesInRadius(connectedRobot.GetTileArea(), connectedRobot.BuildingResource.BuildableRadius), visitedBuildings);
		}
	}

	private void VisitAllConnectedBuildings(BuildingComponent rootBuilding,	BuildingComponent excludeBuilding, HashSet<BuildingComponent> visitedBuildings
	)
	{
		var dependentBuildings = BuildingComponent.GetNonDangerBuildingComponents(this)
			.Where((buildingComponent) =>
			{
				if (buildingComponent.BuildingResource.BuildableRadius == 0) return false;
				if (visitedBuildings.Contains(buildingComponent)) return false;

				var anyTilesInRadius = buildingComponent.GetOccupiedCellPositions()
					.All((tilePosition) => buildingToBuildableTiles[rootBuilding].Contains(tilePosition));
				return buildingComponent != excludeBuilding && anyTilesInRadius;
			}).ToList();

		visitedBuildings.UnionWith(dependentBuildings);
		foreach (var dependentBuilding in dependentBuildings)
		{
			VisitAllConnectedBuildings(dependentBuilding, excludeBuilding, visitedBuildings);
		}
	}

	private HashSet<Vector2I> GetBuildableTileSet(bool isAttackTiles = false)
	{
		return isAttackTiles ? validBuildableAttackTiles : validBuildableTiles;
	}

	private HashSet<Vector2I> GetValidTileSet()
	{
		return allTilesInBuildingRadius;
	}

	private List<TileMapLayer> GetAllTilemapLayers(Node2D rootNode)
	{
		var result = new List<TileMapLayer>();
		var children = rootNode.GetChildren();
		children.Reverse();
		foreach (var child in children)
		{
			if (child is Node2D childNode)
			{
				result.AddRange(GetAllTilemapLayers(childNode));
			}
		}

		if (rootNode is TileMapLayer tileMapLayer)
		{
			result.Add(tileMapLayer);
		}
		return result;
	}

	private List<Vector2I> GetAllBuildableBaseTerrainTiles(TileMapLayer tileMapLayer)
	{
		// Loop through all possible cells in the TileMap
        var usedTiles = tileMapLayer.GetUsedCells();
    	// Filter tiles where the custom data indicates they are buildable
    	var buildableTiles = usedTiles
        .Where(tilePosition =>
        {
            var (tileMapLayer, isBuildable) = GetTileCustomData(tilePosition, IS_BUILDABLE);
            return isBuildable;
        })
        .ToList();
		return buildableTiles;
	}

	private void MapTileMapLayersToElevationLayers()
	{
		foreach (var layer in allTilemapLayers)
		{
			ElevationLayer elevationLayer;
			Node startNode = layer;
			do
			{
				var parent = startNode.GetParent();
				elevationLayer = parent as ElevationLayer;
				startNode = parent;
			} while (elevationLayer == null && startNode != null);

			tileMapLayerToElevationLayer[layer] = elevationLayer;
		}
	}

	private void UpdateDangerOccupiedTiles(BuildingComponent buildingComponent)
	{
		occupiedTiles.UnionWith(buildingComponent.GetOccupiedCellPositions());

		if (buildingComponent.BuildingResource.IsDangerBuilding())
		{
			var tileArea = buildingComponent.GetTileArea();
			var tilesInRadius = GetValidTilesInRadius(tileArea, buildingComponent.BuildingResource.DangerRadius).ToHashSet();

			dangerBuildingToTiles[buildingComponent] = tilesInRadius.ToHashSet();

			if (!buildingComponent.IsDisabled)
			{
				tilesInRadius.ExceptWith(occupiedTiles);
				dangerOccupiedTiles.UnionWith(tilesInRadius);
			}
		}
	}

	private void UpdateValidBuildableTiles(BuildingComponent buildingComponent)
	{
		occupiedTiles.UnionWith(buildingComponent.GetOccupiedCellPositions());
		var tileArea = buildingComponent.GetTileArea();

		if (buildingComponent.BuildingResource.BuildableRadius > 0)
		{
			var allTiles = GetTilesInRadiusFiltered(tileArea, buildingComponent.BuildingResource.BuildableRadius, (_) => true);
			allTilesInBuildingRadius.UnionWith(allTiles);

			var validTiles = GetValidTilesInRadius(tileArea, buildingComponent.BuildingResource.BuildableRadius);
			var validTilesPlusOne = GetValidTilesInRadius(tileArea, buildingComponent.BuildingResource.BuildableRadius + 1);
			buildingToBuildableTiles[buildingComponent] = validTiles.ToHashSet();
			validBuildableTiles.UnionWith(validTiles);
		}

		validBuildableTiles.ExceptWith(occupiedTiles);
		validBuildableAttackTiles.UnionWith(validBuildableTiles);

		validBuildableTiles.ExceptWith(dangerOccupiedTiles);
		EmitSignal(SignalName.GridStateUpdated);
	}

	private void SetBaseAntennaCoverage()
	{
		
		var buildingComponents = BuildingComponent.GetBaseBuilding(this);
		foreach(var buildingComponent in buildingComponents)
		{
			var baseOccupiedTiles = buildingComponent.GetOccupiedCellPositions();
			var tileArea = buildingComponent.GetTileArea();
			var allTiles = GetTilesInRadiusFiltered(tileArea, buildingComponent.BuildingResource.BuildableRadius, (_) => true);
			var allTilesRestrained = GetTilesInRadiusFiltered(tileArea, 2, (_) => true);
			baseProximityTiles = allTilesRestrained.ToHashSet();
			baseAntennaCoveredTiles = allTiles.ToHashSet();
		}
	}

	private void UpdateCollectedResourceTiles(BuildingComponent buildingComponent)
	{
		var tileArea = buildingComponent.GetTileArea();
		var resourceTiles = GetResourceTilesInRadius(tileArea, buildingComponent.BuildingResource.ResourceRadius);

		var oldResourceTileCount = collectedResourceTiles.Count;
		collectedResourceTiles.UnionWith(resourceTiles);

		if (oldResourceTileCount != collectedResourceTiles.Count)
		{
			EmitSignal(SignalName.ResourceTilesUpdated, collectedResourceTiles.Count);
		}
		EmitSignal(SignalName.GridStateUpdated);
	}

	private void UpdateRechargeBattery(BuildingComponent buildingComponent)
	{
		var occupiedCellPositions = buildingComponent.GetOccupiedCellPositions();
		foreach(var position in occupiedCellPositions)
		{
			if(baseProximityTiles.Contains(position))
			{
				buildingComponent.IsRecharging = true;
			}
			else buildingComponent.IsRecharging = false;
		}
	}

	private void UpdateDiscoveredTiles(BuildingComponent buildingComponent)
	{
		var tileArea = buildingComponent.GetTileArea();
		var discoveredTiles = GetDiscoveredTilesInRadius(tileArea, buildingComponent.BuildingResource.VisionRadius);

		var oldDiscoveredTileCount = discoveredElementsTiles.Count;
		discoveredElementsTiles.UnionWith(discoveredTiles.Keys);

		if (oldDiscoveredTileCount != discoveredElementsTiles.Count)
		{
			foreach(var entry in discoveredTiles)
			{
			EmitSignal(SignalName.DiscoveredTileUpdated, entry.Key, entry.Value);
			}
		}
		EmitSignal(SignalName.GridStateUpdated);
	}

	private void UpdateAttackTiles(BuildingComponent buildingComponent)
	{
		if (!buildingComponent.BuildingResource.IsAttackBuilding()) return;

		var tileArea = buildingComponent.GetTileArea();
		var newAttackTiles = GetTilesInRadiusFiltered(tileArea, buildingComponent.BuildingResource.AttackRadius, (_) => true)
			.ToHashSet();
		attackBuildingToTiles[buildingComponent] = newAttackTiles;
		attackTiles.UnionWith(newAttackTiles);
	}

	private void RecalculateGrid()
	{
		occupiedTiles.Clear();
		validBuildableTiles.Clear();
		validBuildableAttackTiles.Clear();
		allTilesInBuildingRadius.Clear();
		collectedResourceTiles.Clear();
		dangerOccupiedTiles.Clear();
		attackTiles.Clear();
		buildingToBuildableTiles.Clear();
		dangerBuildingToTiles.Clear();
		attackBuildingToTiles.Clear();

		var buildingComponents = BuildingComponent.GetValidBuildingComponents(this);

		foreach (var buildingComponent in buildingComponents)
		{
			UpdateBuildingComponentGridState(buildingComponent);
			UpdateDiscoveredTiles(buildingComponent);
			CheckGroundRobotTouchingMonolith(buildingComponent);
			CheckAerialRobotVisualMonolith(buildingComponent);
		}
		CheckStuckRobotNearby();
		CheckDangerBuildingDestruction();

		EmitSignal(SignalName.ResourceTilesUpdated, collectedResourceTiles.Count);
		EmitSignal(SignalName.GridStateUpdated);
	}

	private void RecalculateDangerOccupiedTiles()
	{
		dangerOccupiedTiles.Clear();
		var dangerBuildings = BuildingComponent.GetDangerBuildingComponents(this);
		foreach (var building in dangerBuildings)
		{
			UpdateDangerOccupiedTiles(building);
		}
	}

	private void CheckDangerBuildingDestruction()
	{
		var dangerBuildings = BuildingComponent.GetDangerBuildingComponents(this);
		foreach (var building in dangerBuildings)
		{
			var tileArea = building.GetTileArea();
			var isInsideAttackTile = tileArea.ToTiles().Any((tilePosition) => attackTiles.Contains(tilePosition));
			if (isInsideAttackTile)
			{
				building.Disable();
			}
			else
			{
				building.Enable();
			}
		}
	}

	private void CheckStuckRobotNearby()
	{
		var occupiedTilesExceptStuck = new HashSet<Vector2I>(occupiedTiles);
		bool isNear = false;
		foreach(var robot in buildingStuckToTiles.Keys)
		{
			HashSet<Vector2I> positions = buildingStuckToTiles[robot];
			occupiedTilesExceptStuck.ExceptWith(robot.GetOccupiedCellPositions());
        	isNear = positions.Any(position => occupiedTilesExceptStuck.Contains(position));
			if(isNear)
			{
				robot.SetToUnstuck();
			}
		}
	}

	private void CheckGroundRobotTouchingMonolith(BuildingComponent buildingComponent)
	{
		if(buildingComponent.BuildingResource.IsAerial || buildingComponent.BuildingResource.IsBase) return;
		foreach(var adjacentTile in buildingComponent.GetTileAndAdjacent())
		{
			if (monolithTiles.Contains(adjacentTile))
			{
				EmitSignal(SignalName.GroundRobotTouchingMonolith);
				return;
			}
		}
	}

	private void CheckAerialRobotVisualMonolith(BuildingComponent buildingComponent)
	{
		if(buildingComponent.BuildingResource.IsAerial)
		{
			foreach(var visionTile in GetTilesInRadius(buildingComponent.GetAreaOccupied(ConvertWorldPositionToTilePosition(buildingComponent.GlobalPosition)),buildingComponent.BuildingResource.VisionRadius))
			{
				if (monolithTiles.Contains(visionTile))
				{
					EmitSignal(SignalName.AerialRobotHasVisionOfMonolith);
					return;
				}
			}
		}
	}

	private bool IsTileInsideCircle(Vector2 centerPosition, Vector2 tilePosition, float radius)
	{
		var distanceX = centerPosition.X - (tilePosition.X + .5);
		var distanceY = centerPosition.Y - (tilePosition.Y + .5);
		var distanceSquared = (distanceX * distanceX) + (distanceY * distanceY);
		return distanceSquared <= radius * radius;
	}

	private List<Vector2I> GetTilesInRadiusFiltered(Rect2I tileArea, int radius, Func<Vector2I, bool> filterFn)
	{
		var result = new List<Vector2I>();
		var tileAreaF = tileArea.ToRect2F();
		var tileAreaCenter = tileAreaF.GetCenter();
		var radiusMod = Mathf.Max(tileAreaF.Size.X, tileAreaF.Size.Y) / 2;

		for (var x = tileArea.Position.X - radius; x < tileArea.End.X + radius; x++)
		{
			for (var y = tileArea.Position.Y - radius; y < tileArea.End.Y + radius; y++)
			{
				var tilePosition = new Vector2I(x, y);
				if (!IsTileInsideCircle(tileAreaCenter, tilePosition, radius + radiusMod) || !filterFn(tilePosition)) continue;
				result.Add(tilePosition);
			}
		}
		return result;
	}

		private List<Vector2I> GetTilesInRadius(Rect2I tileArea, int radius)
	{
		var result = new List<Vector2I>();
		var tileAreaF = tileArea.ToRect2F();
		var tileAreaCenter = tileAreaF.GetCenter();
		var radiusMod = Mathf.Max(tileAreaF.Size.X, tileAreaF.Size.Y) / 2;

		for (var x = tileArea.Position.X - radius; x < tileArea.End.X + radius; x++)
		{
			for (var y = tileArea.Position.Y - radius; y < tileArea.End.Y + radius; y++)
			{
				var tilePosition = new Vector2I(x, y);
				if (!IsTileInsideCircle(tileAreaCenter, tilePosition, radius + radiusMod)) continue;
				result.Add(tilePosition);
			}
		}
		return result;
	}

	private List<Vector2I> GetValidTilesInRadius(Rect2I tileArea, int radius)
	{
		return GetTilesInRadiusFiltered(tileArea, radius, (tilePosition) =>
		{
			return GetTileCustomData(tilePosition, IS_BUILDABLE).Item2 || monolithTiles.Contains(tilePosition);
		});
	}

	private List<Vector2I> GetResourceTilesInRadius(Rect2I tileArea, int radius)
	{
		return GetTilesInRadiusFiltered(tileArea, radius, (tilePosition) =>
		{
			return GetTileCustomData(tilePosition, IS_WOOD).Item2;
		});
	}

	private Dictionary<Vector2I, string> GetDiscoveredTilesInRadius(Rect2I tileArea, int radius)
	{
		Dictionary<Vector2I, string> tileToLandscapeType = new();
		var tilesInRadius  = GetTilesInRadius(tileArea, radius);
		string type;
		foreach (var tile in tilesInRadius)
		{
			type = GetTileDiscoveredElements(tile);
			if(type != "")
			{
				tileToLandscapeType.Add(tile, type);
			}
		}
		return tileToLandscapeType;
	}

	public void UpdateBuildingComponentGridState(BuildingComponent buildingComponent)
	{
		var buildingOccupiedTiles = buildingComponent.GetOccupiedCellPositions();
		UpdateDangerOccupiedTiles(buildingComponent);
		UpdateValidBuildableTiles(buildingComponent);
		UpdateRechargeBattery(buildingComponent);
		UpdateCollectedResourceTiles(buildingComponent);
		UpdateAttackTiles(buildingComponent);
	}

	private void OnBuildingPlaced(BuildingComponent buildingComponent)
	{
		UpdateBuildingComponentGridState(buildingComponent);
		UpdateDiscoveredTiles(buildingComponent);
		if(baseAntennaCoveredTiles.Count() == 0)
		{
			SetBaseAntennaCoverage();
		}
		CheckDangerBuildingDestruction();
	}

	private void OnBuildingMoved(BuildingComponent buildingComponent)
	{
		ClearHighlightedTiles();
		CallDeferred("RecalculateGrid");
		HighlightBuildableTiles();
	}

	private void OnBuildingStuck(BuildingComponent buildingComponent)
	{
		var tileAreaOccupied = buildingComponent.GetTileAndAdjacent();
		buildingStuckToTiles[buildingComponent] = tileAreaOccupied;
	}

	private void OnBuildingUnStuck(BuildingComponent buildingComponent)
	{
		buildingStuckToTiles.Clear();
	}

	private void OnBuildingDestroyed(BuildingComponent buildingComponent)
	{
		RecalculateGrid();
	}

	private void OnBuildingEnabled(BuildingComponent buildingComponent)
	{
		UpdateBuildingComponentGridState(buildingComponent);
	}

	private void OnBuildingDisabled(BuildingComponent buildingComponent)
	{
		RecalculateGrid();
	}
}
