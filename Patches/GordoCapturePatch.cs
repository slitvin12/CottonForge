using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.DataModel;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(SnareModel), "GetGordoIdForBait")]
public class GordoCapturePatch
{
    public static void Postfix(SnareModel __instance, ref IdentifiableType __result)
    {
        KeyValuePair<string, IdentifiableType> keyValuePair = Library.gordoBaitDict.FirstOrDefault((KeyValuePair<string, IdentifiableType> x) => x.Key == __instance.baitTypeId.name);
        if ((bool)keyValuePair.Value)
        {
            __result = keyValuePair.Value;
        }
    }
}
