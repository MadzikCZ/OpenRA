#region Copyright & License Information
/*
 * Copyright 2007-2019 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class AircraftInfo : ITraitInfo, IPositionableInfo, IFacingInfo, IMoveInfo, ICruiseAltitudeInfo,
		IActorPreviewInitInfo, IEditorActorOptions
	{
		public readonly WDist CruiseAltitude = new WDist(1280);

		[Desc("Whether the aircraft can be repulsed.")]
		public readonly bool Repulsable = true;

		[Desc("The distance it tries to maintain from other aircraft if repulsable.")]
		public readonly WDist IdealSeparation = new WDist(1706);

		[Desc("The speed at which the aircraft is repulsed from other aircraft. Specify -1 for normal movement speed.")]
		public readonly int RepulsionSpeed = -1;

		public readonly int InitialFacing = 0;

		public readonly int TurnSpeed = 255;

		[Desc("Turn speed to apply when aircraft flies in circles while idle. Defaults to TurnSpeed if negative.")]
		public readonly int IdleTurnSpeed = -1;

		public readonly int Speed = 1;

		[Desc("Minimum altitude where this aircraft is considered airborne.")]
		public readonly int MinAirborneAltitude = 1;

		public readonly HashSet<string> LandableTerrainTypes = new HashSet<string>();

		[Desc("Can the actor be ordered to move in to shroud?")]
		public readonly bool MoveIntoShroud = true;

		[Desc("e.g. crate, wall, infantry")]
		public readonly BitSet<CrushClass> Crushes = default(BitSet<CrushClass>);

		[Desc("Types of damage that are caused while crushing. Leave empty for no damage types.")]
		public readonly BitSet<DamageType> CrushDamageTypes = default(BitSet<DamageType>);

		[VoiceReference] public readonly string Voice = "Action";

		[GrantedConditionReference]
		[Desc("The condition to grant to self while airborne.")]
		public readonly string AirborneCondition = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while at cruise altitude.")]
		public readonly string CruisingCondition = null;

		[Desc("Can the actor hover in place mid-air? If not, then the actor will have to remain in motion (circle around).")]
		public readonly bool CanHover = false;

		[Desc("Does the actor land and take off vertically?")]
		public readonly bool VTOL = false;

		[Desc("Will this actor try to land after it has no more commands?")]
		public readonly bool LandWhenIdle = true;

		[Desc("Does this VTOL actor need to turn before landing (on terrain)?")]
		public readonly bool TurnToLand = false;

		[Desc("Does this VTOL actor need to turn before landing on a resupplier?")]
		public readonly bool TurnToDock = true;

		[Desc("Does this actor cancel its previous activity after resupplying?")]
		public readonly bool AbortOnResupply = true;

		[Desc("Does this actor automatically take off after resupplying?")]
		public readonly bool TakeOffOnResupply = false;

		[Desc("Does this actor automatically take off after creation?")]
		public readonly bool TakeOffOnCreation = true;

		[Desc("Altitude at which the aircraft considers itself landed.")]
		public readonly WDist LandAltitude = WDist.Zero;

		[Desc("How fast this actor ascends or descends when using horizontal take off/landing.")]
		public readonly WAngle MaximumPitch = WAngle.FromDegrees(10);

		[Desc("How fast this actor ascends or descends when using vertical take off/landing.")]
		public readonly WDist AltitudeVelocity = new WDist(43);

		[Desc("Sounds to play when the actor is taking off.")]
		public readonly string[] TakeoffSounds = { };

		[Desc("Sounds to play when the actor is landing.")]
		public readonly string[] LandingSounds = { };

		[Desc("The distance of the resupply base that the aircraft will wait for its turn.")]
		public readonly WDist WaitDistanceFromResupplyBase = new WDist(3072);

		[Desc("The number of ticks that a airplane will wait to make a new search for an available airport.")]
		public readonly int NumberOfTicksToVerifyAvailableAirport = 150;

		[Desc("Facing to use for actor previews (map editor, color picker, etc)")]
		public readonly int PreviewFacing = 92;

		[Desc("Display order for the facing slider in the map editor")]
		public readonly int EditorFacingDisplayOrder = 3;

		public int GetInitialFacing() { return InitialFacing; }
		public WDist GetCruiseAltitude() { return CruiseAltitude; }

		public virtual object Create(ActorInitializer init) { return new Aircraft(init, this); }

		IEnumerable<object> IActorPreviewInitInfo.ActorPreviewInits(ActorInfo ai, ActorPreviewType type)
		{
			yield return new FacingInit(PreviewFacing);
		}

		[Desc("Condition when this aircraft should land as soon as possible and refuse to take off. ",
			"This only applies while the aircraft is above terrain which is listed in LandableTerrainTypes.")]
		public readonly BooleanExpression LandOnCondition;

		public IReadOnlyDictionary<CPos, SubCell> OccupiedCells(ActorInfo info, CPos location, SubCell subCell = SubCell.Any) { return new ReadOnlyDictionary<CPos, SubCell>(); }

		bool IOccupySpaceInfo.SharesCell { get { return false; } }

		// Used to determine if an aircraft can spawn landed
		public bool CanEnterCell(World world, Actor self, CPos cell, Actor ignoreActor = null, bool checkTransientActors = true)
		{
			if (!world.Map.Contains(cell))
				return false;

			var type = world.Map.GetTerrainInfo(cell).Type;
			if (!LandableTerrainTypes.Contains(type))
				return false;

			if (world.WorldActor.Trait<BuildingInfluence>().GetBuildingAt(cell) != null)
				return false;

			if (!checkTransientActors)
				return true;

			return !world.ActorMap.GetActorsAt(cell).Any(x => x != ignoreActor);
		}

		IEnumerable<EditorActorOption> IEditorActorOptions.ActorOptions(ActorInfo ai, World world)
		{
			yield return new EditorActorSlider("Facing", EditorFacingDisplayOrder, 0, 255, 8,
				actor =>
				{
					var init = actor.Init<FacingInit>();
					return init != null ? init.Value(world) : InitialFacing;
				},
				(actor, value) => actor.ReplaceInit(new FacingInit((int)value)));
		}
	}

	public class Aircraft : ITick, ISync, IFacing, IPositionable, IMove, IIssueOrder, IResolveOrder, IOrderVoice, IDeathActorInitModifier,
		INotifyCreated, INotifyAddedToWorld, INotifyRemovedFromWorld, INotifyActorDisposing, INotifyBecomingIdle,
		IActorPreviewInitModifier, IIssueDeployOrder, IObservesVariables
	{
		static readonly Pair<CPos, SubCell>[] NoCells = { };

		public readonly AircraftInfo Info;
		readonly Actor self;

		Repairable repairable;
		Rearmable rearmable;
		ConditionManager conditionManager;
		IDisposable reservation;
		IEnumerable<int> speedModifiers;
		INotifyMoving[] notifyMoving;

		[Sync] public int Facing { get; set; }
		[Sync] public WPos CenterPosition { get; private set; }
		public CPos TopLeft { get { return self.World.Map.CellContaining(CenterPosition); } }
		public int TurnSpeed { get { return Info.TurnSpeed; } }
		public Actor ReservedActor { get; private set; }
		public bool MayYieldReservation { get; private set; }
		public bool ForceLanding { get; private set; }
		CPos? landingCell;

		public WDist LandAltitude { get; private set; }

		public static WPos GroundPosition(Actor self)
		{
			return self.CenterPosition - new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(self.CenterPosition));
		}

		bool airborne;
		bool cruising;
		bool firstTick = true;
		int airborneToken = ConditionManager.InvalidConditionToken;
		int cruisingToken = ConditionManager.InvalidConditionToken;

		MovementType movementTypes;
		WPos cachedPosition;
		int cachedFacing;
		bool? landNow;

		public Aircraft(ActorInitializer init, AircraftInfo info)
		{
			Info = info;
			self = init.Self;

			if (init.Contains<LocationInit>())
				SetPosition(self, init.Get<LocationInit, CPos>());

			if (init.Contains<CenterPositionInit>())
				SetPosition(self, init.Get<CenterPositionInit, WPos>());

			Facing = init.Contains<FacingInit>() ? init.Get<FacingInit, int>() : Info.InitialFacing;
			LandAltitude = info.LandAltitude;
		}

		public void AddLandingOffset(int offset)
		{
			LandAltitude += new WDist(offset);
		}

		public void SubtractLandingOffset(int offset)
		{
			LandAltitude -= new WDist(offset);
		}

		public virtual IEnumerable<VariableObserver> GetVariableObservers()
		{
			if (Info.LandOnCondition != null)
				yield return new VariableObserver(ForceLandConditionChanged, Info.LandOnCondition.Variables);
		}

		void ForceLandConditionChanged(Actor self, IReadOnlyDictionary<string, int> variables)
		{
			landNow = Info.LandOnCondition.Evaluate(variables);
		}

		void INotifyCreated.Created(Actor self)
		{
			Created(self);
		}

		protected virtual void Created(Actor self)
		{
			repairable = self.TraitOrDefault<Repairable>();
			rearmable = self.TraitOrDefault<Rearmable>();
			conditionManager = self.TraitOrDefault<ConditionManager>();
			speedModifiers = self.TraitsImplementing<ISpeedModifier>().ToArray().Select(sm => sm.GetSpeedModifier());
			cachedPosition = self.CenterPosition;
			notifyMoving = self.TraitsImplementing<INotifyMoving>().ToArray();
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			AddedToWorld(self);
		}

		protected virtual void AddedToWorld(Actor self)
		{
			self.World.AddToMaps(self, this);

			var altitude = self.World.Map.DistanceAboveTerrain(CenterPosition);
			if (altitude.Length >= Info.MinAirborneAltitude)
				OnAirborneAltitudeReached();
			if (altitude == Info.CruiseAltitude)
				OnCruisingAltitudeReached();
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			RemovedFromWorld(self);
		}

		protected virtual void RemovedFromWorld(Actor self)
		{
			UnReserve();
			self.World.RemoveFromMaps(self, this);

			OnCruisingAltitudeLeft();
			OnAirborneAltitudeLeft();
		}

		void ITick.Tick(Actor self)
		{
			Tick(self);
		}

		protected virtual void Tick(Actor self)
		{
			if (firstTick)
			{
				firstTick = false;

				// TODO: Aircraft husks don't properly unreserve.
				if (self.Info.HasTraitInfo<FallsToEarthInfo>())
					return;

				ReserveSpawnBuilding();

				var host = GetActorBelow();
				if (host == null)
					return;

				if (Info.TakeOffOnCreation)
					self.QueueActivity(new TakeOff(self));
			}

			// Add land activity if LandOnCondition resolves to true and the actor can land at the current location.
			if (!ForceLanding && landNow.HasValue && landNow.Value && airborne && CanLand(self.Location)
				&& !((self.CurrentActivity is Land) || self.CurrentActivity is Turn))
			{
				self.CancelActivity();

				if (Info.VTOL && Info.TurnToLand)
					self.QueueActivity(new Turn(self, Info.InitialFacing));

				self.QueueActivity(new Land(self));

				ForceLanding = true;
			}

			// Add takeoff activity if LandOnCondidion resolves to false and the actor should not land when idle.
			if (ForceLanding && landNow.HasValue && !landNow.Value && !cruising && !(self.CurrentActivity is TakeOff))
			{
				ForceLanding = false;

				if (!Info.LandWhenIdle)
				{
					self.CancelActivity();

					self.QueueActivity(new TakeOff(self));
				}
			}

			var oldCachedFacing = cachedFacing;
			cachedFacing = Facing;

			var oldCachedPosition = cachedPosition;
			cachedPosition = self.CenterPosition;

			var newMovementTypes = MovementType.None;
			if (oldCachedFacing != Facing)
				newMovementTypes |= MovementType.Turn;

			if ((oldCachedPosition - cachedPosition).HorizontalLengthSquared != 0)
				newMovementTypes |= MovementType.Horizontal;

			if ((oldCachedPosition - cachedPosition).VerticalLengthSquared != 0)
				newMovementTypes |= MovementType.Vertical;

			CurrentMovementTypes = newMovementTypes;

			Repulse();
		}

		public void Repulse()
		{
			var repulsionForce = GetRepulsionForce();
			if (repulsionForce == WVec.Zero)
				return;

			var speed = Info.RepulsionSpeed != -1 ? Info.RepulsionSpeed : MovementSpeed;
			SetPosition(self, CenterPosition + FlyStep(speed, repulsionForce.Yaw.Facing));
		}

		public virtual WVec GetRepulsionForce()
		{
			if (!Info.Repulsable)
				return WVec.Zero;

			if (reservation != null)
			{
				var distanceFromReservationActor = (ReservedActor.CenterPosition - self.CenterPosition).HorizontalLength;
				if (distanceFromReservationActor < Info.WaitDistanceFromResupplyBase.Length)
					return WVec.Zero;
			}

			// Repulsion only applies when we're flying at CruiseAltitude!
			if (!cruising)
				return WVec.Zero;

			// PERF: Avoid LINQ.
			var repulsionForce = WVec.Zero;
			foreach (var actor in self.World.FindActorsInCircle(self.CenterPosition, Info.IdealSeparation))
			{
				if (actor.IsDead)
					continue;

				var ai = actor.Info.TraitInfoOrDefault<AircraftInfo>();
				if (ai == null || !ai.Repulsable || ai.CruiseAltitude != Info.CruiseAltitude)
					continue;

				repulsionForce += GetRepulsionForce(actor);
			}

			// Actors outside the map bounds receive an extra nudge towards the center of the map
			if (!self.World.Map.Contains(self.Location))
			{
				// The map bounds are in projected coordinates, which is technically wrong for this,
				// but we avoid the issues in practice by guessing the middle of the map instead of the edge
				var center = WPos.Lerp(self.World.Map.ProjectedTopLeft, self.World.Map.ProjectedBottomRight, 1, 2);
				repulsionForce += new WVec(1024, 0, 0).Rotate(WRot.FromYaw((self.CenterPosition - center).Yaw));
			}

			if (Info.CanHover)
				return repulsionForce;

			// Non-hovering actors mush always keep moving forward, so they need extra calculations.
			var currentDir = FlyStep(Facing);
			var length = currentDir.HorizontalLength * repulsionForce.HorizontalLength;
			if (length == 0)
				return WVec.Zero;

			var dot = WVec.Dot(currentDir, repulsionForce) / length;

			// avoid stalling the plane
			return dot >= 0 ? repulsionForce : WVec.Zero;
		}

		public WVec GetRepulsionForce(Actor other)
		{
			if (self == other || other.CenterPosition.Z < self.CenterPosition.Z)
				return WVec.Zero;

			var d = self.CenterPosition - other.CenterPosition;
			var distSq = d.HorizontalLengthSquared;
			if (distSq > Info.IdealSeparation.LengthSquared)
				return WVec.Zero;

			if (distSq < 1)
			{
				var yaw = self.World.SharedRandom.Next(0, 1023);
				var rot = new WRot(WAngle.Zero, WAngle.Zero, new WAngle(yaw));
				return new WVec(1024, 0, 0).Rotate(rot);
			}

			return (d * 1024 * 8) / (int)distSq;
		}

		public Actor GetActorBelow()
		{
			// Map.DistanceAboveTerrain(WPos pos) is called directly because Aircraft is an IPositionable trait
			// and all calls occur in Tick methods.
			if (self.World.Map.DistanceAboveTerrain(CenterPosition) != LandAltitude)
				return null; // Not on the resupplier.

			return self.World.ActorMap.GetActorsAt(self.Location)
				.FirstOrDefault(a => a.Info.HasTraitInfo<ReservableInfo>());
		}

		protected void ReserveSpawnBuilding()
		{
			// HACK: Not spawning in the air, so try to associate with our spawner.
			var spawner = GetActorBelow();
			if (spawner == null)
				return;

			MakeReservation(spawner);
		}

		public void MakeReservation(Actor target)
		{
			UnReserve();
			var reservable = target.TraitOrDefault<Reservable>();
			if (reservable != null)
			{
				reservation = reservable.Reserve(target, self, this);
				ReservedActor = target;
			}
		}

		public void AllowYieldingReservation()
		{
			if (reservation == null)
				return;

			MayYieldReservation = true;
		}

		public void UnReserve()
		{
			if (reservation == null)
				return;

			reservation.Dispose();
			reservation = null;
			ReservedActor = null;
			MayYieldReservation = false;

			if (self.World.Map.DistanceAboveTerrain(CenterPosition).Length <= LandAltitude.Length)
				self.QueueActivity(new TakeOff(self));
		}

		public bool AircraftCanEnter(Actor a)
		{
			if (self.AppearsHostileTo(a))
				return false;

			return (rearmable != null && rearmable.Info.RearmActors.Contains(a.Info.Name))
				|| (repairable != null && repairable.Info.RepairActors.Contains(a.Info.Name));
		}

		public int MovementSpeed
		{
			get { return Util.ApplyPercentageModifiers(Info.Speed, speedModifiers); }
		}

		public Pair<CPos, SubCell>[] OccupiedCells()
		{
			if (!self.IsAtGroundLevel())
			{
				if (landingCell.HasValue)
					return new[] { Pair.New(landingCell.Value, SubCell.FullCell) };

				return NoCells;
			}

			return new[] { Pair.New(TopLeft, SubCell.FullCell) };
		}

		public WVec FlyStep(int facing)
		{
			return FlyStep(MovementSpeed, facing);
		}

		public WVec FlyStep(int speed, int facing)
		{
			var dir = new WVec(0, -1024, 0).Rotate(WRot.FromFacing(facing));
			return speed * dir / 1024;
		}

		public bool CanLand(CPos cell, Actor ignoreActor = null)
		{
			if (!self.World.Map.Contains(cell))
				return false;

			foreach (var otherActor in self.World.ActorMap.GetActorsAt(cell))
				if (IsBlockedBy(self, otherActor, ignoreActor))
					return false;

			foreach (var otherActor in self.World.ActorMap.GetActorsAt(cell))
				if (AircraftCanEnter(otherActor))
					return true;

			var type = self.World.Map.GetTerrainInfo(cell).Type;
			return Info.LandableTerrainTypes.Contains(type);
		}

		bool IsBlockedBy(Actor self, Actor otherActor, Actor ignoreActor)
		{
			// We are not blocked by the actor we are ignoring.
			if (otherActor == self || otherActor == ignoreActor)
				return false;

			// PERF: Only perform ITemporaryBlocker trait look-up if mod/map rules contain any actors that are temporary blockers
			if (self.World.RulesContainTemporaryBlocker)
			{
				// If there is a temporary blocker in our path, but we can remove it, we are not blocked.
				var temporaryBlocker = otherActor.TraitOrDefault<ITemporaryBlocker>();
				if (temporaryBlocker != null && temporaryBlocker.CanRemoveBlockage(otherActor, self))
					return false;
			}

			// If we cannot crush the other actor in our way, we are blocked.
			if (Info.Crushes.IsEmpty)
				return true;

			// If the other actor in our way cannot be crushed, we are blocked.
			// PERF: Avoid LINQ.
			var crushables = otherActor.TraitsImplementing<ICrushable>();
			foreach (var crushable in crushables)
				if (crushable.CrushableBy(otherActor, self, Info.Crushes))
					return false;

			return true;
		}

		public bool CanRearmAt(Actor host)
		{
			return rearmable != null && rearmable.Info.RearmActors.Contains(host.Info.Name) && rearmable.RearmableAmmoPools.Any(p => !p.FullAmmo());
		}

		public bool CanRepairAt(Actor host)
		{
			return repairable != null && repairable.Info.RepairActors.Contains(host.Info.Name) && self.GetDamageState() != DamageState.Undamaged;
		}

		public void ModifyDeathActorInit(Actor self, TypeDictionary init)
		{
			init.Add(new FacingInit(Facing));
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			OnBecomingIdle(self);
		}

		protected virtual void OnBecomingIdle(Actor self)
		{
			var atLandAltitude = self.World.Map.DistanceAboveTerrain(CenterPosition) == LandAltitude;

			// Work-around to prevent players from accidentally canceling resupply by pressing 'Stop',
			// by re-queueing Resupply as long as resupply hasn't finished and aircraft is still on resupplier.
			// TODO: Investigate moving this back to ResolveOrder's "Stop" handling,
			// once conflicts with other traits' "Stop" orders have been fixed.
			if (atLandAltitude)
			{
				var host = GetActorBelow();
				if (host != null && (CanRearmAt(host) || CanRepairAt(host)))
				{
					self.QueueActivity(new Resupply(self, host, WDist.Zero));
					return;
				}
			}

			if (!atLandAltitude && Info.LandWhenIdle)
			{
				if (Info.VTOL && Info.TurnToLand)
					self.QueueActivity(new Turn(self, Info.InitialFacing));

				self.QueueActivity(new Land(self));
			}
			else if (!Info.CanHover && !atLandAltitude)
				self.QueueActivity(new FlyCircle(self, -1, Info.IdleTurnSpeed > -1 ? Info.IdleTurnSpeed : TurnSpeed));
			else if (atLandAltitude && !CanLand(self.Location) && ReservedActor == null)
				self.QueueActivity(new TakeOff(self));
			else if (Info.CanHover && Info.IdleTurnSpeed > 0)
			{
				// Temporary HACK for the AutoCarryall special case (needs CanHover, but also FlyCircle on idle).
				// Will go away soon (in a separate PR) with the arrival of ActionsWhenIdle.
				self.QueueActivity(new FlyCircle(self, -1, Info.IdleTurnSpeed > -1 ? Info.IdleTurnSpeed : TurnSpeed));
			}
		}

		#region Implement IPositionable

		public bool CanExistInCell(CPos cell) { return true; }
		public bool IsLeavingCell(CPos location, SubCell subCell = SubCell.Any) { return false; } // TODO: Handle landing
		public bool CanEnterCell(CPos cell, Actor ignoreActor = null, bool checkTransientActors = true) { return true; }
		public SubCell GetValidSubCell(SubCell preferred) { return SubCell.Invalid; }
		public SubCell GetAvailableSubCell(CPos a, SubCell preferredSubCell = SubCell.Any, Actor ignoreActor = null, bool checkTransientActors = true)
		{
			// Does not use any subcell
			return SubCell.Invalid;
		}

		public void SetVisualPosition(Actor self, WPos pos) { SetPosition(self, pos); }

		// Changes position, but not altitude
		public void SetPosition(Actor self, CPos cell, SubCell subCell = SubCell.Any)
		{
			SetPosition(self, self.World.Map.CenterOfCell(cell) + new WVec(0, 0, CenterPosition.Z));
		}

		public void SetPosition(Actor self, WPos pos)
		{
			CenterPosition = pos;

			if (!self.IsInWorld)
				return;

			self.World.UpdateMaps(self, this);

			var altitude = self.World.Map.DistanceAboveTerrain(CenterPosition);

			var isAirborne = altitude.Length >= Info.MinAirborneAltitude;
			if (isAirborne && !airborne)
				OnAirborneAltitudeReached();
			else if (!isAirborne && airborne)
				OnAirborneAltitudeLeft();

			var isCruising = altitude == Info.CruiseAltitude;
			if (isCruising && !cruising)
				OnCruisingAltitudeReached();
			else if (!isCruising && cruising)
				OnCruisingAltitudeLeft();

			FinishedMoving(self);
		}

		public void FinishedMoving(Actor self)
		{
			// Only make actor crush if it is on the ground
			if (!self.IsAtGroundLevel())
				return;

			var actors = self.World.ActorMap.GetActorsAt(TopLeft).Where(a => a != self).ToList();
			if (!AnyCrushables(actors))
				return;

			var notifiers = actors.SelectMany(a => a.TraitsImplementing<INotifyCrushed>().Select(t => new TraitPair<INotifyCrushed>(a, t)));
			foreach (var notifyCrushed in notifiers)
				notifyCrushed.Trait.OnCrush(notifyCrushed.Actor, self, Info.Crushes);
		}

		bool AnyCrushables(List<Actor> actors)
		{
			var crushables = actors.SelectMany(a => a.TraitsImplementing<ICrushable>().Select(t => new TraitPair<ICrushable>(a, t))).ToList();
			if (crushables.Count == 0)
				return false;

			foreach (var crushes in crushables)
				if (crushes.Trait.CrushableBy(crushes.Actor, self, Info.Crushes))
					return true;

			return false;
		}

		public void EnteringCell(Actor self)
		{
			var actors = self.World.ActorMap.GetActorsAt(TopLeft).Where(a => a != self).ToList();
			if (!AnyCrushables(actors))
				return;

			var notifiers = actors.SelectMany(a => a.TraitsImplementing<INotifyCrushed>().Select(t => new TraitPair<INotifyCrushed>(a, t)));
			foreach (var notifyCrushed in notifiers)
				notifyCrushed.Trait.WarnCrush(notifyCrushed.Actor, self, Info.Crushes);
		}

		public void AddInfluence(CPos landingCell)
		{
			this.landingCell = landingCell;
			if (self.IsInWorld)
				self.World.ActorMap.AddInfluence(self, this);
		}

		public void RemoveInfluence()
		{
			if (self.IsInWorld)
				self.World.ActorMap.RemoveInfluence(self, this);

			landingCell = null;
		}

		#endregion

		#region Implement IMove

		public Activity MoveTo(CPos cell, int nearEnough)
		{
			if (!Info.CanHover)
				return new Fly(self, Target.FromCell(self.World, cell));

			return new HeliFly(self, Target.FromCell(self.World, cell));
		}

		public Activity MoveTo(CPos cell, Actor ignoreActor)
		{
			if (!Info.CanHover)
				return new Fly(self, Target.FromCell(self.World, cell));

			return new HeliFly(self, Target.FromCell(self.World, cell));
		}

		public Activity MoveWithinRange(Target target, WDist range,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			if (!Info.CanHover)
				return new Fly(self, target, WDist.Zero, range, initialTargetPosition, targetLineColor);

			return new HeliFly(self, target, WDist.Zero, range, initialTargetPosition, targetLineColor);
		}

		public Activity MoveWithinRange(Target target, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			if (!Info.CanHover)
				return new Fly(self, target, minRange, maxRange,
					initialTargetPosition, targetLineColor);

			return new HeliFly(self, target, minRange, maxRange,
				initialTargetPosition, targetLineColor);
		}

		public Activity MoveFollow(Actor self, Target target, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			if (!Info.CanHover)
				return new FlyFollow(self, target, minRange, maxRange,
					initialTargetPosition, targetLineColor);

			return new Follow(self, target, minRange, maxRange,
				initialTargetPosition, targetLineColor);
		}

		public Activity MoveIntoWorld(Actor self, CPos cell, SubCell subCell = SubCell.Any)
		{
			if (!Info.CanHover)
				return new Fly(self, Target.FromCell(self.World, cell));

			return new HeliFly(self, Target.FromCell(self.World, cell, subCell));
		}

		public Activity MoveToTarget(Actor self, Target target,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			if (!Info.CanHover)
				return new Fly(self, target, WDist.FromCells(3), WDist.FromCells(5),
					initialTargetPosition, targetLineColor);

			return ActivityUtils.SequenceActivities(self,
				new HeliFly(self, target, initialTargetPosition, targetLineColor),
				new Turn(self, Info.InitialFacing));
		}

		public Activity MoveIntoTarget(Actor self, Target target)
		{
			return new Land(self, target);
		}

		public Activity VisualMove(Actor self, WPos fromPos, WPos toPos)
		{
			// TODO: Ignore repulsion when moving
			if (!Info.CanHover)
				return ActivityUtils.SequenceActivities(self,
					new CallFunc(() => SetVisualPosition(self, fromPos)),
					new Fly(self, Target.FromPos(toPos)));

			return ActivityUtils.SequenceActivities(self,
				new CallFunc(() => SetVisualPosition(self, fromPos)),
				new HeliFly(self, Target.FromPos(toPos)));
		}

		public int EstimatedMoveDuration(Actor self, WPos fromPos, WPos toPos)
		{
			var speed = MovementSpeed;
			return speed > 0 ? (toPos - fromPos).Length / speed : 0;
		}

		public CPos NearestMoveableCell(CPos cell) { return cell; }

		public MovementType CurrentMovementTypes
		{
			get
			{
				return movementTypes;
			}

			set
			{
				var oldValue = movementTypes;
				movementTypes = value;
				if (value != oldValue)
					foreach (var n in notifyMoving)
						n.MovementTypeChanged(self, value);
			}
		}

		public bool CanEnterTargetNow(Actor self, Target target)
		{
			if (target.Positions.Any(p => self.World.ActorMap.GetActorsAt(self.World.Map.CellContaining(p)).Any(a => a != self && a != target.Actor)))
				return false;

			MakeReservation(target.Actor);
			return true;
		}

		#endregion

		#region Implement order interfaces

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				yield return new EnterAlliedActorTargeter<BuildingInfo>("Enter", 5,
					target => AircraftCanEnter(target), target => Reservable.IsAvailableFor(target, self));

				yield return new AircraftMoveOrderTargeter(Info);
			}
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, Target target, bool queued)
		{
			if (order.OrderID == "Enter" || order.OrderID == "Move")
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			if (rearmable == null || !rearmable.Info.RearmActors.Any())
				return null;

			return new Order("ReturnToBase", self, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self) { return rearmable != null && rearmable.Info.RearmActors.Any(); }

		public string VoicePhraseForOrder(Actor self, Order order)
		{
			switch (order.OrderString)
			{
				case "Move":
					if (!Info.MoveIntoShroud && order.Target.Type != TargetType.Invalid)
					{
						var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
						if (!self.Owner.Shroud.IsExplored(cell))
							return null;
					}

					return Info.Voice;
				case "Enter":
				case "Stop":
				case "Scatter":
					return Info.Voice;
				case "ReturnToBase":
					return rearmable != null && rearmable.Info.RearmActors.Any() ? Info.Voice : null;
				default: return null;
			}
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "Move")
			{
				var cell = self.World.Map.Clamp(self.World.Map.CellContaining(order.Target.CenterPosition));
				if (!Info.MoveIntoShroud && !self.Owner.Shroud.IsExplored(cell))
					return;

				if (!order.Queued)
					UnReserve();

				var target = Target.FromCell(self.World, cell);

				self.SetTargetLine(target, Color.Green);

				if (!Info.CanHover)
					self.QueueActivity(order.Queued, new Fly(self, target));
				else
					self.QueueActivity(order.Queued, new HeliFlyAndLandWhenIdle(self, target, Info));
			}
			else if (order.OrderString == "Enter" || order.OrderString == "Repair")
			{
				// Enter and Repair orders are only valid for own/allied actors,
				// which are guaranteed to never be frozen.
				if (order.Target.Type != TargetType.Actor)
					return;

				if (!order.Queued)
					UnReserve();

				var targetActor = order.Target.Actor;

				// We only want to set a target line if the order will (most likely) succeed
				if (Reservable.IsAvailableFor(targetActor, self))
					self.SetTargetLine(Target.FromActor(targetActor), Color.Green);

				self.QueueActivity(order.Queued, new ReturnToBase(self, Info.AbortOnResupply, targetActor));
			}
			else if (order.OrderString == "Stop")
			{
				self.CancelActivity();

				// HACK: If the player accidentally pressed 'Stop', we don't want this to cancel reservation.
				// If unreserving is actually desired despite an actor below, it should be triggered from OnBecomingIdle.
				if (GetActorBelow() != null)
					return;

				UnReserve();
			}
			else if (order.OrderString == "ReturnToBase" && rearmable != null && rearmable.Info.RearmActors.Any())
			{
				// Don't restart activity every time deploy hotkey is triggered
				if (self.CurrentActivity is ReturnToBase || GetActorBelow() != null)
					return;

				if (!order.Queued)
					UnReserve();

				// Aircraft with TakeOffOnResupply would immediately take off again, so there's no point in forcing them to land
				// on a resupplier. For aircraft without it, it makes more sense to land than to idle above a free resupplier, though.
				self.QueueActivity(order.Queued, new ReturnToBase(self, Info.AbortOnResupply, null, !Info.TakeOffOnResupply));
			}
			else if (order.OrderString == "Scatter")
				Nudge(self);
		}

		#endregion

		void Nudge(Actor self)
		{
			// Disable nudging if the aircraft is outside the map
			if (!self.World.Map.Contains(self.Location))
				return;

			var offset = new WVec(0, -self.World.SharedRandom.Next(512, 2048), 0)
				.Rotate(WRot.FromFacing(self.World.SharedRandom.Next(256)));
			var target = Target.FromPos(self.CenterPosition + offset);

			self.CancelActivity();
			self.SetTargetLine(target, Color.Green, false);

			if (!Info.CanHover)
				self.QueueActivity(new Fly(self, target));
			else
				self.QueueActivity(new HeliFlyAndLandWhenIdle(self, target, Info));

			UnReserve();
		}

		#region Airborne conditions

		void OnAirborneAltitudeReached()
		{
			if (airborne)
				return;

			airborne = true;
			if (conditionManager != null && !string.IsNullOrEmpty(Info.AirborneCondition) && airborneToken == ConditionManager.InvalidConditionToken)
				airborneToken = conditionManager.GrantCondition(self, Info.AirborneCondition);
		}

		void OnAirborneAltitudeLeft()
		{
			if (!airborne)
				return;

			airborne = false;
			if (conditionManager != null && airborneToken != ConditionManager.InvalidConditionToken)
				airborneToken = conditionManager.RevokeCondition(self, airborneToken);
		}

		#endregion

		#region Cruising conditions

		void OnCruisingAltitudeReached()
		{
			if (cruising)
				return;

			cruising = true;
			if (conditionManager != null && !string.IsNullOrEmpty(Info.CruisingCondition) && cruisingToken == ConditionManager.InvalidConditionToken)
				cruisingToken = conditionManager.GrantCondition(self, Info.CruisingCondition);
		}

		void OnCruisingAltitudeLeft()
		{
			if (!cruising)
				return;

			cruising = false;
			if (conditionManager != null && cruisingToken != ConditionManager.InvalidConditionToken)
				cruisingToken = conditionManager.RevokeCondition(self, cruisingToken);
		}

		#endregion

		void INotifyActorDisposing.Disposing(Actor self)
		{
			UnReserve();
		}

		void IActorPreviewInitModifier.ModifyActorPreviewInit(Actor self, TypeDictionary inits)
		{
			if (!inits.Contains<DynamicFacingInit>() && !inits.Contains<FacingInit>())
				inits.Add(new DynamicFacingInit(() => Facing));
		}
	}
}
