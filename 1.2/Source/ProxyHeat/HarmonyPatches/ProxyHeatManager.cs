using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ProxyHeat
{
	public class ProxyHeatManager : MapComponent
	{
        public bool dirty = false;

        public Dictionary<IntVec3, List<CompTemperatureSource>> temperatureSources = new Dictionary<IntVec3, List<CompTemperatureSource>>();
        public List<CompTemperatureSource> compTemperatures = new List<CompTemperatureSource>();
        private List<CompTemperatureSource> dirtyComps = new List<CompTemperatureSource>();
        public ProxyHeatManager(Map map) : base(map)
		{
            HarmonyInit.proxyHeatManagers[map] = this;
		}
        public void MarkDirty(CompTemperatureSource comp)
        {
            dirtyComps.Add(comp);
            dirty = true;
        }
        public void RemoveComp(CompTemperatureSource comp)
        {
            Log.Message("Removing: " + comp);
            if (compTemperatures.Contains(comp))
            {
                compTemperatures.Remove(comp);
            }
            foreach (var data in temperatureSources.Values)
            {
                if (data.Contains(comp))
                {
                    data.Remove(comp);
                }
            }
        }
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (this.dirty)
            {
                foreach (var comp in dirtyComps)
                {
                    if (comp != null && comp.parent.Spawned)
                    {
                        comp.RecalculateAffectedCells();
                        Log.Message(comp + " recalc");
                    }
                }
                dirtyComps.Clear();
                this.dirty = false;
            }
        }
    }
}
