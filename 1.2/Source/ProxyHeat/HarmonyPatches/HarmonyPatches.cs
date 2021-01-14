using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace ProxyHeat
{
	[StaticConstructorOnStartup]
	internal static class HarmonyInit
	{
		public static Dictionary<Map, List<CompTemperatureSource>> compTemperatureSources = new Dictionary<Map, List<CompTemperatureSource>>();
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
				var thingMap = __instance.Map;
				if (thingMap != null && compTemperatureSources.TryGetValue(thingMap, out List<CompTemperatureSource> tempSources))
				{
					var position = __instance.Position;
					for (int i = 0; i < tempSources.Count; i++)
					{
						var compTempSource = tempSources[i];
						if (compTempSource.Active && compTempSource.IsNearby(position))
						{
							compTempSource.MarkDirty();
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
				var thingMap = __instance.Map;
				if (thingMap != null && compTemperatureSources.TryGetValue(thingMap, out List<CompTemperatureSource> tempSources))
				{
					var position = __instance.Position;
					for (int i = 0; i < tempSources.Count; i++)
					{
						var compTempSource = tempSources[i];
						if (compTempSource.Active && compTempSource.InRange(position))
						{
							compTempSource.MarkDirty();
						}
					}
				}
			}
		}


		[HarmonyPatch(typeof(Thing), nameof(Thing.AmbientTemperature), MethodType.Getter)]
		public static class Patch_AmbientTemperature
		{
			private static void Postfix(Thing __instance, ref float __result)
			{
				var thingMap = __instance.Map;
				if (thingMap != null && compTemperatureSources.TryGetValue(thingMap, out List<CompTemperatureSource> tempSources) && __instance is Pawn)
                {
					var tempSourceCandidates = new List<CompTemperatureSource>();
					var position = __instance.Position;
					for (int i = 0; i < tempSources.Count; i++)
					{
						var compTempSource = tempSources[i];
						if (compTempSource.Active && compTempSource.IsNearby(position))
                        {
							tempSourceCandidates.Add(compTempSource);
                        }
					}
					if (tempSourceCandidates.Any())
                    {
						var tempSource = tempSourceCandidates.OrderBy(x => x.position.DistanceTo(position)).First();
						__result = tempSource.Props.tempOutcome;
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
				if (pawn.Position.UsesOutdoorTemperature(map) && HarmonyInit.compTemperatureSources.TryGetValue(map, out List<CompTemperatureSource> temperatureSources))
				{
					var candidates = temperatureSources.Where(x => tempRange.Includes(x.Props.tempOutcome)).OrderBy(x => pawn.Position.DistanceTo(x.position)).ToList();
					while (candidates.Any())
					{
						var candidate = candidates.First();
						var cells = candidate.AffectedCells.OrderBy(x => candidate.position.DistanceTo(x)).ToList();
						while (cells.Any())
                        {
							var list = cells.Take(10).InRandomOrder();
							cells = cells.Skip(10).ToList();
							foreach (var affectedCell in list)
							{
								if (pawn.CanReserveAndReach(affectedCell, PathEndMode.OnCell, Danger.Deadly))
								{
									Log.Message("Return job: " + pawn + " - " + affectedCell);
									return JobMaker.MakeJob(JobDefOf.GotoSafeTemperature, affectedCell);
								}

							}
						}
						candidates.Remove(candidate);
					}
				}
				Log.Message("Return null - SeekSafeTemperature: " + pawn);
				return null;
			}
		}
	}
}
