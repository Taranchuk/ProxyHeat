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
	[StaticConstructorOnStartup]
	internal static class HarmonyInit
	{
		public static Dictionary<Map, ProxyHeatManager> proxyHeatManagers = new Dictionary<Map, ProxyHeatManager>();
		static HarmonyInit()
		{
			Harmony harmony = new Harmony("LongerCFloor.ProxyHeat");
			harmony.PatchAll();
		}

		[HarmonyPatch(typeof(Building), nameof(Building.SpawnSetup))]
		public static class Patch_SpawnSetup
		{
			private static void Postfix(Building __instance)
			{
				if (proxyHeatManagers.TryGetValue(__instance.Map, out ProxyHeatManager proxyHeatManager))
                {
					foreach (var comp in proxyHeatManager.compTemperatures)
                    {
						if (comp.InRangeAndActive(__instance.Position))
                        {
							proxyHeatManager.MarkDirty(comp);
						}
                    }
                }
			}
		}

		[HarmonyPatch(typeof(Building), nameof(Building.DeSpawn))]
		public static class Patch_DeSpawn
		{
			private static void Prefix(Building __instance)
			{
				if (proxyHeatManagers.TryGetValue(__instance.Map, out ProxyHeatManager proxyHeatManager))
				{
					foreach (var comp in proxyHeatManager.compTemperatures)
					{
						if (comp.InRangeAndActive(__instance.Position))
						{
							proxyHeatManager.MarkDirty(comp);
						}
					}
				}
			}
		}

		[HarmonyPatch(typeof(GlobalControls), "TemperatureString")]
		public static class Patch_TemperatureString
		{
			private static string indoorsUnroofedStringCached;

			private static int indoorsUnroofedStringCachedRoofCount = -1;

			private static bool Prefix(ref string __result)
			{
				IntVec3 intVec = UI.MouseCell();
				IntVec3 c = intVec;
				Room room = intVec.GetRoom(Find.CurrentMap, RegionType.Set_All);
				if (room == null)
				{
					for (int i = 0; i < 9; i++)
					{
						IntVec3 intVec2 = intVec + GenAdj.AdjacentCellsAndInside[i];
						if (intVec2.InBounds(Find.CurrentMap))
						{
							Room room2 = intVec2.GetRoom(Find.CurrentMap, RegionType.Set_All);
							if (room2 != null && ((!room2.PsychologicallyOutdoors && !room2.UsesOutdoorTemperature) || (!room2.PsychologicallyOutdoors && (room == null || room.PsychologicallyOutdoors)) || (room2.PsychologicallyOutdoors && room == null)))
							{
								c = intVec2;
								room = room2;
							}
						}
					}
				}
				if (room == null && intVec.InBounds(Find.CurrentMap))
				{
					Building edifice = intVec.GetEdifice(Find.CurrentMap);
					if (edifice != null)
					{
						foreach (IntVec3 item in edifice.OccupiedRect().ExpandedBy(1).ClipInsideMap(Find.CurrentMap))
						{
							room = item.GetRoom(Find.CurrentMap, RegionType.Set_All);
							if (room != null && !room.PsychologicallyOutdoors)
							{
								c = item;
								break;
							}
						}
					}
				}
				string text;
				if (c.InBounds(Find.CurrentMap) && !c.Fogged(Find.CurrentMap) && room != null && !room.PsychologicallyOutdoors)
				{
					if (room.OpenRoofCount == 0)
					{
						text = "Indoors".Translate();
					}
					else
					{
						if (indoorsUnroofedStringCachedRoofCount != room.OpenRoofCount)
						{
							indoorsUnroofedStringCached = "IndoorsUnroofed".Translate() + " (" + room.OpenRoofCount.ToStringCached() + ")";
							indoorsUnroofedStringCachedRoofCount = room.OpenRoofCount;
						}
						text = indoorsUnroofedStringCached;
					}
				}
				else
				{
					text = "Outdoors".Translate();
				}
				var map = Find.CurrentMap;
				float num = 0f;
				if (room == null || c.Fogged(map))
                {
					num = GetOutDoorTemperature(Find.CurrentMap.mapTemperature.OutdoorTemp, map, c);
				}
				else if (room.UsesOutdoorTemperature)
				{
					num = GetOutDoorTemperature(room.Temperature, map, c);
				}
				else
                {
					num = room.Temperature;
                }
				__result = text + " " + num.ToStringTemperature("F0");
				return false;
			}

			private static float GetOutDoorTemperature(float result, Map map, IntVec3 cell)
            {
				if (proxyHeatManagers.TryGetValue(map, out ProxyHeatManager proxyHeatManager))
				{
					if (proxyHeatManager.temperatureSources.TryGetValue(cell, out List<CompTemperatureSource> tempSources))
					{
						foreach (var tempSourceCandidate in tempSources)
						{
							Log.Message(cell + " - " + tempSourceCandidate);
							result += tempSourceCandidate.TemperatureOutcome;
						}
					}
				}
				return result;
			}
		}

		[HarmonyPatch(typeof(Thing), nameof(Thing.AmbientTemperature), MethodType.Getter)]
		public static class Patch_AmbientTemperature
		{
			private static void Postfix(Thing __instance, ref float __result)
			{
				var map = __instance.Map;
				if (map != null && proxyHeatManagers.TryGetValue(map, out ProxyHeatManager proxyHeatManager))
				{
					if (proxyHeatManager.temperatureSources.TryGetValue(__instance.Position, out List<CompTemperatureSource> tempSources))
					{
						foreach (var tempSourceCandidate in tempSources)
						{
							__result += tempSourceCandidate.TemperatureOutcome;
						}
					}
				}
			}
		}


		[HarmonyPatch(typeof(JobGiver_SeekSafeTemperature), "TryGiveJob")]
		public static class Patch_TryGiveJob
		{
			private static bool Prefix(Pawn pawn, ref Job __result)
            {
				if (!pawn.health.hediffSet.HasTemperatureInjury(TemperatureInjuryStage.Serious))
				{
					return false;
				}
				FloatRange tempRange = pawn.ComfortableTemperatureRange();
				if (!tempRange.Includes(pawn.AmbientTemperature))
				{
					var job = SeekSafeTemperature(pawn, tempRange);
					if (job != null)
                    {
						__result = job;
						return false;
                    }
				}
				return true;
			}

			private static Job SeekSafeTemperature(Pawn pawn, FloatRange tempRange)
			{
				Log.Message("SeekSafeTemperature: " + pawn);
				var map = pawn.Map;
				if (pawn.Position.UsesOutdoorTemperature(map) && proxyHeatManagers.TryGetValue(map, out ProxyHeatManager proxyHeatManager))
				{
					var candidates = new List<IntVec3>();
					foreach (var tempSource in proxyHeatManager.temperatureSources)
                    {
						var result = GenTemperature.GetTemperatureForCell(tempSource.Key, map);
						foreach (var comp in tempSource.Value)
                        {
							result += comp.TemperatureOutcome;
                        }
						if (tempRange.Includes(result))
						{
							candidates.Add(tempSource.Key);
						}
					}
					candidates = candidates.OrderBy(x => pawn.Position.DistanceTo(x)).ToList();
					while (candidates.Any())
					{
						var list = candidates.Take(10).InRandomOrder();
						candidates = candidates.Skip(10).ToList();
						foreach (var affectedCell in list)
						{
							if (pawn.CanReserveAndReach(affectedCell, PathEndMode.OnCell, Danger.Deadly))
							{
								Log.Message("Return job: " + pawn + " - " + affectedCell);
								return JobMaker.MakeJob(JobDefOf.GotoSafeTemperature, affectedCell);
							}
						}
					}
				}
				Log.Message("Return null - SeekSafeTemperature: " + pawn);
				return null;
			}
		}
	}
}
