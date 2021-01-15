﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace ProxyHeat
{
	public class CompProperties_TemperatureSource : CompProperties
	{
		public float radius;
		public float tempOutcome;
		public bool dependsOnPower;
		public bool dependsOnFuel;
		public IntVec3 tileOffset = IntVec3.Invalid;
		public CompProperties_TemperatureSource()
		{
			compClass = typeof(CompTemperatureSource);
		}
	}

	public class CompTemperatureSource : ThingComp
    {
		public CompProperties_TemperatureSource Props => (CompProperties_TemperatureSource)props;
		private bool active;
		private Map map;

		private CompPowerTrader powerComp;
		private CompRefuelable fuelComp;
		private CompTempControl tempControlComp;
		public IntVec3 position;
		private HashSet<IntVec3> affectedCells = new HashSet<IntVec3>();
		public HashSet<IntVec3> AffectedCells => affectedCells;
		private List<IntVec3> affectedCellsList = new List<IntVec3>();
		private ProxyHeatManager proxyHeatManager;
		public float TemperatureOutcome
        {
			get
            {
				if (tempControlComp != null)
                {
					return tempControlComp.targetTemperature;
				}
				return this.Props.tempOutcome;
            }
        }
		public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
			if (Props.dependsOnPower)
            {
				powerComp = this.parent.GetComp<CompPowerTrader>();
            }
			if (Props.dependsOnFuel)
            {
				fuelComp = this.parent.GetComp<CompRefuelable>();
			}
			tempControlComp = this.parent.GetComp<CompTempControl>();

			this.position = this.parent.Position;
			this.map = this.parent.Map;
			this.proxyHeatManager = this.map.GetComponent<ProxyHeatManager>();
			this.MarkDirty();
		}
        public override void PostPostMake()
        {
            base.PostPostMake();
        }

		public void MarkDirty()
        {
			this.proxyHeatManager.MarkDirty(this);
			Log.Message("Marking dirty");
        }
        public void RecalculateAffectedCells()
        {
			affectedCells.Clear();
			affectedCellsList.Clear();
			proxyHeatManager.RemoveComp(this);

			if (this.active)
            {
				HashSet<IntVec3> tempCells = new HashSet<IntVec3>();
				foreach (var cell in GetCells())
				{
					foreach (var intVec in GenRadial.RadialCellsAround(cell, Props.radius, true))
					{
						tempCells.Add(intVec);
					}
				}

				Predicate<IntVec3> validator = delegate (IntVec3 cell)
				{
					if (!tempCells.Contains(cell)) return false;
					var edifice = cell.GetEdifice(map);
					var result = edifice == null || edifice.def.passability != Traversability.Impassable || edifice == this.parent;
					return result;
				};

				var offset = this.Props.tileOffset != IntVec3.Invalid ? this.parent.OccupiedRect().MovedBy(this.Props.tileOffset.RotatedBy(this.parent.Rotation)).CenterCell : position;
				map.floodFiller.FloodFill(offset, validator, delegate (IntVec3 x)
				{
					if (tempCells.Contains(x))
					{
						var edifice = x.GetEdifice(map);
						var result = edifice == null || edifice.def.passability != Traversability.Impassable || edifice == this.parent;
						if (result && (GenSight.LineOfSight(offset, x, map) || offset.DistanceTo(x) <= 1.5f))
						{
							affectedCells.Add(x);
						}
					}
				}, int.MaxValue, rememberParents: false, (IEnumerable<IntVec3>)null);
				affectedCells.AddRange(this.parent.OccupiedRect());
				affectedCellsList.AddRange(affectedCells.ToList());
				foreach (var cell in affectedCells)
				{
					if (proxyHeatManager.temperatureSources.ContainsKey(cell))
					{
						proxyHeatManager.temperatureSources[cell].Add(this);
					}
					else
					{
						proxyHeatManager.temperatureSources[cell] = new List<CompTemperatureSource> { this };
					}
				}
				proxyHeatManager.compTemperatures.Add(this);
			}
		}

		public IEnumerable<IntVec3> GetCells()
        {
			if (this.Props.tileOffset != IntVec3.Invalid)
			{
				return this.parent.OccupiedRect().MovedBy(this.Props.tileOffset.RotatedBy(this.parent.Rotation)).Cells;
			}
			else
			{
				return this.parent.OccupiedRect().Cells;
			}
		}
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
			if (this.TemperatureOutcome >= 0)
            {
				GenDraw.DrawFieldEdges(affectedCellsList, GenTemperature.ColorRoomHot);
            }
			else
            {
				GenDraw.DrawFieldEdges(affectedCellsList, GenTemperature.ColorRoomCold);
			}
		}
		public override void PostDeSpawn(Map map)
        {
            base.PostDeSpawn(map);
			proxyHeatManager.RemoveComp(this);
		}

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
			proxyHeatManager.RemoveComp(this);
		}

		public override void CompTick()
        {
            base.CompTick();
			bool dirty = false;
			if (!Props.dependsOnFuel && !Props.dependsOnPower)
            {
				if (!this.active)
                {
					this.active = true;
					dirty = true;
				}
            }
			if (powerComp != null)
            {
				if (!powerComp.PowerOn) this.active = false;
				else if (!this.active)
				{
					this.active = true;
					dirty = true;
				}
			}
			if (fuelComp != null)
            {
				if (!fuelComp.HasFuel) this.active = false;
				else if (!this.active)
				{
					this.active = true;
					dirty = true;
				}
            }
			if (!this.active && this.affectedCells.Any())
            {
				this.affectedCells.Clear();
				this.affectedCellsList.Clear();
				dirty = true;
            }
			if (dirty)
			{
				MarkDirty();
			}
		}

		public bool InRangeAndActive(IntVec3 nearByCell)
		{
			if (this.active && this.position.DistanceTo(nearByCell) <= Props.radius)
			{
				return true;
			}
			return false;
		}
		public override void PostExposeData()
        {
            base.PostExposeData();
			Scribe_Values.Look(ref active, "active");
        }
    }
}
