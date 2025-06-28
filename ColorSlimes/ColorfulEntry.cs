global using CottonLibrary;
global using static CottonLibrary.Library;
global using static CottonLibrary.ExtentionLibrary;
using ColorSlimes;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Slime;
using MelonLoader;
using UnityEngine;
using Object = System.Object;
using SlimeDefinitions = Il2Cpp.SlimeDefinitions;

[assembly: MelonInfo(typeof(ColorfulEntry), "Color Slimes", "1.0.0", "PinkTarr")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]

namespace ColorSlimes;

public class ColorfulEntry : CottonMod
{
    public static Color32 red = new Color32(254, 59, 69, 255);
    public static Color32 green = new Color32(94, 244, 112, 255);
    public static Color32 blue = new Color32(60, 123, 247, 255);
    public static Color32 yellow = new Color32(255, 206, 92, 255);
    public static Color32 cyan = new Color32(104, 252, 255, 255);
    public static Color32 magenta = new Color32(244, 124, 240, 255);
    public static SlimeDefinition chromaticSlimeDefinition;
    public static IdentifiableType chromaticPlortIdent;
    public static GameObject chromaticPlortObject;
    public static GameObject chromaticSlimeObject;
    
    public static SlimeDefinition redSlimeDefinition;
    public static IdentifiableType redPlortIdent;
    public static GameObject redPlortObject;
    public static GameObject redSlimeObject;
    
    public static SlimeDefinition greenSlimeDefinition;
    public static IdentifiableType greenPlortIdent;
    public static GameObject greenPlortObject;
    public static GameObject greenSlimeObject;
    
    public static SlimeDefinition blueSlimeDefinition;
    public static IdentifiableType bluePlortIdent;
    public static GameObject bluePlortObject;
    public static GameObject blueSlimeObject;
    
    public static SlimeDefinition yellowSlimeDefinition;
    public static IdentifiableType yellowPlortIdent;
    public static GameObject yellowPlortObject;
    public static GameObject yellowSlimeObject;
    
    public static SlimeDefinition cyanSlimeDefinition;
    public static IdentifiableType cyanPlortIdent;
    public static GameObject cyanPlortObject;
    public static GameObject cyanSlimeObject;
    
    public static SlimeDefinition magentaSlimeDefinition;
    public static IdentifiableType magentaPlortIdent;
    public static GameObject magentaPlortObject;
    public static GameObject magentaSlimeObject;
    public override void OnEarlyInitializeMelon()
    {
        AddLanguages("localized");
    }

    public override void SaveDirectorLoaded()
    {
        // Base
        Red();
        Green();
        Blue();
        
        // Combo
        Yellow();
        Cyan();
        Magenta();
        
        // Dream
        Chromatic();
        
        // Grouped Setup
        InitializeSpawning();
        InitializeCombos();
    }

    public void InitializeSpawning()
    {
        // Red
        MakeSpawnableInZones(redSlimeDefinition, new DirectedActorSpawner.TimeWindow{TimeMode = DirectedActorSpawner.TimeMode.DAY}, SpawnLocations.RainbowFields, 0.00005f, SpawnerTypes.Slime);
        MakeSpawnableInZones(redSlimeDefinition, new DirectedActorSpawner.TimeWindow{TimeMode = DirectedActorSpawner.TimeMode.NIGHT}, SpawnLocations.RainbowFields, 0.00005f, SpawnerTypes.Slime);
        
        // Green
        MakeSpawnableInZones(greenSlimeDefinition, new DirectedActorSpawner.TimeWindow{TimeMode = DirectedActorSpawner.TimeMode.DAY}, SpawnLocations.EmberValley, 0.00005f, SpawnerTypes.Slime);
        MakeSpawnableInZones(greenSlimeDefinition, new DirectedActorSpawner.TimeWindow{TimeMode = DirectedActorSpawner.TimeMode.NIGHT}, SpawnLocations.EmberValley, 0.00005f, SpawnerTypes.Slime);
        
        // Blue
        MakeSpawnableInZones(blueSlimeDefinition, new DirectedActorSpawner.TimeWindow{TimeMode = DirectedActorSpawner.TimeMode.DAY}, SpawnLocations.StarlightStand, 0.00005f, SpawnerTypes.Slime);
        MakeSpawnableInZones(blueSlimeDefinition, new DirectedActorSpawner.TimeWindow{TimeMode = DirectedActorSpawner.TimeMode.NIGHT}, SpawnLocations.StarlightStand, 0.00005f, SpawnerTypes.Slime);
        
        // Chromatic
        MakeSpawnableInZones(
            chromaticSlimeDefinition,
            new DirectedActorSpawner.TimeWindow{TimeMode = DirectedActorSpawner.TimeMode.NIGHT},
            SpawnLocations.LabyrinthDreamland,
            0.00000005f,
            SpawnerTypes.Slime);
    }
    
    public void InitializeCombos()
    {
        // Yellow
        redSlimeDefinition.AddEatmapToSlime(CreateEatmap(SlimeEmotions.Emotion.HUNGER, 0, null, greenSlimeDefinition, yellowSlimeDefinition));
        greenSlimeDefinition.AddEatmapToSlime(CreateEatmap(SlimeEmotions.Emotion.HUNGER, 0, null, redSlimeDefinition, yellowSlimeDefinition));
        
        // Cyan
        blueSlimeDefinition.AddEatmapToSlime(CreateEatmap(SlimeEmotions.Emotion.HUNGER, 0, null, greenSlimeDefinition, cyanSlimeDefinition));
        greenSlimeDefinition.AddEatmapToSlime(CreateEatmap(SlimeEmotions.Emotion.HUNGER, 0, null, blueSlimeDefinition, cyanSlimeDefinition));
        
        // Magenta
        blueSlimeDefinition.AddEatmapToSlime(CreateEatmap(SlimeEmotions.Emotion.HUNGER, 0, null, redSlimeDefinition, magentaSlimeDefinition));
        redSlimeDefinition.AddEatmapToSlime(CreateEatmap(SlimeEmotions.Emotion.HUNGER, 0, null, blueSlimeDefinition, magentaSlimeDefinition));
    }
    
    public void Chromatic()
    {
        if (chromaticSlimeDefinition) return;
        
        chromaticPlortIdent = CreatePlortType("Chromatic", new Color32(60, 70, 235, 255), null, "IdentifiableType.ChromaticPlort", 21, 15);
        chromaticPlortObject = CreatePrefab("plortChromatic", GetPlort("TwinPlort").prefab);
        
        chromaticPlortIdent.localizedName = CreateLocalizedString("l.chromatic_plort");
        
        chromaticPlortIdent.SetObjectPrefab(chromaticPlortObject);
        chromaticPlortObject.SetObjectIdent(chromaticPlortIdent);
        
        SetPlortColor(red, green, blue, chromaticPlortObject);
        SetPlortTwinColor(cyan, magenta, yellow, chromaticPlortObject);
        
        chromaticPlortIdent.MakeVaccable();
        
        
        var pink = GetSlime("Pink");
        
        chromaticSlimeDefinition = CreateSlimeDef(
            "Chromatic",
            new Color32(80, 105, 255, 255), 
            null,
            UnityEngine.Object.Instantiate(pink.AppearancesDefault[0]),
            "ChromaticDefault",
            "SlimeDefinition.Chromatic"
        );

        chromaticSlimeDefinition.CanLargofy = true;
        
        chromaticSlimeDefinition.localizedName = CreateLocalizedString("l.chromatic_slime");
        
        chromaticSlimeObject = CreatePrefab("slimeChromatic", pink.prefab);
        
        chromaticSlimeDefinition.SetObjectPrefab(chromaticSlimeObject);
        chromaticSlimeObject.SetObjectIdent(chromaticSlimeDefinition);
        
        chromaticSlimeDefinition.SetSlimeColor(
            red,
            green,
            blue
        );
        chromaticSlimeDefinition.SetTwinColor(
            cyan,
            magenta,
            yellow
        );
        chromaticSlimeDefinition.EnableTwinEffect();
        
        chromaticSlimeDefinition.AppearancesDefault[0].SetPalette(chromaticSlimeDefinition.AppearancesDefault[0].Structures[0].DefaultMaterials[0], chromaticSlimeDefinition);

        chromaticSlimeDefinition.AppearancesDefault[0]._splatColor = new Color32(80, 105, 255, 255);
        chromaticSlimeDefinition.AppearancesDefault[0].SetSplatColor();
        
        IdentifiableTypeGroup[] chromaticFoods = new[]
        {
            nectar
        }!;

        chromaticSlimeDefinition.AddProduceIdent(chromaticPlortIdent);
        
        chromaticSlimeDefinition.Diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(chromaticFoods);
        chromaticSlimeDefinition.RefreshEatmap();
        
        chromaticSlimeDefinition.MakeVaccable();
    }
    
    public void Red()
    {
        if (redSlimeDefinition) return;
        
        redPlortIdent = CreatePlortType("Red", red, null, "IdentifiableType.RedPlort", 21, 15);
        redPlortObject = CreatePrefab("plortRed", GetPlort("PinkPlort").prefab);
        
        redPlortIdent.localizedName = CreateLocalizedString("l.red_plort");
        
        redPlortIdent.SetObjectPrefab(redPlortObject);
        redPlortObject.SetObjectIdent(redPlortIdent);
        
        SetPlortColor(red, red, red, redPlortObject);
        
        redPlortIdent.MakeVaccable();
        
        
        var pink = GetSlime("Pink");
        
        redSlimeDefinition = CreateSlimeDef(
            "Red",
            red, 
            null,
            UnityEngine.Object.Instantiate(pink.AppearancesDefault[0]),
            "RedDefault",
            "SlimeDefinition.Red"
        );

        redSlimeDefinition.CanLargofy = true;
        
        redSlimeDefinition.localizedName = CreateLocalizedString("l.red_slime");
        
        redSlimeObject = CreatePrefab("slimeRed", pink.prefab);
        
        redSlimeDefinition.SetObjectPrefab(redSlimeObject);
        redSlimeObject.SetObjectIdent(redSlimeDefinition);
        
        redSlimeDefinition.SetSlimeColor(
            red,
            red,
            red
        );
        redSlimeDefinition.AppearancesDefault[0].SetPalette(redSlimeDefinition.AppearancesDefault[0].Structures[0].DefaultMaterials[0], redSlimeDefinition);

        redSlimeDefinition.AppearancesDefault[0]._splatColor = red;
        redSlimeDefinition.AppearancesDefault[0].SetSplatColor();
        
        IdentifiableTypeGroup[] redFoods = new[]
        {
            veggies
        }!;

        redSlimeDefinition.AddProduceIdent(redPlortIdent);
        
        redSlimeDefinition.Diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(redFoods);
        redSlimeDefinition.RefreshEatmap();
        
        redSlimeDefinition.MakeVaccable();
    }

    public void Green()
    {
        if (greenSlimeDefinition) return;
        
        greenPlortIdent = CreatePlortType("Green", green, null, "IdentifiableType.GreenPlort", 21, 15);
        greenPlortObject = CreatePrefab("plortGreen", GetPlort("PinkPlort").prefab);
        
        greenPlortIdent.localizedName = CreateLocalizedString("l.green_plort");
        
        greenPlortIdent.SetObjectPrefab(greenPlortObject);
        greenPlortObject.SetObjectIdent(greenPlortIdent);
        
        SetPlortColor(green, green, green, greenPlortObject);
        
        greenPlortIdent.MakeVaccable();
        
        
        var pink = GetSlime("Pink");
        
        greenSlimeDefinition = CreateSlimeDef(
            "Green",
            green, 
            null,
            UnityEngine.Object.Instantiate(pink.AppearancesDefault[0]),
            "GreenDefault",
            "SlimeDefinition.Green"
        );

        greenSlimeDefinition.CanLargofy = true;
        
        greenSlimeDefinition.localizedName = CreateLocalizedString("l.green_slime");
        
        greenSlimeObject = CreatePrefab("slimeGreen", pink.prefab);
        
        greenSlimeDefinition.SetObjectPrefab(greenSlimeObject);
        greenSlimeObject.SetObjectIdent(greenSlimeDefinition);
        
        greenSlimeDefinition.SetSlimeColor(
            green,
            green,
            green
        );
        greenSlimeDefinition.AppearancesDefault[0].SetPalette(greenSlimeDefinition.AppearancesDefault[0].Structures[0].DefaultMaterials[0], greenSlimeDefinition);

        greenSlimeDefinition.AppearancesDefault[0]._splatColor = green;
        greenSlimeDefinition.AppearancesDefault[0].SetSplatColor();
        
        IdentifiableTypeGroup[] greenFoods = new[]
        {
            fruits
        }!;

        greenSlimeDefinition.AddProduceIdent(greenPlortIdent);
        
        greenSlimeDefinition.Diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(greenFoods);
        greenSlimeDefinition.RefreshEatmap();
        
        greenSlimeDefinition.MakeVaccable();
    }

    public void Blue()
    {
        if (blueSlimeDefinition) return;
        
        bluePlortIdent = CreatePlortType("Blue", blue, null, "IdentifiableType.BluePlort", 21, 15);
        bluePlortObject = CreatePrefab("plortBlue", GetPlort("PinkPlort").prefab);
        
        bluePlortIdent.localizedName = CreateLocalizedString("l.blue_plort");
        
        bluePlortIdent.SetObjectPrefab(bluePlortObject);
        bluePlortObject.SetObjectIdent(bluePlortIdent);
        
        SetPlortColor(blue, blue, blue, bluePlortObject);
        
        bluePlortIdent.MakeVaccable();
        
        
        var pink = GetSlime("Pink");
        
        blueSlimeDefinition = CreateSlimeDef(
            "Blue",
            blue, 
            null,
            UnityEngine.Object.Instantiate(pink.AppearancesDefault[0]),
            "BlueDefault",
            "SlimeDefinition.Blue"
        );

        blueSlimeDefinition.CanLargofy = true;

        
        blueSlimeDefinition.localizedName = CreateLocalizedString("l.blue_slime");
        
        blueSlimeObject = CreatePrefab("slimeBlue", pink.prefab);
        
        blueSlimeDefinition.SetObjectPrefab(blueSlimeObject);
        blueSlimeObject.SetObjectIdent(blueSlimeDefinition);
        
        blueSlimeDefinition.SetSlimeColor(
            blue,
            blue,
            blue
        );
        blueSlimeDefinition.AppearancesDefault[0].SetPalette(blueSlimeDefinition.AppearancesDefault[0].Structures[0].DefaultMaterials[0], blueSlimeDefinition);

        blueSlimeDefinition.AppearancesDefault[0]._splatColor = blue;
        blueSlimeDefinition.AppearancesDefault[0].SetSplatColor();
        
        IdentifiableTypeGroup[] blueFoods = new[]
        {
            meat
        }!;

        blueSlimeDefinition.AddProduceIdent(bluePlortIdent);
        
        blueSlimeDefinition.Diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(blueFoods);
        blueSlimeDefinition.RefreshEatmap();
        
        blueSlimeDefinition.MakeVaccable();
    }

    public void Yellow()
    {
        if (yellowSlimeDefinition) return;
        
        yellowPlortIdent = CreatePlortType("Yellow", yellow, null, "IdentifiableType.YellowPlort", 21, 15);
        yellowPlortObject = CreatePrefab("plortYellow", GetPlort("PinkPlort").prefab);
        
        yellowPlortIdent.localizedName = CreateLocalizedString("l.yellow_plort");
        
        yellowPlortIdent.SetObjectPrefab(yellowPlortObject);
        yellowPlortObject.SetObjectIdent(yellowPlortIdent);
        
        SetPlortColor(yellow, yellow, yellow, yellowPlortObject);
        
        yellowPlortIdent.MakeVaccable();
        
        
        var pink = GetSlime("Pink");
        
        yellowSlimeDefinition = CreateSlimeDef(
            "Yellow",
            yellow, 
            null,
            UnityEngine.Object.Instantiate(pink.AppearancesDefault[0]),
            "YellowDefault",
            "SlimeDefinition.Yellow"
        );

        
        yellowSlimeDefinition.localizedName = CreateLocalizedString("l.yellow_slime");
        
        yellowSlimeObject = CreatePrefab("slimeYellow", pink.prefab);
        
        yellowSlimeDefinition.SetObjectPrefab(yellowSlimeObject);
        yellowSlimeObject.SetObjectIdent(yellowSlimeDefinition);
        
        yellowSlimeDefinition.SetSlimeColor(
            yellow,
            yellow,
            yellow
        );
        yellowSlimeDefinition.AppearancesDefault[0].SetPalette(yellowSlimeDefinition.AppearancesDefault[0].Structures[0].DefaultMaterials[0], yellowSlimeDefinition);

        yellowSlimeDefinition.AppearancesDefault[0]._splatColor = yellow;
        yellowSlimeDefinition.AppearancesDefault[0].SetSplatColor();
        
        IdentifiableTypeGroup[] yellowFoods = new[]
        {
            veggies, fruits
        }!;

        yellowSlimeDefinition.AddProduceIdent(yellowPlortIdent);
        
        yellowSlimeDefinition.Diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(yellowFoods);
        yellowSlimeDefinition.RefreshEatmap();
        
        yellowSlimeDefinition.MakeVaccable();
    }

    public void Cyan()
    {
        if (cyanSlimeDefinition) return;
        
        cyanPlortIdent = CreatePlortType("Cyan", cyan, null, "IdentifiableType.CyanPlort", 21, 15);
        cyanPlortObject = CreatePrefab("plortCyan", GetPlort("PinkPlort").prefab);
        
        cyanPlortIdent.localizedName = CreateLocalizedString("l.cyan_plort");
        
        cyanPlortIdent.SetObjectPrefab(cyanPlortObject);
        cyanPlortObject.SetObjectIdent(cyanPlortIdent);
        
        SetPlortColor(cyan, cyan, cyan, cyanPlortObject);
        
        cyanPlortIdent.MakeVaccable();
        
        
        var pink = GetSlime("Pink");
        
        cyanSlimeDefinition = CreateSlimeDef(
            "Cyan",
            cyan, 
            null,
            UnityEngine.Object.Instantiate(pink.AppearancesDefault[0]),
            "CyanDefault",
            "SlimeDefinition.Cyan"
        );

        
        cyanSlimeDefinition.localizedName = CreateLocalizedString("l.cyan_slime");
        
        cyanSlimeObject = CreatePrefab("slimeCyan", pink.prefab);
        
        cyanSlimeDefinition.SetObjectPrefab(cyanSlimeObject);
        cyanSlimeObject.SetObjectIdent(cyanSlimeDefinition);
        
        cyanSlimeDefinition.SetSlimeColor(
            cyan,
            cyan,
            cyan
        );
        cyanSlimeDefinition.AppearancesDefault[0].SetPalette(cyanSlimeDefinition.AppearancesDefault[0].Structures[0].DefaultMaterials[0], cyanSlimeDefinition);

        cyanSlimeDefinition.AppearancesDefault[0]._splatColor = cyan;
        cyanSlimeDefinition.AppearancesDefault[0].SetSplatColor();
        
        IdentifiableTypeGroup[] cyanFoods = new[]
        {
            fruits, meat
        }!;

        cyanSlimeDefinition.AddProduceIdent(cyanPlortIdent);
        
        cyanSlimeDefinition.Diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(cyanFoods);
        cyanSlimeDefinition.RefreshEatmap();
        
        cyanSlimeDefinition.MakeVaccable();
    }

    public void Magenta()
    {
        if (magentaSlimeDefinition) return;
        
        magentaPlortIdent = CreatePlortType("Magenta", magenta, null, "IdentifiableType.MagentaPlort", 21, 15);
        magentaPlortObject = CreatePrefab("plortMagenta", GetPlort("PinkPlort").prefab);
        
        magentaPlortIdent.localizedName = CreateLocalizedString("l.magenta_plort");
        
        magentaPlortIdent.SetObjectPrefab(magentaPlortObject);
        magentaPlortObject.SetObjectIdent(magentaPlortIdent);
        
        SetPlortColor(magenta, magenta, magenta, magentaPlortObject);
        
        magentaPlortIdent.MakeVaccable();
        
        
        var pink = GetSlime("Pink");
        
        magentaSlimeDefinition = CreateSlimeDef(
            "Magenta",
            magenta, 
            null,
            UnityEngine.Object.Instantiate(pink.AppearancesDefault[0]),
            "MagentaDefault",
            "SlimeDefinition.Magenta"
        );

        
        magentaSlimeDefinition.localizedName = CreateLocalizedString("l.magenta_slime");
        
        magentaSlimeObject = CreatePrefab("slimeMagenta", pink.prefab);
        
        magentaSlimeDefinition.SetObjectPrefab(magentaSlimeObject);
        magentaSlimeObject.SetObjectIdent(magentaSlimeDefinition);
        
        magentaSlimeDefinition.SetSlimeColor(
            magenta,
            magenta,
            magenta
        );
        magentaSlimeDefinition.AppearancesDefault[0].SetPalette(magentaSlimeDefinition.AppearancesDefault[0].Structures[0].DefaultMaterials[0], magentaSlimeDefinition);

        magentaSlimeDefinition.AppearancesDefault[0]._splatColor = magenta;
        magentaSlimeDefinition.AppearancesDefault[0].SetSplatColor();
        
        IdentifiableTypeGroup[] magentaFoods = new[]
        {
            veggies, meat
        }!;

        magentaSlimeDefinition.AddProduceIdent(magentaPlortIdent);
        
        magentaSlimeDefinition.Diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(magentaFoods);
        magentaSlimeDefinition.RefreshEatmap();
        
        magentaSlimeDefinition.MakeVaccable();
    }
}