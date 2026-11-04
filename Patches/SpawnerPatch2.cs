using Il2Cpp;

namespace CottonLibrary.Patches;

public class SpawnerPatch2
{
    private static bool Replacement(DirectedActorSpawner __instance, ref IdentifiableType __result, IdentifiableType id)
    {
        if (!__instance)
        {
            return false;
        }
        if (__instance.WasCollected)
        {
            return false;
        }
        foreach (Library.ReplacementSpawnerData spawnerReplacement in Library.spawnerReplacements)
        {
            try
            {
                if (Library.IsInZone(spawnerReplacement.zones) && Randoms.SHARED.GetProbability(1f / spawnerReplacement.chance))
                {
                    __result = spawnerReplacement.ident;
                    return false;
                }
            }
            catch
            {
            }
        }
        __result = id;
        return false;
    }
}
