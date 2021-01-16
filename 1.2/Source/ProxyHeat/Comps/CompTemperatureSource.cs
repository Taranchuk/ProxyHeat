using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
		public float? minTemperature;
		public float? maxTemperature;
		public bool dependsOnPower;
		public bool dependsOnFuel;
		public bool dependsOnGas;
		public bool flickable;
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
		private ThingComp gasComp;
		private CompRefuelable fuelComp;
		private CompFlickable compFlickable;
		private CompTempControl tempControlComp;
		public static MethodInfo methodInfoGasOn;
		public static Type gasCompType;
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
			if (Props.dependsOnGas)
			{
				gasComp = GetGasComp();
				Log.Message(gasComp + " - " + methodInfoGasOn + " - " + gasCompType);
			}
			if (Props.flickable)
            {
				compFlickable = this.parent.GetComp<CompFlickable>();
			}
			if (!Props.dependsOnFuel && !Props.dependsOnPower)
            {
				active = true;
            }

			tempControlComp = this.parent.GetComp<CompTempControl>();
			this.position = this.parent.Position;
			this.map = this.parent.Map;
			this.proxyHeatManager = this.map.GetComponent<ProxyHeatManager>();
			Log.Message(this.parent.def + " - is spawned as a temp source");
			if (Props.dependsOnPower || Props.dependsOnFuel || Props.dependsOnGas || Props.flickable)
			{
				Log.Message("Adding " + this + " to compTemperaturesToTick");
				this.proxyHeatManager.compTemperaturesToTick.Add(this);
			}

			this.MarkDirty();
		}

		private ThingComp GetGasComp()
        {
			foreach (var comp in this.parent.AllComps)
			{
				if (comp.GetType() == gasCompType)
				{
					return comp;
				}
			}
			return null;
		}
		public override void PostDeSpawn(Map map)
		{
			base.PostDeSpawn(map);
			proxyHeatManager.RemoveComp(this);
			if (proxyHeatManager.compTemperaturesToTick.Contains(this))
            {
				proxyHeatManager.compTemperaturesToTick.Remove(this);
			}
		}
		public void MarkDirty()
        {
			this.proxyHeatManager.MarkDirty(this);
			this.dirty = false;
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
				affectedCells.AddRange(this.parent.OccupiedRect().Where(x => x.UsesOutdoorTemperature(map)));
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
				return this.parent.OccupiedRect().MovedBy(this.Props.tileOffset.RotatedBy(this.parent.Rotation)).Cells.Where(x => x.UsesOutdoorTemperature(map));
			}
			else
			{
				return this.parent.OccupiedRect().Cells.Where(x => x.UsesOutdoorTemperature(map));
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
		
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
			proxyHeatManager.RemoveComp(this);
		}
		
		public bool dirty = false;
		private void SetActive(bool value)
        {
			this.active = value;
			this.dirty = true;
        }

		public void TempTick()
        {
			if (this.parent.def.defName.ToLower().Contains("gas"))
			{
				Log.Message(this.compFlickable + " - " + this.powerComp + " - " + this.tempControlComp + " - " + this.fuelComp);
			}
			if (compFlickable != null)
            {
				if (!compFlickable.SwitchIsOn)
                {
					if (this.active)
					{
						SetActive(false);
						RecalculateAffectedCells();
						if (proxyHeatManager.compTemperatures.Contains(this))
                        {
							proxyHeatManager.RemoveComp(this);
                        }
					}
					return;
				}
			}
		
			if (Props.dependsOnFuel && Props.dependsOnPower)
            {
				if (powerComp != null && powerComp.PowerOn && fuelComp != null && fuelComp.HasFuel)
                {
					if (!this.active)
                    {
						Log.Message("1");
						this.SetActive(true);
					}
				}
				else if (this.active)
                {
					Log.Message("2");
					this.SetActive(false);
                }
            }
		
			else if (powerComp != null)
            {
				if (!powerComp.PowerOn && this.active)
                {
					Log.Message("3");
					this.SetActive(false);
				}
				else if (powerComp.PowerOn && !this.active)
				{
					Log.Message("4");
					this.SetActive(true);
				}
			}
		
			else if (fuelComp != null)
            {
				if (!fuelComp.HasFuel && this.active)
                {
					Log.Message("5");
					this.SetActive(false);
				}
				else if (fuelComp.HasFuel && !this.active)
				{
					Log.Message("6");
					this.SetActive(true);
				}
            }
			else if (gasComp != null)
            {
				if (!(bool)methodInfoGasOn.Invoke(gasComp, null) && this.active)
                {
					Log.Message("7");
					this.SetActive(false);
				}
				else if ((bool)methodInfoGasOn.Invoke(gasComp, null) && !this.active)
                {
					Log.Message("8");
					this.SetActive(true);
				}

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
