using RimWorld;
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
		public IntVec3 position;
		private HashSet<IntVec3> affectedCells = new HashSet<IntVec3>();
		public HashSet<IntVec3> AffectedCells => affectedCells;
		private List<IntVec3> affectedCellsList = new List<IntVec3>();

		private ProxyHeatManager proxyHeatManager;
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
        }
        public void RecalculateAffectedCells()
        {
			affectedCells.Clear();
			affectedCellsList.Clear();
			proxyHeatManager.RemoveComp(this);
			Func<IntVec3, bool> validator = delegate (IntVec3 cell)
			{
				if (!cell.Walkable(map))
                {
					return false;
                }
				var edifice = cell.GetEdifice(map);
				var result = edifice == null || edifice.def.passability != Traversability.Impassable;
				return result;
			};
			foreach (var cell in this.parent.OccupiedRect().Cells)
            {
				foreach (var intVec in GenRadial.RadialCellsAround(cell, Props.radius, true))
				{
					if (!affectedCells.Contains(intVec) && GenSight.LineOfSight(cell, intVec, map, validator: validator))
					{
						var edifice = intVec.GetEdifice(map);
						if (edifice == null || edifice.def.passability != Traversability.Impassable)
						{
							affectedCells.Add(intVec);
						}
					}
				}
			}

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
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
			if (Props.tempOutcome >= 0)
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
            }
			else if (dirty)
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
