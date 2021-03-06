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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Transports actors with the `Carryable` trait.")]
	public class CarryallInfo : ITraitInfo, Requires<BodyOrientationInfo>, Requires<AircraftInfo>
	{
		[Desc("Delay on the ground while attaching an actor to the carryall.")]
		public readonly int LoadingDelay = 0;

		[Desc("Delay on the ground while detacting an actor to the carryall.")]
		public readonly int UnloadingDelay = 0;

		[Desc("Carryable attachment point relative to body.")]
		public readonly WVec LocalOffset = WVec.Zero;

		[Desc("Radius around the target drop location that are considered if the target tile is blocked.")]
		public readonly WDist DropRange = WDist.FromCells(5);

		[Desc("Cursor to display when able to unload the passengers.")]
		public readonly string UnloadCursor = "deploy";

		[Desc("Cursor to display when unable to unload the passengers.")]
		public readonly string UnloadBlockedCursor = "deploy-blocked";

		[Desc("Allow moving and unloading with one order using force-move")]
		public readonly bool AllowDropOff = false;

		[Desc("Cursor to display when able to drop off the passengers at location.")]
		public readonly string DropOffCursor = "ability";

		[Desc("Cursor to display when unable to drop off the passengers at location.")]
		public readonly string DropOffBlockedCursor = "move-blocked";

		[VoiceReference]
		public readonly string Voice = "Action";

		public virtual object Create(ActorInitializer init) { return new Carryall(init.Self, this); }
	}

	public class Carryall : INotifyKilled, ISync, ITick, IRender, INotifyActorDisposing, IIssueOrder, IResolveOrder,
		IOrderVoice, IIssueDeployOrder
	{
		public enum CarryallState
		{
			Idle,
			Reserved,
			Carrying
		}

		public readonly CarryallInfo Info;
		readonly AircraftInfo aircraftInfo;
		readonly Aircraft aircraft;
		readonly BodyOrientation body;
		readonly IMove move;
		readonly IFacing facing;
		readonly Actor self;

		// The actor we are currently carrying.
		[Sync] public Actor Carryable { get; private set; }
		public CarryallState State { get; private set; }

		int cachedFacing;
		IActorPreview[] carryablePreview = null;

		/// <summary>Offset between the carryall's and the carried actor's CenterPositions</summary>
		public WVec CarryableOffset { get; private set; }

		public Carryall(Actor self, CarryallInfo info)
		{
			Info = info;

			Carryable = null;
			State = CarryallState.Idle;

			aircraftInfo = self.Info.TraitInfoOrDefault<AircraftInfo>();
			aircraft = self.Trait<Aircraft>();
			body = self.Trait<BodyOrientation>();
			move = self.Trait<IMove>();
			facing = self.Trait<IFacing>();
			this.self = self;
		}

		void ITick.Tick(Actor self)
		{
			// Cargo may be killed in the same tick as, but after they are attached
			if (Carryable != null && Carryable.IsDead)
				DetachCarryable(self);

			// HACK: We don't have an efficient way to know when the preview
			// bounds change, so assume that we need to update the screen map
			// (only) when the facing changes
			if (facing.Facing != cachedFacing && carryablePreview != null)
			{
				self.World.ScreenMap.AddOrUpdate(self);
				cachedFacing = facing.Facing;
			}
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (State == CarryallState.Carrying)
			{
				Carryable.Dispose();
				Carryable = null;
			}

			UnreserveCarryable(self);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (State == CarryallState.Carrying)
			{
				if (!Carryable.IsDead)
				{
					var positionable = Carryable.Trait<IPositionable>();
					positionable.SetPosition(Carryable, self.Location);
					Carryable.Kill(e.Attacker);
				}

				Carryable = null;
			}

			UnreserveCarryable(self);
		}

		public virtual bool RequestTransportNotify(Actor self, Actor carryable, CPos destination)
		{
			return false;
		}

		public virtual WVec OffsetForCarryable(Actor self, Actor carryable)
		{
			return Info.LocalOffset - carryable.Info.TraitInfo<CarryableInfo>().LocalOffset;
		}

		public virtual bool AttachCarryable(Actor self, Actor carryable)
		{
			if (State == CarryallState.Carrying)
				return false;

			Carryable = carryable;
			State = CarryallState.Carrying;
			self.World.ScreenMap.AddOrUpdate(self);

			CarryableOffset = OffsetForCarryable(self, carryable);
			aircraft.AddLandingOffset(-CarryableOffset.Z);
			return true;
		}

		public virtual void DetachCarryable(Actor self)
		{
			UnreserveCarryable(self);
			self.World.ScreenMap.AddOrUpdate(self);

			carryablePreview = null;
			aircraft.SubtractLandingOffset(-CarryableOffset.Z);
			CarryableOffset = WVec.Zero;
		}

		public virtual bool ReserveCarryable(Actor self, Actor carryable)
		{
			if (State == CarryallState.Reserved)
				UnreserveCarryable(self);

			if (State != CarryallState.Idle || !carryable.Trait<Carryable>().Reserve(carryable, self))
				return false;

			Carryable = carryable;
			State = CarryallState.Reserved;
			return true;
		}

		public virtual void UnreserveCarryable(Actor self)
		{
			if (Carryable != null && Carryable.IsInWorld && !Carryable.IsDead)
				Carryable.Trait<Carryable>().UnReserve(Carryable);

			Carryable = null;
			State = CarryallState.Idle;
		}

		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (State == CarryallState.Carrying && !Carryable.IsDead)
			{
				if (carryablePreview == null)
				{
					var carryableInits = new TypeDictionary()
					{
						new OwnerInit(Carryable.Owner),
						new DynamicFacingInit(() => facing.Facing),
					};

					foreach (var api in Carryable.TraitsImplementing<IActorPreviewInitModifier>())
						api.ModifyActorPreviewInit(Carryable, carryableInits);

					var init = new ActorPreviewInitializer(Carryable.Info, wr, carryableInits);
					carryablePreview = Carryable.Info.TraitInfos<IRenderActorPreviewInfo>()
						.SelectMany(rpi => rpi.RenderPreview(init))
						.ToArray();
				}

				var offset = body.LocalToWorld(CarryableOffset.Rotate(body.QuantizeOrientation(self, self.Orientation)));
				var previewRenderables = carryablePreview
					.SelectMany(p => p.Render(wr, self.CenterPosition + offset))
					.OrderBy(WorldRenderer.RenderableScreenZPositionComparisonKey);

				foreach (var r in previewRenderables)
					yield return r;
			}
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			if (carryablePreview == null)
				yield break;

			var pos = self.CenterPosition;
			foreach (var p in carryablePreview)
				foreach (var b in p.ScreenBounds(wr, pos))
					yield return b;
		}

		// Check if we can drop the unit at our current location.
		public bool CanUnload()
		{
			var localOffset = CarryableOffset.Rotate(body.QuantizeOrientation(self, self.Orientation));
			var targetCell = self.World.Map.CellContaining(self.CenterPosition + body.LocalToWorld(localOffset));
			return Carryable != null && Carryable.Trait<IPositionable>().CanEnterCell(targetCell, self);
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				yield return new CarryallPickupOrderTargeter();
				yield return new DeployOrderTargeter("Unload", 10,
				() => CanUnload() ? Info.UnloadCursor : Info.UnloadBlockedCursor);
				yield return new CarryallDeliverUnitTargeter(aircraftInfo, Info, CarryableOffset);
			}
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, Target target, bool queued)
		{
			if (order.OrderID == "PickupUnit" || order.OrderID == "DeliverUnit" || order.OrderID == "Unload")
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			return new Order("Unload", self, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self) { return true; }

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "DeliverUnit")
			{
				var cell = self.World.Map.Clamp(self.World.Map.CellContaining(order.Target.CenterPosition));
				if (!aircraftInfo.MoveIntoShroud && !self.Owner.Shroud.IsExplored(cell))
					return;

				var targetLocation = move.NearestMoveableCell(cell);
				self.SetTargetLine(Target.FromCell(self.World, targetLocation), Color.Yellow);
				self.QueueActivity(order.Queued, new DeliverUnit(self, targetLocation));
			}
			else if (order.OrderString == "Unload")
			{
				if (!order.Queued && !CanUnload())
					return;

				self.QueueActivity(order.Queued, new DeliverUnit(self));
			}

			if (order.OrderString == "PickupUnit")
			{
				if (order.Target.Type != TargetType.Actor)
					return;

				if (!order.Queued)
					self.CancelActivity();

				self.SetTargetLine(order.Target, Color.Yellow);
				self.QueueActivity(order.Queued, new PickupUnit(self, order.Target.Actor, Info.LoadingDelay));
			}
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			switch (order.OrderString)
			{
				case "DeliverUnit":
				case "Unload":
				case "PickupUnit":
					return Info.Voice;
				default:
					return null;
			}
		}

		class CarryallPickupOrderTargeter : UnitOrderTargeter
		{
			public CarryallPickupOrderTargeter()
				: base("PickupUnit", 5, "ability", false, true)
			{
			}

			static bool CanTarget(Actor self, Actor target)
			{
				if (target == null || !target.AppearsFriendlyTo(self))
					return false;

				var carryable = target.TraitOrDefault<Carryable>();
				if (carryable == null || carryable.IsTraitDisabled)
					return false;

				if (carryable.Reserved && carryable.Carrier != self)
					return false;

				return true;
			}

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				return CanTarget(self, target);
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				return CanTarget(self, target.Actor);
			}
		}

		class CarryallDeliverUnitTargeter : AircraftMoveOrderTargeter
		{
			readonly AircraftInfo aircraftInfo;
			readonly CarryallInfo info;
			readonly WVec carryableOffset;

			public CarryallDeliverUnitTargeter(AircraftInfo aircraftInfo, CarryallInfo info, WVec carryableOffset)
				: base(aircraftInfo)
			{
				OrderID = "DeliverUnit";
				OrderPriority = 6;
				this.carryableOffset = carryableOffset;
				this.aircraftInfo = aircraftInfo;
				this.info = info;
			}

			public override bool CanTarget(Actor self, Target target, List<Actor> othersAtTarget, ref TargetModifiers modifiers, ref string cursor)
			{
				if (!info.AllowDropOff || !modifiers.HasModifier(TargetModifiers.ForceMove))
					return false;

				cursor = info.DropOffCursor;
				var type = target.Type;

				if ((type == TargetType.Actor && target.Actor.Info.HasTraitInfo<BuildingInfo>())
					|| (target.Type == TargetType.FrozenActor && target.FrozenActor.Info.HasTraitInfo<BuildingInfo>()))
				{
					cursor = info.DropOffBlockedCursor;
					return true;
				}

				var location = self.World.Map.CellContaining(target.CenterPosition);
				var explored = self.Owner.Shroud.IsExplored(location);
				cursor = self.World.Map.Contains(location) ?
					(self.World.Map.GetTerrainInfo(location).CustomCursor ?? info.DropOffCursor) :
					info.DropOffBlockedCursor;

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				if (!explored && !aircraftInfo.MoveIntoShroud)
					cursor = info.DropOffBlockedCursor;

				return true;
			}
		}
	}
}
