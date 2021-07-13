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
	internal static class HarmonyPatches
	{
		public static Dictionary<Map, ProxyHeatManager> proxyHeatManagers = new Dictionary<Map, ProxyHeatManager>();
		static HarmonyPatches()
		{
			Harmony harmony = new Harmony("LongerCFloor.ProxyHeat");
			CompTemperatureSource.gasCompType = AccessTools.TypeByName("GasNetwork.CompGasTrader");
			if (CompTemperatureSource.gasCompType != null)
            {
				CompTemperatureSource.methodInfoGasOn = AccessTools.PropertyGetter(CompTemperatureSource.gasCompType, "GasOn");
            }
			harmony.PatchAll();
			if (ModLister.HasActiveModWithName("Brrr and Phew (Continued)"))
            {
				var prefix = AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.TryGiveJobPrefix));
				var postfix = AccessTools.Method(typeof(HarmonyPatches), nameof(HarmonyPatches.TryGiveJobPostfix));
				foreach (var type in GenTypes.AllSubclasses(typeof(ThinkNode_JobGiver)))
                {
					if (type.Namespace == "Brrr")
                    {
						var method = AccessTools.Method(type, "TryGiveJob");
						harmony.Patch(method, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
                    }
                }

				var genNewRRJobMethod = AccessTools.Method("Brrr.BrrrGlobals:GenNewRRJob");
				harmony.Patch(genNewRRJobMethod, new HarmonyMethod(typeof(HarmonyPatches), nameof(HarmonyPatches.GenNewRRJobPrefix)));

				var meth_JobGiver_Brrr_TryGiveJob = AccessTools.Method("Brrr.JobGiver_Brrr:TryGiveJob");
				harmony.Patch(meth_JobGiver_Brrr_TryGiveJob, null, null, new HarmonyMethod(typeof(HarmonyPatches), nameof(HarmonyPatches.JobGiver_BrrrTranspiler)));

				var meth_JobGiver_Phew_TryGiveJob = AccessTools.Method("Brrr.JobGiver_Phew:TryGiveJob");
				harmony.Patch(meth_JobGiver_Phew_TryGiveJob, null, null, new HarmonyMethod(typeof(HarmonyPatches), nameof(HarmonyPatches.JobGiver_PhewTranspiler)));
			}
		}

		public static IEnumerable<CodeInstruction> JobGiver_BrrrTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var jobDefInfo = AccessTools.Field(AccessTools.TypeByName("BrrrJobDef"), "Brrr_BrrrRecovery");
			bool found = false;
			bool inserted = false;
			var list = instructions.ToList();
			for (int i = 0; i < list.Count; i++)
			{
				var instruction = list[i];
				if (i == 173)
                {
					found = true;
				}
				if (found && !inserted)
				{
					var method = AccessTools.Method(typeof(HarmonyPatches), "BrrrJob");
					yield return new CodeInstruction(OpCodes.Ldfld, jobDefInfo);
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Call, method);
					yield return new CodeInstruction(OpCodes.Ret);
					inserted = true;
				}
				yield return instruction;
			}
		}
		public static IEnumerable<CodeInstruction> JobGiver_PhewTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var jobDefInfo = AccessTools.Field(AccessTools.TypeByName("BrrrJobDef"), "Brrr_PhewRecovery");
			bool found = false;
			bool inserted = false;
			var list = instructions.ToList();
			for (int i = 0; i < list.Count; i++)
			{
				var instruction = list[i];
				if (i == 175)
				{
					found = true;
				}
				if (found && !inserted)
				{
					var method = AccessTools.Method(typeof(HarmonyPatches), "BrrrJob");
					yield return new CodeInstruction(OpCodes.Ldfld, jobDefInfo);
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Call, method);
					yield return new CodeInstruction(OpCodes.Ret);
					inserted = true;
				}
				yield return instruction;
			}
		}
		public static Job BrrrJob(JobDef def, Pawn pawn)
		{
			var tempRange = pawn.SafeTemperatureRange();
			if (!tempRange.Includes(pawnToLookUp.AmbientTemperature))
			{
				var result = Patch_TryGiveJob.SeekSafeTemperature(def, pawn, tempRange);
				return result;
			}
			return null;
		}

		private static Pawn pawnToLookUp;
		public static void TryGiveJobPrefix(Pawn pawn)
        {
			pawnToLookUp = pawn;
		}
		public static void TryGiveJobPostfix(Pawn pawn)
		{
			pawnToLookUp = null;
		}

		public static bool GenNewRRJobPrefix(ref Job __result, JobDef def, Region reg)
		{
			if (pawnToLookUp != null)
			{
				var map = reg.Map;
				var tempRange = pawnToLookUp.ComfortableTemperatureRange();
				if (reg.Room.UsesOutdoorTemperature && proxyHeatManagers.TryGetValue(map, out ProxyHeatManager proxyHeatManager))
				{
					var candidates = new List<IntVec3>();
					foreach (var tempSource in proxyHeatManager.temperatureSources)
					{
						if (reg.Room.ContainsCell(tempSource.Key))
						{
							var result = proxyHeatManager.GetTemperatureOutcomeFor(tempSource.Key, GenTemperature.GetTemperatureForCell(tempSource.Key, map));
							if (tempRange.Includes(result))
							{
								candidates.Add(tempSource.Key);
							}
						}
					}
					candidates = candidates.OrderBy(x => pawnToLookUp.Position.DistanceTo(x)).ToList();

					foreach (var cell in candidates)
					{
						if (cell.GetFirstPawn(map) is null && pawnToLookUp.Map.pawnDestinationReservationManager.FirstReserverOf(cell, pawnToLookUp.Faction) is null
							&& pawnToLookUp.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly))
						{
							__result = JobMaker.MakeJob(def, cell);
							pawnToLookUp.Reserve(cell, __result);
							return false;
						}
					}

					foreach (var cell in candidates)
					{
						if (pawnToLookUp.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly))
						{
							__result = JobMaker.MakeJob(def, cell);
							pawnToLookUp.Reserve(cell, __result);
							return false;
						}
					}
				}
			}
			return true;
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
				else
				{
					num = GetOutDoorTemperature(room.Temperature, map, c);
				}
				__result = text + " " + num.ToStringTemperature("F0");
				return false;
			}
		
			private static float GetOutDoorTemperature(float result, Map map, IntVec3 cell)
            {
				if (proxyHeatManagers.TryGetValue(map, out ProxyHeatManager proxyHeatManager))
				{
					return proxyHeatManager.GetTemperatureOutcomeFor(cell, result);
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
					__result = proxyHeatManager.GetTemperatureOutcomeFor(__instance.Position, __result);
				}
			}
		}

		[HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRateFactor_Temperature), MethodType.Getter)]
		public static class Patch_GrowthRateFactor_Temperature
		{
			public static bool checkForPlantGrowth;
			private static void Prefix(Plant __instance)
			{
				if (ProxyHeatMod.settings.allowPlantGrowthInsideProxyHeatEffectRadius)
				{
					checkForPlantGrowth = true;
				}
			}
			private static void Postfix(Plant __instance, float __result)
			{
				checkForPlantGrowth = false;
			}
		}

		[HarmonyPatch(typeof(PlantUtility), nameof(PlantUtility.GrowthSeasonNow))]
		public static class Patch_GrowthSeasonNow
		{
			private static bool Prefix(ref bool __result, IntVec3 c, Map map, bool forSowing = false)
			{
				if (ProxyHeatMod.settings.allowPlantGrowthInsideProxyHeatEffectRadius)
                {
					if (proxyHeatManagers.TryGetValue(map, out ProxyHeatManager proxyHeatManager))
					{
						var tempResult = proxyHeatManager.GetTemperatureOutcomeFor(c, 0f);
						if (tempResult != 0)
                        {
							float temperature = c.GetTemperature(map) + tempResult;
							if (temperature > 0f)
							{
								__result = temperature < 58f;
							}
							else
                            {
								__result = false;
                            }
							return false;
						}
					}
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(GenTemperature), "TryGetTemperatureForCell")]
		public static class Patch_TryGetTemperatureForCell
		{
			private static void Postfix(bool __result, IntVec3 c, Map map, ref float tempResult)
			{
				if (__result && Patch_GrowthRateFactor_Temperature.checkForPlantGrowth)
				{
					if (proxyHeatManagers.TryGetValue(map, out ProxyHeatManager proxyHeatManager))
					{
						var plant = c.GetPlant(map);
						if (plant != null)
                        {
							tempResult = proxyHeatManager.GetTemperatureOutcomeFor(c, tempResult);
						}
					}
				}
			}
		}

		[HarmonyPatch(typeof(JobGiver_SeekSafeTemperature), "TryGiveJob")]
		public static class Patch_TryGiveJob
		{
			public static bool Prefix(Pawn pawn, ref Job __result)
            {
				if (!pawn.health.hediffSet.HasTemperatureInjury(TemperatureInjuryStage.Serious))
				{
					return false;
				}
				FloatRange tempRange = pawn.ComfortableTemperatureRange();
				if (!tempRange.Includes(pawn.AmbientTemperature))
				{
					var job = SeekSafeTemperature(JobDefOf.GotoSafeTemperature, pawn, tempRange);
					if (job != null)
                    {
						__result = job;
						return false;
                    }
				}
				return true;
			}

			public static Job SeekSafeTemperature(JobDef def, Pawn pawn, FloatRange tempRange)
			{
				var map = pawn.Map;
				if (pawn.Position.UsesOutdoorTemperature(map) && proxyHeatManagers.TryGetValue(map, out ProxyHeatManager proxyHeatManager))
				{
					var candidates = new List<IntVec3>();
					foreach (var tempSource in proxyHeatManager.temperatureSources)
					{
						var result = proxyHeatManager.GetTemperatureOutcomeFor(tempSource.Key, GenTemperature.GetTemperatureForCell(tempSource.Key, map));
						if (tempRange.Includes(result))
						{
							candidates.Add(tempSource.Key);
						}
					}
					candidates = candidates.OrderBy(x => pawn.Position.DistanceTo(x)).ToList();
					foreach (var cell in candidates)
					{
						if (cell.GetFirstPawn(map) is null && pawn.Map.pawnDestinationReservationManager.FirstReserverOf(cell, pawn.Faction) is null
							&& pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly))
						{
							var job = JobMaker.MakeJob(def, cell);
							pawn.Reserve(cell, job);
							return job;
						}
					}

					foreach (var cell in candidates)
					{
						if (pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly))
						{
							var job = JobMaker.MakeJob(def, cell);
							pawn.Reserve(cell, job);
							return job;
						}
					}
				}
				return null;
			}
		}
	}
}
