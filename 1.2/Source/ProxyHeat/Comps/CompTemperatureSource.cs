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
		public int radius;
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
		public bool Active => active;// && position.UsesOutdoorTemperature(map);
		private CompPowerTrader powerComp;
		private CompRefuelable fuelComp;
		public IntVec3 position;
		private HashSet<IntVec3> affectedCells = new HashSet<IntVec3>();
		public HashSet<IntVec3> AffectedCells => affectedCells;
		private List<IntVec3> affectedCellsList = new List<IntVec3>();
		private bool dirty;
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
			this.dirty = true;
			if (HarmonyInit.compTemperatureSources.ContainsKey(map))
            {
				HarmonyInit.compTemperatureSources[map].Add(this);
			}
			else
            {
				HarmonyInit.compTemperatureSources[map] = new List<CompTemperatureSource> { this };
			}
		}

        public override void PostPostMake()
        {
            base.PostPostMake();
        }

		public void MarkDirty()
        {
			this.dirty = true;
        }
        public void RecalculateAffectedCells()
        {
			Log.Message(this + " - RecalculateAffectedCells");
			affectedCells.Clear();
			affectedCellsList.Clear();
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
			foreach (var intVec in GenRadial.RadialCellsAround(position, Props.radius, true))
			{
				if (GenSight.LineOfSight(position, intVec, map, validator: validator))
                {
					var edifice = intVec.GetEdifice(map);
					if (edifice == null || edifice.def.passability != Traversability.Impassable)
                    {
						if (!this.parent.OccupiedRect().Contains(intVec))
                        {
							affectedCells.Add(intVec);
                        }
					}
				}
			}
			affectedCellsList.AddRange(affectedCells.ToList());
			this.dirty = false;
		}
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
			Log.Message(affectedCellsList.Count.ToString());
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
			if (HarmonyInit.compTemperatureSources.ContainsKey(map))
			{
				HarmonyInit.compTemperatureSources[map].Remove(this);
			}
		}

		public override void CompTick()
        {
            base.CompTick();
			if (!Props.dependsOnFuel && !Props.dependsOnPower)
            {
				if (!this.active)
                {
					this.active = true;
					this.dirty = true;
				}
            }
			if (powerComp != null)
            {
				if (!powerComp.PowerOn) this.active = false;
				else if (!this.active)
				{
					this.active = true;
					this.dirty = true;
				}
			}
			if (fuelComp != null)
            {
				if (!fuelComp.HasFuel) this.active = false;
				else if (!this.active)
				{
					this.active = true;
					this.dirty = true;
				}
            }
			if (!this.active && this.affectedCells.Any())
            {
				this.affectedCells.Clear();
				this.affectedCellsList.Clear();
            }
			else if (this.dirty)
			{
				RecalculateAffectedCells();
			}
		}

		public bool IsNearby(IntVec3 nearByCell)
        {
			if (affectedCells.Contains(nearByCell))
            {
				return true;
            }
			return false;
        }

		public bool InRange(IntVec3 nearByCell)
		{
			if (this.position.DistanceTo(nearByCell) <= Props.radius)
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
