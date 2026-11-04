using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Damage;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.Persist;
using Il2CppMonomiPark.SlimeRancher.Script.Util;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppSystem.Collections.Generic;
using Il2CppSystem.Linq;
using MelonLoader;
using Microsoft.VisualBasic.FileIO;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace CottonLibrary;

public static partial class Library
{
    public class LargoOverrides
    {
        public string overrideTranslation = "{0} {1} Largo";

        public string overridePediaSuffix = "{0}_{1}_largo";
    }

    /// <summary>
    /// Automated largos from the <c>Library.CreateSlimeDef</c> function will use <c>KeepFirst</c> values for your slime.
    /// </summary>
    [Flags]
    public enum LargoSettings : ulong
    {
        KeepFirstBody = 1uL,
        KeepSecondBody = 2uL,
        KeepFirstFace = 4uL,
        KeepSecondFace = 8uL,
        KeepFirstColor = 0x10uL,
        KeepSecondColor = 0x20uL,
        KeepFirstTwinColor = 0x40uL,
        KeepSecondTwinColor = 0x80uL,
        MergeColors = 0x100uL,
        MergeTwinColors = 0x200uL,
        /// <summary>Fork: the smart largo engine picks the body (the Keep*Body flags only matter for the original Cotton / SmartLargos off).</summary>
        AutoBody = 0x400uL,
        /// <summary>Fork: the smart largo engine picks the face.</summary>
        AutoFace = 0x800uL,
        /// <summary>Fork: the smart largo engine picks the colour mode.</summary>
        AutoColors = 0x1000uL,
        /// <summary>Fork: AutoBody | AutoFace | AutoColors. Combine with the old flags for the original Cotton, e.g. KeepSecondBody | KeepSecondFace | MergeColors | Auto.</summary>
        Auto = AutoBody | AutoFace | AutoColors
    }

    public class GordoFaceData
    {
        public Material eyesBlink;

        public Material eyesNormal;

        public Material mouthHappy;

        public Material mouthChompOpen;

        public Material mouthEating;
    }

    internal static System.Collections.Generic.Dictionary<string, IdentifiableType> gordoBaitDict = new System.Collections.Generic.Dictionary<string, IdentifiableType>();

    public static System.Collections.Generic.Dictionary<SlimeDefinition, System.Collections.Generic.List<SlimeDiet.EatMapEntry>> customEatmaps = new System.Collections.Generic.Dictionary<SlimeDefinition, System.Collections.Generic.List<SlimeDiet.EatMapEntry>>();

    internal static IdentifiableTypeGroup INTERNAL_EnsureLargoGroup(SlimeDefinition slime)
    {
        if (slime == null || string.IsNullOrEmpty(slime.name))
        {
            return null;
        }
        string text = slime.name + "LargoGroup";
        IdentifiableTypeGroup identifiableTypeGroup = null;
        try
        {
            identifiableTypeGroup = Get<IdentifiableTypeGroup>(text);
            if (identifiableTypeGroup == null)
            {
                identifiableTypeGroup = ScriptableObject.CreateInstance<IdentifiableTypeGroup>();
                identifiableTypeGroup.name = text;
                identifiableTypeGroup.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(identifiableTypeGroup);
                identifiableTypeGroup._memberTypes = new Il2CppSystem.Collections.Generic.List<IdentifiableType>();
                identifiableTypeGroup._memberGroups = new Il2CppSystem.Collections.Generic.List<IdentifiableTypeGroup>();
                identifiableTypeGroup._isFood = false;
                identifiableTypeGroup._localizedName = slime.localizedName;
                identifiableTypeGroup.AllowedCategories = new Il2CppSystem.Collections.Generic.List<IdentifiableCategory>();
                identifiableTypeGroup._runtimeObject = new IdentifiableTypeGroupRuntimeObject(identifiableTypeGroup);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Cotton] Failed to create " + text + ": " + ex.Message);
            return identifiableTypeGroup;
        }
        try
        {
            IdentifiableTypeGroupList identifiableTypeGroupList = Get<IdentifiableTypeGroupList>("All Type Groups List");
            if (identifiableTypeGroupList != null && !identifiableTypeGroupList.items.Contains(identifiableTypeGroup))
            {
                identifiableTypeGroupList.items.Add(identifiableTypeGroup);
            }
        }
        catch
        {
        }
        try
        {
            LookupDirector lookupDirector = gameContext?.LookupDirector;
            if (lookupDirector != null)
            {
                if (lookupDirector._allIdentifiableTypeGroups != null && !lookupDirector._allIdentifiableTypeGroups.items.Contains(identifiableTypeGroup))
                {
                    lookupDirector._allIdentifiableTypeGroups.items.Add(identifiableTypeGroup);
                }
                lookupDirector.RegisterIdentifiableTypeGroup(identifiableTypeGroup);
            }
        }
        catch
        {
        }
        return identifiableTypeGroup;
    }

    internal static void INTERNAL_AddLargoToBaseGroups(SlimeDefinition largo, SlimeDefinition slimeOne, SlimeDefinition slimeTwo)
    {
        SlimeDefinition[] array = new SlimeDefinition[2] { slimeOne, slimeTwo };
        foreach (SlimeDefinition slimeDefinition in array)
        {
            try
            {
                IdentifiableTypeGroup identifiableTypeGroup = Get<IdentifiableTypeGroup>(slimeDefinition.name + "LargoGroup");
                if (identifiableTypeGroup != null && !identifiableTypeGroup._memberTypes.Contains(largo))
                {
                    identifiableTypeGroup._memberTypes.Add(largo);
                    if (identifiableTypeGroup.GetRuntimeObject() != null && !identifiableTypeGroup.GetRuntimeObject()._memberTypes.Contains(largo))
                    {
                        identifiableTypeGroup.GetRuntimeObject()._memberTypes.Add(largo);
                    }
                }
            }
            catch
            {
            }
        }
    }

    internal static bool DoesLargoComboExist(SlimeDefinition slime1, SlimeDefinition slime2)
    {
        if (slime1 == null || slime2 == null || slime1 == slime2 || slime1.name == slime2.name)
        {
            return true;
        }
        try
        {
            SlimeDefinitions slimeDefinitions = gameContext?.SlimeDefinitions;
            if (slimeDefinitions != null && (slimeDefinitions.GetLargoByBaseSlimes(slime1, slime2) != null || slimeDefinitions.GetLargoByBaseSlimes(slime2, slime1) != null))
            {
                return true;
            }
        }
        catch
        {
        }
        bool result = false;
        foreach (string largoCombo in largoCombos)
        {
            if (largoCombo.Contains(slime1.name) && largoCombo.Contains(slime2.name))
            {
                result = true;
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// Create a SlimeDefinition.
    /// </summary>
    /// <param name="name">The name of the SlimeDefinition object.</param>
    /// <param name="vacColor">The vac color for the slime.</param>
    /// <param name="icon">The icon for the slime.</param>
    /// <param name="baseAppearance">The appearance to copy.</param>
    /// <param name="appearanceName">The name of the new appearance.</param>
    /// <param name="refID">The reference ID, used for saving. Recommended to do <c>SlimeDefinition.{name</c>,</param>
    /// <returns>The new SlimeDefinition that was created.</returns>
    public static SlimeDefinition CreateSlimeDef(string name, Color32 vacColor, Sprite icon, SlimeAppearance baseAppearance, string appearanceName, string refID)
    {
        return CreateSlimeDef(name, vacColor, icon, baseAppearance, appearanceName, refID, largoable: false, (LargoSettings)0uL);
    }

    /// <summary>
    /// Create a SlimeDefinition.
    /// </summary>
    /// <param name="name">The name of the SlimeDefinition object.</param>
    /// <param name="vacColor">The vac color for the slime.</param>
    /// <param name="icon">The icon for the slime.</param>
    /// <param name="baseAppearance">The appearance to copy.</param>
    /// <param name="appearanceName">The name of the new appearance.</param>
    /// <param name="refID">The reference ID, used for saving. Recommended to do <c>SlimeDefinition.{name</c>,</param>
    /// <param name="largoable">If the slime can made into a largo.</param>
    /// <param name="largoSettings">The settings used for the largo's appearance.</param>
    /// <returns>The new SlimeDefinition that was created.</returns>
    public static SlimeDefinition CreateSlimeDef(string name, Color32 vacColor, Sprite icon, SlimeAppearance baseAppearance, string appearanceName, string refID, bool largoable, LargoSettings largoSettings)
    {
        SlimeDefinition slimeDef = UnityEngine.Object.Instantiate(GetSlime("Pink"));
        UnityEngine.Object.DontDestroyOnLoad(slimeDef);
        slimeDef.hideFlags = HideFlags.HideAndDontSave;
        slimeDef.name = name;
        slimeDef.Name = name;
        slimeDef.AppearancesDefault = new Il2CppReferenceArray<SlimeAppearance>(1L);
        SlimeAppearance slimeAppearance = UnityEngine.Object.Instantiate(baseAppearance);
        UnityEngine.Object.DontDestroyOnLoad(slimeAppearance);
        slimeAppearance.name = appearanceName;
        slimeAppearance._icon = icon;
        slimeDef.AppearancesDefault = slimeDef.AppearancesDefault.Add(slimeAppearance);
        if (slimeDef.AppearancesDefault[0] == null)
        {
            slimeDef.AppearancesDefault[0] = slimeAppearance;
        }
        for (int i = 0; i < slimeDef.AppearancesDefault[0].Structures.Count - 1; i++)
        {
            SlimeAppearanceStructure slimeAppearanceStructure = slimeDef.AppearancesDefault[0].Structures[i];
            SlimeAppearanceStructure slimeAppearanceStructure2 = new SlimeAppearanceStructure(slimeAppearanceStructure);
            slimeDef.AppearancesDefault[0].Structures[i] = slimeAppearanceStructure2;
            if (slimeAppearanceStructure.DefaultMaterials.Count != 0)
            {
                slimeAppearanceStructure2.DefaultMaterials[0] = UnityEngine.Object.Instantiate(slimeAppearanceStructure.DefaultMaterials[0]);
            }
        }
        SlimeDiet diet = INTERNAL_CreateNewDiet();
        slimeDef.Diet = diet;
        slimeDef.color = vacColor;
        slimeDef.icon = icon;
        INTERNAL_EnsureLargoGroup(slimeDef);
        if (!slimeDef.IsLargo)
        {
            gameContext.SlimeDefinitions.Slimes = gameContext.SlimeDefinitions.Slimes.AddItem(slimeDef).ToArray();
            gameContext.SlimeDefinitions._slimeDefinitionsByIdentifiable.TryAdd(slimeDef, slimeDef);
        }
        INTERNAL_SetupLoadForIdent(refID, slimeDef);
        if (largoable)
        {
            createLargoActions.Add(delegate
            {
                foreach (IdentifiableType item in baseSlimes.GetAllMembersArray())
                {
                    if (item.Cast<SlimeDefinition>().CanLargofy)
                    {
                        CreateCompleteLargo(slimeDef, item.Cast<SlimeDefinition>(), largoSettings);
                    }
                }
            });
        }
        baseSlimes._memberTypes.Add(slimeDef);
        baseSlimes.GetRuntimeObject()._memberTypes.Add(slimeDef);
        slimeDef.AppearancesDefault[0]._colorPalette = new SlimeAppearance.Palette
        {
            Ammo = vacColor,
            Bottom = vacColor,
            Middle = vacColor,
            Top = vacColor
        };
        slimeDef.CanLargofy = largoable;
        return slimeDef;
    }

    /// <summary>
    /// Sets the palette
    /// </summary>
    /// <param name="app">The slime appearance to set the palette for.</param>
    /// <param name="slimeMaterial">The material to get colors from.</param>
    /// <param name="definition">The slime definition for the ammo color.</param>
    public static void SetPalette(this SlimeAppearance app, Material slimeMaterial, SlimeDefinition definition)
    {
        app._colorPalette = new SlimeAppearance.Palette
        {
            Ammo = definition.color,
            Bottom = slimeMaterial.GetColor("_BottomColor"),
            Middle = slimeMaterial.GetColor("_MiddleColor"),
            Top = slimeMaterial.GetColor("_TopColor")
        };
    }

    internal static SlimeAppearance.Palette INTERNAL_GetTwinPalette(this SlimeAppearance app)
    {
        Material material = null;
        foreach (SlimeAppearanceStructure structure in app._structures)
        {
            if (structure.Element.Type == SlimeAppearanceElement.ElementType.BODY)
            {
                material = structure.DefaultMaterials[0];
                break;
            }
        }
        if (material == null)
        {
            return default(SlimeAppearance.Palette);
        }
        return new SlimeAppearance.Palette
        {
            Ammo = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue),
            Top = material.GetColor("_TwinTopColor"),
            Middle = material.GetColor("_TwinMiddleColor"),
            Bottom = material.GetColor("_TwinBottomColor")
        };
    }

    internal static SlimeAppearance.Palette INTERNAL_GetSloomberPalette(this SlimeAppearance app)
    {
        // fork: null-safe, and the SR2 body shader has no _SloomberMiddleColor (2 GetColor errors per largo before)
        Material material = null;
        if (app != null && app._structures != null)
        {
            foreach (SlimeAppearanceStructure structure in app._structures)
            {
                if (structure?.Element != null && structure.Element.Type == SlimeAppearanceElement.ElementType.BODY && structure.DefaultMaterials != null && structure.DefaultMaterials.Length > 0)
                {
                    material = structure.DefaultMaterials[0];
                    break;
                }
            }
        }
        Color GetOr(string prop) => material != null && material.HasProperty(prop) ? material.GetColor(prop) : default;
        return new SlimeAppearance.Palette
        {
            Ammo = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue),
            Top = GetOr("_SloomberTopColor"),
            Middle = GetOr("_SloomberMiddleColor"),
            Bottom = GetOr("_SloomberBottomColor")
        };
    }

    internal static bool INTERNAL_GetLargoHasTwinEffect(SlimeAppearance slime1, SlimeAppearance slime2)
    {
        bool flag = false;
        foreach (SlimeAppearanceStructure structure in slime1._structures)
        {
            if (structure.DefaultMaterials.Count != 0 && structure.DefaultMaterials[0].IsKeywordEnabled("_ENABLETWINEFFECT_ON"))
            {
                flag = true;
                break;
            }
        }
        foreach (SlimeAppearanceStructure structure2 in slime2._structures)
        {
            if (flag)
            {
                break;
            }
            if (structure2.DefaultMaterials.Count != 0 && structure2.DefaultMaterials[0].IsKeywordEnabled("_ENABLETWINEFFECT_ON"))
            {
                flag = true;
                break;
            }
        }
        return flag;
    }

    internal static bool INTERNAL_GetLargoHasSloomberEffect(SlimeAppearance slime1, SlimeAppearance slime2)
    {
        bool flag = false;
        foreach (SlimeAppearanceStructure structure in slime1._structures)
        {
            if (structure.DefaultMaterials.Count != 0 && structure.DefaultMaterials[0].IsKeywordEnabled("_BODYCOLORING_SLOOMBER"))
            {
                flag = true;
                break;
            }
        }
        foreach (SlimeAppearanceStructure structure2 in slime2._structures)
        {
            if (flag)
            {
                break;
            }
            if (structure2.DefaultMaterials.Count != 0 && structure2.DefaultMaterials[0].IsKeywordEnabled("_BODYCOLORING_SLOOMBER"))
            {
                flag = true;
                break;
            }
        }
        return flag;
    }

    /// <summary>
    /// Merges two appearance structures together for a largo.
    /// </summary>
    /// <param name="slime1">Base slime #1</param>
    /// <param name="slime2">Base slime #2</param>
    /// <param name="settings">Settings that determine what structures this function merges, and what colors.</param>
    /// <returns>A new array of structures.</returns>
    public static Il2CppReferenceArray<SlimeAppearanceStructure> MergeStructures(SlimeAppearance slime1, SlimeAppearance slime2, LargoSettings settings)
    {
        if (CottonLibrary.Forge.LargoBlend.Enabled)
        {
            try
            {
                return CottonLibrary.Forge.LargoBlend.MergeStructures(slime1, slime2, settings);
            }
            catch (Exception e)
            {
                CottonLibrary.Forge.ForgeLog.WarnOnce("mergestructures", "Smart MergeStructures failed (" + e.Message + "), using Cotton's merge");
            }
        }
        return MergeStructuresLegacy(slime1, slime2, CottonLibrary.Forge.LargoBlend.LegacySettings(settings));
    }

    /// <summary>
    /// Cotton's original structure merge (RGB colours, parts of both parents). Fork: no longer replaces materials of the parents.
    /// </summary>
    public static Il2CppReferenceArray<SlimeAppearanceStructure> MergeStructuresLegacy(SlimeAppearance slime1, SlimeAppearance slime2, LargoSettings settings)
    {
        System.Collections.Generic.List<SlimeAppearanceStructure> list = new System.Collections.Generic.List<SlimeAppearanceStructure>(0);
        SlimeAppearance.Palette colorPalette = slime1._colorPalette;
        SlimeAppearance.Palette palette = slime1.INTERNAL_GetTwinPalette();
        SlimeAppearance.Palette palette2 = slime1.INTERNAL_GetSloomberPalette();
        SlimeAppearance.Palette colorPalette2 = slime2._colorPalette;
        SlimeAppearance.Palette palette3 = slime2.INTERNAL_GetTwinPalette();
        SlimeAppearance.Palette palette4 = slime2.INTERNAL_GetSloomberPalette();
        bool flag = INTERNAL_GetLargoHasTwinEffect(slime1, slime2);
        bool flag2 = INTERNAL_GetLargoHasSloomberEffect(slime1, slime2);
        Material material = GetSlime("Sloomber").AppearancesDefault[0]._structures[0].DefaultMaterials[0];
        foreach (SlimeAppearanceStructure structure in slime1.Structures)
        {
            if (structure.Element.Type == SlimeAppearanceElement.ElementType.FACE || structure.Element.Type == SlimeAppearanceElement.ElementType.FACE_ATTACH)
            {
                if (!settings.HasFlag(LargoSettings.KeepFirstFace) || structure == null || list.Contains(structure) || structure.DefaultMaterials.Length == 0)
                {
                    continue;
                }
                SlimeAppearanceStructure slimeAppearanceStructure = new SlimeAppearanceStructure(structure);
                list.Add(slimeAppearanceStructure);
                Material material2 = UnityEngine.Object.Instantiate(structure.DefaultMaterials[0]);
                slimeAppearanceStructure.DefaultMaterials[0] = material2;
                try
                {
                    if (settings.HasFlag(LargoSettings.KeepFirstColor))
                    {
                        material2.SetColor("_TopColor", colorPalette.Top);
                        material2.SetColor("_MiddleColor", colorPalette.Middle);
                        material2.SetColor("_BottomColor", colorPalette.Bottom);
                        material2.SetColor("_SpecColor", colorPalette.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                    {
                        material2.SetColor("_TopColor", colorPalette2.Top);
                        material2.SetColor("_MiddleColor", colorPalette2.Middle);
                        material2.SetColor("_BottomColor", colorPalette2.Bottom);
                        material2.SetColor("_SpecColor", colorPalette2.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.MergeColors))
                    {
                        Color value = Color.Lerp(colorPalette.Top, colorPalette2.Top, 0.5f);
                        Color value2 = Color.Lerp(colorPalette.Middle, colorPalette2.Middle, 0.5f);
                        Color value3 = Color.Lerp(colorPalette.Bottom, colorPalette2.Bottom, 0.5f);
                        material2.SetColor("_TopColor", value);
                        material2.SetColor("_MiddleColor", value2);
                        material2.SetColor("_BottomColor", value3);
                        material2.SetColor("_SpecColor", value2);
                    }
                }
                catch
                {
                }
            }
            else if (structure.Element.Type == SlimeAppearanceElement.ElementType.BODY)
            {
                if (!settings.HasFlag(LargoSettings.KeepFirstBody) || structure == null || list.Contains(structure) || structure.DefaultMaterials.Length == 0)
                {
                    continue;
                }
                SlimeAppearanceStructure slimeAppearanceStructure2 = new SlimeAppearanceStructure(structure);
                list.Add(slimeAppearanceStructure2);
                Material material3 = UnityEngine.Object.Instantiate(structure.DefaultMaterials[0]);
                slimeAppearanceStructure2.DefaultMaterials[0] = material3;
                try
                {
                    if (settings.HasFlag(LargoSettings.KeepFirstColor))
                    {
                        material3.SetColor("_TopColor", colorPalette.Top);
                        material3.SetColor("_MiddleColor", colorPalette.Middle);
                        material3.SetColor("_BottomColor", colorPalette.Bottom);
                        material3.SetColor("_SpecColor", colorPalette.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                    {
                        material3.SetColor("_TopColor", colorPalette2.Top);
                        material3.SetColor("_MiddleColor", colorPalette2.Middle);
                        material3.SetColor("_BottomColor", colorPalette2.Bottom);
                        material3.SetColor("_SpecColor", colorPalette2.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.MergeColors))
                    {
                        Color value4 = Color.Lerp(colorPalette.Top, colorPalette2.Top, 0.5f);
                        Color value5 = Color.Lerp(colorPalette.Middle, colorPalette2.Middle, 0.5f);
                        Color value6 = Color.Lerp(colorPalette.Bottom, colorPalette2.Bottom, 0.5f);
                        material3.SetColor("_TopColor", value4);
                        material3.SetColor("_MiddleColor", value5);
                        material3.SetColor("_BottomColor", value6);
                        material3.SetColor("_SpecColor", value5);
                    }
                    if (!flag)
                    {
                        continue;
                    }
                    material3.EnableKeyword("_ENABLETWINEFFECT_ON");
                    if (settings.HasFlag(LargoSettings.KeepFirstTwinColor))
                    {
                        material3.SetColor("_TwinTopColor", palette.Top);
                        material3.SetColor("_TwinMiddleColor", palette.Middle);
                        material3.SetColor("_TwinBottomColor", colorPalette.Bottom);
                    }
                    else if (settings.HasFlag(LargoSettings.KeepSecondTwinColor))
                    {
                        material3.SetColor("_TwinTopColor", palette3.Top);
                        material3.SetColor("_TwinMiddleColor", palette3.Middle);
                        material3.SetColor("_TwinBottomColor", palette3.Bottom);
                    }
                    else if (settings.HasFlag(LargoSettings.MergeTwinColors))
                    {
                        Color value7 = Color.Lerp(palette.Top, palette3.Top, 0.5f);
                        Color value8 = Color.Lerp(palette.Middle, palette3.Middle, 0.5f);
                        Color value9 = Color.Lerp(palette.Bottom, palette3.Bottom, 0.5f);
                        material3.SetColor("_TwinTopColor", value7);
                        material3.SetColor("_TwinMiddleColor", value8);
                        material3.SetColor("_TwinBottomColor", value9);
                    }
                    if (flag2)
                    {
                        material3.SetTexture("_SloomberColorOverlay", material.GetTexture("_SloomberColorOverlay"));
                        material3.SetTexture("_SloomberStarMask", material.GetTexture("_SloomberStarMask"));
                        material3.EnableKeyword("_BODYCOLORING_SLOOMBER");
                        material3.DisableKeyword("_BODYCOLORING_DEFAULT");
                        if (settings.HasFlag(LargoSettings.KeepFirstColor))
                        {
                            material3.SetColor("_SloomberTopColor", palette2.Top);
                            material3.SetColor("_SloomberMiddleColor", palette2.Middle);
                            material3.SetColor("_SloomberBottomColor", palette2.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                        {
                            material3.SetColor("_SloomberTopColor", palette4.Top);
                            material3.SetColor("_SloomberMiddleColor", palette4.Middle);
                            material3.SetColor("_SloomberBottomColor", palette4.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeColors))
                        {
                            Color value10 = Color.Lerp(palette2.Top, palette4.Top, 0.5f);
                            Color value11 = Color.Lerp(palette2.Middle, palette4.Middle, 0.5f);
                            Color value12 = Color.Lerp(palette2.Bottom, palette4.Bottom, 0.5f);
                            material3.SetColor("_SloomberTopColor", value10);
                            material3.SetColor("_SloomberMiddleColor", value11);
                            material3.SetColor("_SloomberBottomColor", value12);
                        }
                    }
                }
                catch
                {
                }
            }
            else
            {
                if (structure == null || list.Contains(structure) || structure.DefaultMaterials.Length == 0)
                {
                    continue;
                }
                SlimeAppearanceStructure item = new SlimeAppearanceStructure(structure);
                list.Add(item);
                Material material4 = UnityEngine.Object.Instantiate(structure.DefaultMaterials[0]);
                System.Collections.Generic.List<Material> materials4 = new System.Collections.Generic.List<Material>(structure.DefaultMaterials);
                materials4[0] = material4;
                item.DefaultMaterials = CottonLibrary.Forge.Gc.Array(materials4); // fork: was structure.DefaultMaterials[0] = material4 (recoloured the parent)
                try
                {
                    if (settings.HasFlag(LargoSettings.KeepFirstColor))
                    {
                        material4.SetColor("_TopColor", colorPalette.Top);
                        material4.SetColor("_MiddleColor", colorPalette.Middle);
                        material4.SetColor("_BottomColor", colorPalette.Bottom);
                        material4.SetColor("_SpecColor", colorPalette.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                    {
                        material4.SetColor("_TopColor", colorPalette2.Top);
                        material4.SetColor("_MiddleColor", colorPalette2.Middle);
                        material4.SetColor("_BottomColor", colorPalette2.Bottom);
                        material4.SetColor("_SpecColor", colorPalette2.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.MergeColors))
                    {
                        Color value13 = Color.Lerp(colorPalette.Top, colorPalette2.Top, 0.5f);
                        Color value14 = Color.Lerp(colorPalette.Middle, colorPalette2.Middle, 0.5f);
                        Color value15 = Color.Lerp(colorPalette.Bottom, colorPalette2.Bottom, 0.5f);
                        material4.SetColor("_TopColor", value13);
                        material4.SetColor("_MiddleColor", value14);
                        material4.SetColor("_BottomColor", value15);
                        material4.SetColor("_SpecColor", value14);
                    }
                    if (flag2)
                    {
                        material4.SetTexture("_SloomberColorOverlay", material.GetTexture("_SloomberColorOverlay"));
                        material4.SetTexture("_SloomberStarMask", material.GetTexture("_SloomberStarMask"));
                        material4.EnableKeyword("_BODYCOLORING_SLOOMBER");
                        material4.DisableKeyword("_BODYCOLORING_DEFAULT");
                        if (settings.HasFlag(LargoSettings.KeepFirstColor))
                        {
                            material4.SetColor("_SloomberTopColor", palette2.Top);
                            material4.SetColor("_SloomberMiddleColor", palette2.Middle);
                            material4.SetColor("_SloomberBottomColor", palette2.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                        {
                            material4.SetColor("_SloomberTopColor", palette4.Top);
                            material4.SetColor("_SloomberMiddleColor", palette4.Middle);
                            material4.SetColor("_SloomberBottomColor", palette4.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeColors))
                        {
                            Color value16 = Color.Lerp(palette2.Top, palette4.Top, 0.5f);
                            Color value17 = Color.Lerp(palette2.Middle, palette4.Middle, 0.5f);
                            Color value18 = Color.Lerp(palette2.Bottom, palette4.Bottom, 0.5f);
                            material4.SetColor("_SloomberTopColor", value16);
                            material4.SetColor("_SloomberMiddleColor", value17);
                            material4.SetColor("_SloomberBottomColor", value18);
                        }
                    }
                    if (flag)
                    {
                        material4.EnableKeyword("_ENABLETWINEFFECT_ON");
                        if (settings.HasFlag(LargoSettings.KeepFirstTwinColor))
                        {
                            material4.SetColor("_TwinTopColor", palette.Top);
                            material4.SetColor("_TwinMiddleColor", palette.Middle);
                            material4.SetColor("_TwinBottomColor", palette.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondTwinColor))
                        {
                            material4.SetColor("_TwinTopColor", palette3.Top);
                            material4.SetColor("_TwinMiddleColor", palette3.Middle);
                            material4.SetColor("_TwinBottomColor", palette3.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeTwinColors))
                        {
                            Color value19 = Color.Lerp(palette.Top, palette3.Top, 0.5f);
                            Color value20 = Color.Lerp(palette.Middle, palette3.Middle, 0.5f);
                            Color value21 = Color.Lerp(palette.Bottom, palette3.Bottom, 0.5f);
                            material4.SetColor("_TwinTopColor", value19);
                            material4.SetColor("_TwinMiddleColor", value20);
                            material4.SetColor("_TwinBottomColor", value21);
                        }
                    }
                }
                catch
                {
                }
            }
        }
        foreach (SlimeAppearanceStructure structure2 in slime2.Structures)
        {
            if (structure2.Element.Type == SlimeAppearanceElement.ElementType.FACE || structure2.Element.Type == SlimeAppearanceElement.ElementType.FACE_ATTACH)
            {
                if (!settings.HasFlag(LargoSettings.KeepSecondFace) || structure2 == null || list.Contains(structure2) || structure2.DefaultMaterials.Length == 0)
                {
                    continue;
                }
                SlimeAppearanceStructure slimeAppearanceStructure3 = new SlimeAppearanceStructure(structure2);
                list.Add(slimeAppearanceStructure3);
                Material material5 = UnityEngine.Object.Instantiate(structure2.DefaultMaterials[0]);
                slimeAppearanceStructure3.DefaultMaterials[0] = material5;
                try
                {
                    if (settings.HasFlag(LargoSettings.KeepFirstColor))
                    {
                        material5.SetColor("_TopColor", colorPalette.Top);
                        material5.SetColor("_MiddleColor", colorPalette.Middle);
                        material5.SetColor("_BottomColor", colorPalette.Bottom);
                        material5.SetColor("_SpecColor", colorPalette.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                    {
                        material5.SetColor("_TopColor", colorPalette2.Top);
                        material5.SetColor("_MiddleColor", colorPalette2.Middle);
                        material5.SetColor("_BottomColor", colorPalette2.Bottom);
                        material5.SetColor("_SpecColor", colorPalette2.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.MergeColors))
                    {
                        Color value22 = Color.Lerp(colorPalette.Top, colorPalette2.Top, 0.5f);
                        Color value23 = Color.Lerp(colorPalette.Middle, colorPalette2.Middle, 0.5f);
                        Color value24 = Color.Lerp(colorPalette.Bottom, colorPalette2.Bottom, 0.5f);
                        material5.SetColor("_TopColor", value22);
                        material5.SetColor("_MiddleColor", value23);
                        material5.SetColor("_BottomColor", value24);
                        material5.SetColor("_SpecColor", value23);
                    }
                    if (flag2)
                    {
                        material5.SetTexture("_SloomberColorOverlay", material.GetTexture("_SloomberColorOverlay"));
                        material5.SetTexture("_SloomberStarMask", material.GetTexture("_SloomberStarMask"));
                        material5.EnableKeyword("_BODYCOLORING_SLOOMBER");
                        material5.DisableKeyword("_BODYCOLORING_DEFAULT");
                        if (settings.HasFlag(LargoSettings.KeepFirstColor))
                        {
                            material5.SetColor("_SloomberTopColor", palette2.Top);
                            material5.SetColor("_SloomberMiddleColor", palette2.Middle);
                            material5.SetColor("_SloomberBottomColor", palette2.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                        {
                            material5.SetColor("_SloomberTopColor", palette4.Top);
                            material5.SetColor("_SloomberMiddleColor", palette4.Middle);
                            material5.SetColor("_SloomberBottomColor", palette4.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeColors))
                        {
                            Color value25 = Color.Lerp(palette2.Top, palette4.Top, 0.5f);
                            Color value26 = Color.Lerp(palette2.Middle, palette4.Middle, 0.5f);
                            Color value27 = Color.Lerp(palette2.Bottom, palette4.Bottom, 0.5f);
                            material5.SetColor("_SloomberTopColor", value25);
                            material5.SetColor("_SloomberMiddleColor", value26);
                            material5.SetColor("_SloomberBottomColor", value27);
                        }
                    }
                    if (flag)
                    {
                        material5.EnableKeyword("_ENABLETWINEFFECT_ON");
                        if (settings.HasFlag(LargoSettings.KeepFirstTwinColor))
                        {
                            material5.SetColor("_TwinTopColor", palette.Top);
                            material5.SetColor("_TwinMiddleColor", palette.Middle);
                            material5.SetColor("_TwinBottomColor", palette.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondTwinColor))
                        {
                            material5.SetColor("_TwinTopColor", palette3.Top);
                            material5.SetColor("_TwinMiddleColor", palette3.Middle);
                            material5.SetColor("_TwinBottomColor", palette3.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeTwinColors))
                        {
                            Color value28 = Color.Lerp(palette.Top, palette3.Top, 0.5f);
                            Color value29 = Color.Lerp(palette.Middle, palette3.Middle, 0.5f);
                            Color value30 = Color.Lerp(palette.Bottom, palette3.Bottom, 0.5f);
                            material5.SetColor("_TwinTopColor", value28);
                            material5.SetColor("_TwinMiddleColor", value29);
                            material5.SetColor("_TwinBottomColor", value30);
                        }
                    }
                }
                catch
                {
                }
            }
            else if (structure2.Element.Type == SlimeAppearanceElement.ElementType.BODY)
            {
                if (!settings.HasFlag(LargoSettings.KeepSecondBody) || list.Contains(structure2))
                {
                    continue;
                }
                SlimeAppearanceStructure slimeAppearanceStructure4 = new SlimeAppearanceStructure(structure2);
                list.Add(slimeAppearanceStructure4);
                Material material6 = UnityEngine.Object.Instantiate(structure2.DefaultMaterials[0]);
                slimeAppearanceStructure4.DefaultMaterials[0] = material6;
                try
                {
                    if (settings.HasFlag(LargoSettings.KeepFirstColor))
                    {
                        material6.SetColor("_TopColor", colorPalette.Top);
                        material6.SetColor("_MiddleColor", colorPalette.Middle);
                        material6.SetColor("_BottomColor", colorPalette.Bottom);
                        material6.SetColor("_SpecColor", colorPalette.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                    {
                        material6.SetColor("_TopColor", colorPalette2.Top);
                        material6.SetColor("_MiddleColor", colorPalette2.Middle);
                        material6.SetColor("_BottomColor", colorPalette2.Bottom);
                        material6.SetColor("_SpecColor", colorPalette2.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.MergeColors))
                    {
                        Color value31 = Color.Lerp(colorPalette.Top, colorPalette2.Top, 0.5f);
                        Color value32 = Color.Lerp(colorPalette.Middle, colorPalette2.Middle, 0.5f);
                        Color value33 = Color.Lerp(colorPalette.Bottom, colorPalette2.Bottom, 0.5f);
                        material6.SetColor("_TopColor", value31);
                        material6.SetColor("_MiddleColor", value32);
                        material6.SetColor("_BottomColor", value33);
                        material6.SetColor("_SpecColor", value32);
                    }
                    if (flag2)
                    {
                        material6.SetTexture("_SloomberColorOverlay", material.GetTexture("_SloomberColorOverlay"));
                        material6.SetTexture("_SloomberStarMask", material.GetTexture("_SloomberStarMask"));
                        material6.EnableKeyword("_BODYCOLORING_SLOOMBER");
                        material6.DisableKeyword("_BODYCOLORING_DEFAULT");
                        if (settings.HasFlag(LargoSettings.KeepFirstColor))
                        {
                            material6.SetColor("_SloomberTopColor", palette2.Top);
                            material6.SetColor("_SloomberMiddleColor", palette2.Middle);
                            material6.SetColor("_SloomberBottomColor", palette2.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                        {
                            material6.SetColor("_SloomberTopColor", palette4.Top);
                            material6.SetColor("_SloomberMiddleColor", palette4.Middle);
                            material6.SetColor("_SloomberBottomColor", palette4.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeColors))
                        {
                            Color value34 = Color.Lerp(palette2.Top, palette4.Top, 0.5f);
                            Color value35 = Color.Lerp(palette2.Middle, palette4.Middle, 0.5f);
                            Color value36 = Color.Lerp(palette2.Bottom, palette4.Bottom, 0.5f);
                            material6.SetColor("_SloomberTopColor", value34);
                            material6.SetColor("_SloomberMiddleColor", value35);
                            material6.SetColor("_SloomberBottomColor", value36);
                        }
                    }
                    if (flag)
                    {
                        material6.EnableKeyword("_ENABLETWINEFFECT_ON");
                        if (settings.HasFlag(LargoSettings.KeepFirstTwinColor))
                        {
                            material6.SetColor("_TwinTopColor", palette.Top);
                            material6.SetColor("_TwinMiddleColor", palette.Middle);
                            material6.SetColor("_TwinBottomColor", palette.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondTwinColor))
                        {
                            material6.SetColor("_TwinTopColor", palette3.Top);
                            material6.SetColor("_TwinMiddleColor", palette3.Middle);
                            material6.SetColor("_TwinBottomColor", palette3.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeTwinColors))
                        {
                            Color value37 = Color.Lerp(palette.Top, palette3.Top, 0.5f);
                            Color value38 = Color.Lerp(palette.Middle, palette3.Middle, 0.5f);
                            Color value39 = Color.Lerp(palette.Bottom, palette3.Bottom, 0.5f);
                            material6.SetColor("_TwinTopColor", value37);
                            material6.SetColor("_TwinMiddleColor", value38);
                            material6.SetColor("_TwinBottomColor", value39);
                        }
                    }
                }
                catch
                {
                }
            }
            else
            {
                if (structure2 == null || list.Contains(structure2) || structure2.DefaultMaterials.Length == 0)
                {
                    continue;
                }
                SlimeAppearanceStructure slimeAppearanceStructure5 = new SlimeAppearanceStructure(structure2);
                list.Add(slimeAppearanceStructure5);
                Material material7 = UnityEngine.Object.Instantiate(structure2.DefaultMaterials[0]);
                slimeAppearanceStructure5.DefaultMaterials[0] = material7;
                try
                {
                    if (settings.HasFlag(LargoSettings.KeepFirstColor))
                    {
                        material7.SetColor("_TopColor", colorPalette.Top);
                        material7.SetColor("_MiddleColor", colorPalette.Middle);
                        material7.SetColor("_BottomColor", colorPalette.Bottom);
                        material7.SetColor("_SpecColor", colorPalette.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                    {
                        material7.SetColor("_TopColor", colorPalette2.Top);
                        material7.SetColor("_MiddleColor", colorPalette2.Middle);
                        material7.SetColor("_BottomColor", colorPalette2.Bottom);
                        material7.SetColor("_SpecColor", colorPalette2.Middle);
                    }
                    else if (settings.HasFlag(LargoSettings.MergeColors))
                    {
                        Color value40 = Color.Lerp(colorPalette.Top, colorPalette2.Top, 0.5f);
                        Color value41 = Color.Lerp(colorPalette.Middle, colorPalette2.Middle, 0.5f);
                        Color value42 = Color.Lerp(colorPalette.Bottom, colorPalette2.Bottom, 0.5f);
                        material7.SetColor("_TopColor", value40);
                        material7.SetColor("_MiddleColor", value41);
                        material7.SetColor("_BottomColor", value42);
                        material7.SetColor("_SpecColor", value41);
                    }
                    if (flag2)
                    {
                        material7.SetTexture("_SloomberColorOverlay", material.GetTexture("_SloomberColorOverlay"));
                        material7.SetTexture("_SloomberStarMask", material.GetTexture("_SloomberStarMask"));
                        material7.EnableKeyword("_BODYCOLORING_SLOOMBER");
                        material7.DisableKeyword("_BODYCOLORING_DEFAULT");
                        if (settings.HasFlag(LargoSettings.KeepFirstColor))
                        {
                            material7.SetColor("_SloomberTopColor", palette2.Top);
                            material7.SetColor("_SloomberMiddleColor", palette2.Middle);
                            material7.SetColor("_SloomberBottomColor", palette2.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondColor))
                        {
                            material7.SetColor("_SloomberTopColor", palette4.Top);
                            material7.SetColor("_SloomberMiddleColor", palette4.Middle);
                            material7.SetColor("_SloomberBottomColor", palette4.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeColors))
                        {
                            Color value43 = Color.Lerp(palette2.Top, palette4.Top, 0.5f);
                            Color value44 = Color.Lerp(palette2.Middle, palette4.Middle, 0.5f);
                            Color value45 = Color.Lerp(palette2.Bottom, palette4.Bottom, 0.5f);
                            material7.SetColor("_SloomberTopColor", value43);
                            material7.SetColor("_SloomberMiddleColor", value44);
                            material7.SetColor("_SloomberBottomColor", value45);
                        }
                    }
                    if (flag)
                    {
                        material7.EnableKeyword("_ENABLETWINEFFECT_ON");
                        if (settings.HasFlag(LargoSettings.KeepFirstTwinColor))
                        {
                            material7.SetColor("_TwinTopColor", palette.Top);
                            material7.SetColor("_TwinMiddleColor", palette.Middle);
                            material7.SetColor("_TwinBottomColor", palette.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.KeepSecondTwinColor))
                        {
                            material7.SetColor("_TwinTopColor", palette3.Top);
                            material7.SetColor("_TwinMiddleColor", palette3.Middle);
                            material7.SetColor("_TwinBottomColor", palette3.Bottom);
                        }
                        else if (settings.HasFlag(LargoSettings.MergeTwinColors))
                        {
                            Color value46 = Color.Lerp(palette.Top, palette3.Top, 0.5f);
                            Color value47 = Color.Lerp(palette.Middle, palette3.Middle, 0.5f);
                            Color value48 = Color.Lerp(palette.Bottom, palette3.Bottom, 0.5f);
                            material7.SetColor("_TwinTopColor", value46);
                            material7.SetColor("_TwinMiddleColor", value47);
                            material7.SetColor("_TwinBottomColor", value48);
                        }
                    }
                }
                catch
                {
                }
            }
        }
        return new Il2CppReferenceArray<SlimeAppearanceStructure>(list.ToArray());
    }

    /// <summary>
    /// Merges two slime's favorite toys list. Use for largos
    /// </summary>
    /// <param name="slimeOne">The first slime</param>
    /// <param name="slimeTwo">The other slime</param>
    /// <returns>An IL2Cpp array of ToyDefinition</returns>
    public static Il2CppReferenceArray<ToyDefinition> MergeFavoriteToys(SlimeDefinition slimeOne, SlimeDefinition slimeTwo)
    {
        System.Collections.Generic.List<ToyDefinition> list = new System.Collections.Generic.List<ToyDefinition>();
        foreach (ToyDefinition favoriteToyIdent in slimeOne.FavoriteToyIdents)
        {
            list.Add(favoriteToyIdent);
        }
        foreach (ToyDefinition favoriteToyIdent2 in slimeTwo.FavoriteToyIdents)
        {
            list.Add(favoriteToyIdent2);
        }
        return list.ToArray();
    }

    /// <summary>
    /// Merges the components on two GameObjects.
    /// </summary>
    /// <param name="obj">The target GameObject.</param>
    /// <param name="oldOne">The first GameObject to get components from.</param>
    /// <param name="oldTwo">The other GameObject to get components from, optional.</param>
    public static void MergeComponents(GameObject obj, GameObject oldOne, GameObject oldTwo)
    {
        System.Collections.Generic.Dictionary<string, (MonoBehaviour, bool)> dictionary = new System.Collections.Generic.Dictionary<string, (MonoBehaviour, bool)>();
        foreach (MonoBehaviour component in obj.GetComponents<MonoBehaviour>())
        {
            dictionary.TryAdd(component.GetIl2CppType().Name, (component, false));
        }
        foreach (MonoBehaviour component2 in oldOne.GetComponents<MonoBehaviour>())
        {
            dictionary.TryAdd(component2.GetIl2CppType().Name, (component2, true));
        }
        if ((bool)oldTwo)
        {
            foreach (MonoBehaviour component3 in oldTwo.GetComponents<MonoBehaviour>())
            {
                dictionary.TryAdd(component3.GetIl2CppType().Name, (component3, true));
            }
        }
        foreach (System.Collections.Generic.KeyValuePair<string, (MonoBehaviour, bool)> item in dictionary)
        {
            if (item.Value.Item2 && !item.Key.ContainsAny("AweTowardsLargos", "SlimeEyeComponents", "SlimeMouthComponents", "ColliderTotemLinkerHelper") && obj.TryAddComponentTypeIfNull(item.Value.Item1.GetIl2CppType(), out var comp))
            {
                comp.CopyFields(item.Value.Item1);
            }
        }
    }

    /// <summary>
    /// Creates a fully completed largo.
    /// </summary>
    /// <param name="slimeOne">Base slime #1</param>
    /// <param name="slimeTwo">Base slime #2</param>
    /// <param name="settings">The appearance settings.</param>
    /// <param name="overrides">The overrides for the translations.</param>
    /// <returns>The SlimeDefinition created for the largo.</returns>
    public static SlimeDefinition CreateCompleteLargo(SlimeDefinition slimeOne, SlimeDefinition slimeTwo, LargoSettings settings, LargoOverrides overrides = null)
    {
        if (DoesLargoComboExist(slimeOne, slimeTwo))
        {
            return null;
        }
        bool smart = CottonLibrary.Forge.LargoBlend.Enabled;
        SlimeDefinition largo = smart ? CottonLibrary.Forge.LargoBlend.PickTemplate(slimeOne, slimeTwo) : GetLargo("PinkRock");
        if (slimeOne.IsLargo || slimeTwo.IsLargo)
        {
            return null;
        }
        SlimeDefinition slimeDefinition = UnityEngine.Object.Instantiate(largo);
        slimeDefinition.BaseSlimes = new SlimeDefinition[2] { slimeOne, slimeTwo };
        slimeDefinition.SlimeModules = new GameObject[2]
        {
            Get<GameObject>("moduleSlime" + slimeOne.name),
            Get<GameObject>("moduleSlime" + slimeTwo.name)
        };
        if (overrides != null)
        {
            slimeDefinition._pediaPersistenceSuffix = string.Format(overrides.overridePediaSuffix, slimeOne.name.ToLower(), slimeTwo.name.ToLower());
        }
        else
        {
            slimeDefinition._pediaPersistenceSuffix = slimeOne.name.ToLower() + "_" + slimeTwo.name.ToLower() + "_largo";
        }
        slimeDefinition.referenceId = "SlimeDefinition." + slimeOne.name + slimeTwo.name;
        if (overrides != null)
        {
            slimeDefinition.localizedName = CreateStaticString(string.Format(overrides.overrideTranslation, slimeOne.name, slimeTwo.name), "l." + slimeDefinition._pediaPersistenceSuffix);
        }
        else
        {
            slimeDefinition.localizedName = CreateStaticString(slimeOne.name + " " + slimeTwo.name + " Largo", "l." + slimeDefinition._pediaPersistenceSuffix);
        }
        slimeDefinition.FavoriteToyIdents = new Il2CppReferenceArray<ToyDefinition>(MergeFavoriteToys(slimeOne, slimeTwo));
        UnityEngine.Object.DontDestroyOnLoad(slimeDefinition);
        slimeDefinition.hideFlags = HideFlags.HideAndDontSave;
        slimeDefinition.name = slimeOne.name + slimeTwo.name;
        slimeDefinition.Name = slimeDefinition.name;
        slimeDefinition.prefab = UnityEngine.Object.Instantiate(largo.prefab, rootOBJ.transform);
        slimeDefinition.prefab.name = "slime" + slimeOne.name + slimeTwo.name;
        slimeDefinition.prefab.GetComponent<Identifiable>().identType = slimeDefinition;
        slimeDefinition.prefab.GetComponent<SlimeEat>().SlimeDefinition = slimeDefinition;
        slimeDefinition.prefab.GetComponent<SlimeAppearanceApplicator>().SlimeDefinition = slimeDefinition;
        slimeDefinition.prefab.GetComponent<PlayWithToys>().SlimeDefinition = slimeDefinition;
        slimeDefinition.prefab.GetComponent<ReactToToyNearby>().SlimeDefinition = slimeDefinition;
        // fork (smart largos): Rock largos keep rolling / hurting, the template (PinkRock or Pink+X) only loses them when no parent has them
        if (!smart || (slimeOne.prefab.GetComponent<RockSlimeRoll>() == null && slimeTwo.prefab.GetComponent<RockSlimeRoll>() == null))
        {
            slimeDefinition.prefab.RemoveComponent<RockSlimeRoll>();
        }
        if (!smart || (slimeOne.prefab.GetComponent<DamagePlayerOnTouch>() == null && slimeTwo.prefab.GetComponent<DamagePlayerOnTouch>() == null))
        {
            slimeDefinition.prefab.RemoveComponent<DamagePlayerOnTouch>();
        }
        SlimeAppearance slimeAppearance = UnityEngine.Object.Instantiate(largo.AppearancesDefault[0]);
        slimeDefinition.AppearancesDefault[0] = slimeAppearance;
        UnityEngine.Object.DontDestroyOnLoad(slimeAppearance);
        slimeAppearance.name = slimeOne.AppearancesDefault[0].name + slimeTwo.AppearancesDefault[0].name;
        slimeAppearance._dependentAppearances = new SlimeAppearance[2]
        {
            slimeOne.AppearancesDefault[0],
            slimeTwo.AppearancesDefault[0]
        };
        bool blended = smart && CottonLibrary.Forge.LargoBlend.Apply(slimeDefinition, slimeOne, slimeTwo, largo, settings);
        if (!blended)
        {
            slimeAppearance._structures = MergeStructuresLegacy(slimeAppearance._dependentAppearances[0], slimeAppearance._dependentAppearances[1], CottonLibrary.Forge.LargoBlend.LegacySettings(settings));
        }
        try
        {
            slimeDefinition.Diet = slimeOne.Diet.MergeDiet(slimeTwo.Diet);
        }
        catch
        {
            if (settings.HasFlag(LargoSettings.KeepFirstBody))
            {
                slimeDefinition.Diet = slimeOne.Diet;
            }
            else if (settings.HasFlag(LargoSettings.KeepSecondBody))
            {
                slimeDefinition.Diet = slimeTwo.Diet;
            }
            else
            {
                slimeDefinition.Diet = slimeOne.Diet;
                MelonLogger.BigError("Largo Error", "Failed to merge diet, and largo settings are incorrectly set! Defaulting to slime 1's diet.");
                MelonLogger.BigError("Largo Error", "Failed to merge diet, and largo settings are incorrectly set! Defaulting to slime 1's diet.");
                MelonLogger.BigError("Largo Error", "Failed to merge diet, and largo settings are incorrectly set! Defaulting to slime 1's diet.");
            }
        }
        slimeDefinition.RefreshEatmap();
        slimeDefinitions.Slimes = slimeDefinitions.Slimes.Add(slimeDefinition);
        slimeDefinitions._slimeDefinitionsByIdentifiable.TryAdd(slimeDefinition, slimeDefinition);
        if (blended)
        {
            CottonLibrary.Forge.LargoBlend.Register(slimeDefinition);
        }
        else
        {
            mainAppearanceDirector.RegisterDependentAppearances(slimeDefinition, slimeDefinition.AppearancesDefault[0]);
            mainAppearanceDirector.UpdateChosenSlimeAppearance(slimeDefinition, slimeDefinition.AppearancesDefault[0]);
        }
        slimeDefinition.AddToGroup("LargoGroup");
        slimeDefinition.AddToGroup("SlimesGroup");
        INTERNAL_AddLargoToBaseGroups(slimeDefinition, slimeOne, slimeTwo);
        INTERNAL_SetupLoadForIdent(slimeDefinition.referenceId, slimeDefinition);
        largoCombos.Add(slimeOne.name + " " + slimeTwo.name);
        slimeDefinitions.RefreshDefinitions();
        slimeDefinitions.RefreshIndexes();
        // fork: the parents' eat maps after RefreshIndexes (before it, a parent never became its newest largo)
        slimeOne.RefreshEatmap();
        slimeTwo.RefreshEatmap();
        // fork (smart largos): parent components with their references remapped onto the largo prefab, then parity
        // (template references, groups, full art, zones, diet, icon); OnCreated hooks run after it
        if (smart)
        {
            CottonLibrary.Forge.LargoParity.MergeComponents(slimeDefinition, slimeOne, slimeTwo);
            try
            {
                CottonLibrary.Forge.LargoParity.Fix(slimeDefinition, slimeOne, slimeTwo, largo);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Cotton] Largo parity of " + slimeDefinition.name + " failed: " + ex.Message);
            }
        }
        else
        {
            MergeComponents(slimeDefinition.prefab, slimeOne.prefab, slimeTwo.prefab);
        }
        CottonLibrary.Forge.LargoBlend.Finish(slimeDefinition, slimeOne, slimeTwo);
        return slimeDefinition;
    }

    public static void AddStructure(this SlimeAppearance appearance, SlimeAppearanceStructure structure)
    {
        appearance.Structures.Add(structure);
    }

    public static SlimeDiet MergeDiet(this SlimeDiet firstDiet, SlimeDiet secondDiet)
    {
        SlimeDiet slimeDiet = INTERNAL_CreateNewDiet();
        slimeDiet.EatMap.AddListRangeNoMultiple(firstDiet.EatMap);
        slimeDiet.EatMap.AddListRangeNoMultiple(secondDiet.EatMap);
        slimeDiet.AdditionalFoodIdents = slimeDiet.AdditionalFoodIdents.AddRangeNoMultiple(firstDiet.AdditionalFoodIdents);
        slimeDiet.AdditionalFoodIdents = slimeDiet.AdditionalFoodIdents.AddRangeNoMultiple(secondDiet.AdditionalFoodIdents);
        slimeDiet.FavoriteIdents = slimeDiet.FavoriteIdents.AddRangeNoMultiple(firstDiet.FavoriteIdents);
        slimeDiet.FavoriteIdents = slimeDiet.FavoriteIdents.AddRangeNoMultiple(secondDiet.FavoriteIdents);
        slimeDiet.MajorFoodIdentifiableTypeGroups = slimeDiet.MajorFoodIdentifiableTypeGroups.AddRangeNoMultiple(firstDiet.MajorFoodIdentifiableTypeGroups);
        slimeDiet.MajorFoodIdentifiableTypeGroups = slimeDiet.MajorFoodIdentifiableTypeGroups.AddRangeNoMultiple(secondDiet.MajorFoodIdentifiableTypeGroups);
        slimeDiet.ProduceIdents = slimeDiet.ProduceIdents.AddRangeNoMultiple(firstDiet.ProduceIdents);
        slimeDiet.ProduceIdents = slimeDiet.ProduceIdents.AddRangeNoMultiple(secondDiet.ProduceIdents);
        return slimeDiet;
    }

    public static void SwitchSlimeAppearances(this SlimeDefinition slimeOneDef, SlimeDefinition slimeTwoDef)
    {
        Il2CppReferenceArray<SlimeAppearanceStructure> structures = slimeOneDef.AppearancesDefault[0]._structures;
        slimeOneDef.AppearancesDefault[0]._structures = slimeTwoDef.AppearancesDefault[0]._structures;
        slimeTwoDef.AppearancesDefault[0]._structures = structures;
        Color splatColor = slimeOneDef.AppearancesDefault[0]._splatColor;
        slimeOneDef.AppearancesDefault[0]._splatColor = slimeTwoDef.AppearancesDefault[0]._splatColor;
        slimeTwoDef.AppearancesDefault[0]._splatColor = splatColor;
        SlimeAppearance.Palette colorPalette = slimeOneDef.AppearancesDefault[0]._colorPalette;
        slimeOneDef.AppearancesDefault[0]._colorPalette = slimeTwoDef.AppearancesDefault[0]._colorPalette;
        slimeTwoDef.AppearancesDefault[0]._colorPalette = colorPalette;
        Sprite icon = slimeOneDef.AppearancesDefault[0]._icon;
        slimeOneDef.AppearancesDefault[0]._icon = slimeTwoDef.AppearancesDefault[0]._icon;
        slimeTwoDef.AppearancesDefault[0]._icon = icon;
        Sprite icon2 = slimeOneDef.icon;
        slimeOneDef.icon = slimeTwoDef.icon;
        slimeTwoDef.icon = icon2;
        Sprite debugIcon = slimeOneDef.debugIcon;
        slimeOneDef.debugIcon = slimeTwoDef.debugIcon;
        slimeTwoDef.debugIcon = debugIcon;
    }

    public static SlimeDiet.EatMapEntry CreateEatmap(SlimeEmotions.Emotion driver, float mindrive, IdentifiableType produce, IdentifiableType eat, IdentifiableType becomes)
    {
        return new SlimeDiet.EatMapEntry
        {
            EatsIdent = eat,
            ProducesIdent = produce,
            BecomesIdent = becomes,
            Driver = driver,
            MinDrive = mindrive
        };
    }

    public static SlimeDiet.EatMapEntry CreateEatmap(SlimeEmotions.Emotion driver, float mindrive, IdentifiableType produce, IdentifiableType eat)
    {
        return new SlimeDiet.EatMapEntry
        {
            EatsIdent = eat,
            ProducesIdent = produce,
            Driver = driver,
            MinDrive = mindrive
        };
    }

    public static void ModifyEatmap(this SlimeDiet.EatMapEntry eatmap, SlimeEmotions.Emotion driver, float mindrive, IdentifiableType produce, IdentifiableType eat, IdentifiableType becomes)
    {
        eatmap.EatsIdent = eat;
        eatmap.BecomesIdent = becomes;
        eatmap.ProducesIdent = produce;
        eatmap.Driver = driver;
        eatmap.MinDrive = mindrive;
    }

    public static void ModifyEatmap(this SlimeDiet.EatMapEntry eatmap, SlimeEmotions.Emotion driver, float mindrive, IdentifiableType produce, IdentifiableType eat)
    {
        eatmap.EatsIdent = eat;
        eatmap.ProducesIdent = produce;
        eatmap.Driver = driver;
        eatmap.MinDrive = mindrive;
    }

    public static void AddProduceIdent(this SlimeDefinition slimeDef, IdentifiableType ident)
    {
        slimeDef.Diet.ProduceIdents = slimeDef.Diet.ProduceIdents.Add(ident);
    }

    public static void SetProduceIdent(this SlimeDefinition slimeDef, IdentifiableType ident, int index)
    {
        slimeDef.Diet.ProduceIdents[index] = ident;
    }

    public static void AddExtraEatIdent(this SlimeDefinition slimeDef, IdentifiableType ident)
    {
        slimeDef.Diet.AdditionalFoodIdents = slimeDef.Diet.AdditionalFoodIdents.Add(ident);
    }

    public static void SetFavoriteProduceCount(this SlimeDefinition slimeDef, int count)
    {
        slimeDef.Diet.FavoriteProductionCount = count;
    }

    public static void AddFavorite(this SlimeDefinition slimeDef, IdentifiableType id)
    {
        slimeDef.Diet.FavoriteIdents = slimeDef.Diet.FavoriteIdents.Add(id);
    }

    public static void AddEatmapToSlime(this SlimeDefinition slimeDef, SlimeDiet.EatMapEntry eatmap)
    {
        if (slimeDef == null || eatmap == null || eatmap.EatsIdent == null)
        {
            MelonLogger.Warning("[Cotton] Skipped invalid eatmap entry for " + ((slimeDef != null) ? slimeDef.name : "null slime") + " (missing food ident).");
            return;
        }
        if (!customEatmaps.TryGetValue(slimeDef, out var value))
        {
            value = new System.Collections.Generic.List<SlimeDiet.EatMapEntry>();
            customEatmaps.Add(slimeDef, value);
        }
        value.Add(eatmap);
        slimeDef.Diet.EatMap.Add(eatmap);
    }

    public static void SetStructColor(this SlimeAppearanceStructure structure, int id, Color color)
    {
        structure.DefaultMaterials[0].SetColor(id, color);
    }

    public static void RefreshEatmap(this SlimeDefinition def)
    {
        def.Diet.RefreshEatMap(slimeDefinitions, def);
    }

    public static void ChangeSlimeFoodGroup(this SlimeDefinition def, IdentifiableTypeGroup FG, int index)
    {
        def.Diet.MajorFoodIdentifiableTypeGroups[index] = FG;
    }

    public static void AddSlimeFoodGroup(this SlimeDefinition def, IdentifiableTypeGroup FG)
    {
        // fork: 0.3.1 added the group twice
        if (def == null || def.Diet == null || FG == null) return;
        var groups = def.Diet.MajorFoodIdentifiableTypeGroups;
        if (groups != null)
        {
            foreach (IdentifiableTypeGroup g in groups)
            {
                if (g != null && g.Pointer == FG.Pointer) return;
            }
        }
        def.Diet.MajorFoodIdentifiableTypeGroups = groups != null ? groups.Add(FG) : new Il2CppReferenceArray<IdentifiableTypeGroup>(new IdentifiableTypeGroup[] { FG });
    }

    internal static SlimeDiet INTERNAL_CreateNewDiet()
    {
        return new SlimeDiet
        {
            ProduceIdents = new Il2CppReferenceArray<IdentifiableType>(0L),
            FavoriteProductionCount = 2,
            EatMap = new Il2CppSystem.Collections.Generic.List<SlimeDiet.EatMapEntry>(0),
            FavoriteIdents = new Il2CppReferenceArray<IdentifiableType>(0L),
            AdditionalFoodIdents = new Il2CppReferenceArray<IdentifiableType>(0L),
            MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(0L),
            BecomesOnTarrifyIdentifiableType = Get<IdentifiableType>("Tarr"),
            EdiblePlortIdentifiableTypeGroup = Get<IdentifiableTypeGroup>("EdiblePlortFoodGroup"),
            StableResourceIdentifiableTypeGroup = Get<IdentifiableTypeGroup>("StableResourcesGroup"),
            UnstableResourceIdentifiableTypeGroup = Get<IdentifiableTypeGroup>("UnstableResourcesGroup"),
            UnstablePlort = GetPlort("UnstablePlort")
        };
    }

    public static SlimeDefinition GetSlime(string name)
    {
        foreach (IdentifiableType item in slimes.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item.Cast<SlimeDefinition>();
            }
        }
        return null;
    }

    public static void SetSlimeColorSpecific(this SlimeDefinition slimeDef, Color32 top, Color32 middle, Color32 bottom, Color32 special, int index, int index2, bool isSS, int structure)
    {
        Material obj = ((!isSS) ? slimeDef.AppearancesDefault[index].Structures[structure].DefaultMaterials[index2] : slimeDef.AppearancesDynamic.ToArray()[index].Structures[structure].DefaultMaterials[index2]);
        obj.SetColor("_TopColor", top);
        obj.SetColor("_MiddleColor", middle);
        obj.SetColor("_BottomColor", bottom);
        obj.SetColor("_SpecColor", special);
    }

    public static void SetSlimeColor(this SlimeDefinition slimeDef, Color32 top, Color32 middle, Color32 bottom)
    {
        for (int i = 0; i < slimeDef.AppearancesDefault[0].Structures.Count - 1; i++)
        {
            Material material = slimeDef.AppearancesDefault[0].Structures[i].DefaultMaterials[0];
            material.SetColor("_TopColor", top);
            material.SetColor("_MiddleColor", middle);
            material.SetColor("_BottomColor", bottom);
            material.SetColor("_SpecColor", middle);
        }
    }

    public static void SetTwinColor(this SlimeDefinition slimeDef, Color32 top, Color32 middle, Color32 bottom)
    {
        for (int i = 0; i < slimeDef.AppearancesDefault[0].Structures.Count - 1; i++)
        {
            Material material = slimeDef.AppearancesDefault[0].Structures[i].DefaultMaterials[0];
            material.SetColor("_TwinTopColor", top);
            material.SetColor("_TwinMiddleColor", middle);
            material.SetColor("_TwinBottomColor", bottom);
            material.SetColor("_TwinSpecColor", middle);
        }
    }

    public static void SetSloomberColor(this SlimeDefinition slimeDef, Color32 top, Color32 middle, Color32 bottom)
    {
        for (int i = 0; i < slimeDef.AppearancesDefault[0].Structures.Count - 1; i++)
        {
            Material material = slimeDef.AppearancesDefault[0].Structures[i].DefaultMaterials[0];
            material.SetColor("_SloomberTopColor", top);
            material.SetColor("_SloomberMiddleColor", middle);
            material.SetColor("_SloomberBottomColor", bottom);
        }
    }

    public static void SetTwinColorSpecific(this SlimeDefinition slimeDef, Color32 top, Color32 middle, Color32 bottom, int index, int index2, bool isSS, int structure)
    {
        Material obj = ((!isSS) ? slimeDef.AppearancesDefault[index].Structures[structure].DefaultMaterials[index2] : slimeDef.AppearancesDynamic.ToArray()[index].Structures[structure].DefaultMaterials[index2]);
        obj.SetColor("_TwinTopColor", top);
        obj.SetColor("_TwinMiddleColor", middle);
        obj.SetColor("_TwinBottomColor", bottom);
    }

    public static void SetSloomberColorSpecific(this SlimeDefinition slimeDef, Color32 top, Color32 middle, Color32 bottom, int index, int index2, bool isSS, int structure)
    {
        Material obj = ((!isSS) ? slimeDef.AppearancesDefault[index].Structures[structure].DefaultMaterials[index2] : slimeDef.AppearancesDynamic.ToArray()[index].Structures[structure].DefaultMaterials[index2]);
        obj.SetColor("_SloomberTopColor", top);
        obj.SetColor("_SloomberMiddleColor", middle);
        obj.SetColor("_SloomberBottomColor", bottom);
    }

    public static void EnableTwinEffectSpecific(this SlimeDefinition slimeDef, int index, int index2, bool isSS, int structure)
    {
        ((!isSS) ? slimeDef.AppearancesDefault[index].Structures[structure].DefaultMaterials[index2] : slimeDef.AppearancesDynamic.ToArray()[index].Structures[structure].DefaultMaterials[index2]).EnableKeyword("_ENABLETWINEFFECT_ON");
    }

    public static void EnableTwinEffect(this SlimeDefinition slimeDef)
    {
        for (int i = 0; i < slimeDef.AppearancesDefault[0].Structures.Count - 1; i++)
        {
            slimeDef.AppearancesDefault[0].Structures[i].DefaultMaterials[0].EnableKeyword("_ENABLETWINEFFECT_ON");
        }
    }

    public static void EnableSloomberEffect(this SlimeDefinition slimeDef)
    {
        for (int i = 0; i < slimeDef.AppearancesDefault[0].Structures.Count - 1; i++)
        {
            slimeDef.AppearancesDefault[0].Structures[i].DefaultMaterials[0].EnableKeyword("_BODYCOLORING_SLOOMBER");
        }
    }

    public static void DisableTwinEffect(this SlimeDefinition slimeDef, int index, int index2, bool isSS, int structure)
    {
        ((!isSS) ? slimeDef.AppearancesDefault[index].Structures[structure].DefaultMaterials[index2] : slimeDef.AppearancesDynamic.ToArray()[index].Structures[structure].DefaultMaterials[index2]).DisableKeyword("_ENABLETWINEFFECT_ON");
    }

    public static void SetSlimeMatTopColor(this Material mat, Color color)
    {
        mat.SetColor("_TopColor", color);
    }

    public static void SetSlimeMatMiddleColor(this Material mat, Color color)
    {
        mat.SetColor("_MiddleColor", color);
    }

    public static void SetSlimeMatBottomColor(this Material mat, Color color)
    {
        mat.SetColor("_BottomColor", color);
    }

    public static void SetSlimeMatColors(this Material material, Color32 Top, Color32 Middle, Color32 Bottom, Color32 Specular)
    {
        material.SetColor("_TopColor", Top);
        material.SetColor("_MiddleColor", Middle);
        material.SetColor("_BottomColor", Bottom);
        material.SetColor("_SpecColor", Specular);
    }

    public static void SetSlimeMatColors(this Material material, Color32 Top, Color32 Middle, Color32 Bottom)
    {
        material.SetColor("_TopColor", Top);
        material.SetColor("_MiddleColor", Middle);
        material.SetColor("_BottomColor", Bottom);
    }

    public static SlimeDefinition GetBaseSlime(string name)
    {
        foreach (IdentifiableType item in baseSlimes.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item.Cast<SlimeDefinition>();
            }
        }
        return null;
    }

    public static SlimeDefinition GetLargo(string name)
    {
        foreach (IdentifiableType item in largos.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item.Cast<SlimeDefinition>();
            }
        }
        return null;
    }

    public static SlimeDiet.EatMapEntry GetEatMap(SlimeDiet diet, IdentifiableType type)
    {
        return diet.EatMap._items.FirstOrDefault((SlimeDiet.EatMapEntry eatmap) => eatmap.EatsIdent == type);
    }

    public static bool TryGetEatMap(SlimeDiet diet, IdentifiableType type, out SlimeDiet.EatMapEntry eatMap)
    {
        SlimeDiet.EatMapEntry eatMapEntry = null;
        try
        {
            eatMapEntry = GetEatMap(diet, type);
            if (eatMapEntry != null)
            {
                eatMap = eatMapEntry;
                return true;
            }
            eatMap = null;
            return false;
        }
        catch
        {
            eatMap = null;
            return false;
        }
    }

    private static bool Where_map_GetEatMap1(SlimeDiet.EatMapEntry map, string group)
    {
        if (map == null)
        {
            return false;
        }
        if (map.EatsIdent == null)
        {
            return false;
        }
        return map.EatsIdent.name.Contains(group);
    }

    private static bool Where_map_GetEatMap2(SlimeDiet.EatMapEntry map, IdentifiableTypeGroup group)
    {
        if (map == null)
        {
            return false;
        }
        if (map.EatsIdent == null)
        {
            return false;
        }
        if (group == null)
        {
            return false;
        }
        return group.GetAllMembers()?.Contains(map.EatsIdent) ?? false;
    }

    public static SlimeDiet.EatMapEntry[] GetEatMapsByName(SlimeDiet diet, string group)
    {
        return diet?.EatMap?._items.Where((SlimeDiet.EatMapEntry map) => Where_map_GetEatMap1(map, group)).ToArray();
    }

    public static SlimeDiet.EatMapEntry[] GetEatMapsByIdentifiableGroup(SlimeDiet diet, IdentifiableTypeGroup group)
    {
        return diet?.EatMap?._items.Where((SlimeDiet.EatMapEntry map) => Where_map_GetEatMap2(map, group)).ToArray();
    }

    public static IdentifiableType CreateGordoType(string name, Sprite icon, LocalizedString localizedName, string refID)
    {
        IdentifiableType identifiableType = UnityEngine.Object.Instantiate(Get<IdentifiableType>("PinkGordo"));
        identifiableType.name = name + "Gordo";
        identifiableType.icon = icon;
        identifiableType.localizedName = localizedName;
        identifiableType.referenceId = refID;
        identifiableType.AddToGroup("GordoGroup");
        INTERNAL_SetupLoadForIdent(refID, identifiableType);
        return identifiableType;
    }

    public static GameObject CreateGordoObject(GameObject baseObj, IdentifiableType gordoType, SlimeDefinition baseSlime, GordoFaceData face, params Material[] materials)
    {
        GameObject gameObject = baseObj.CopyObject();
        gameObject.GetComponent<GordoIdentifiable>().identType = gordoType;
        gameObject.GetComponent<GordoEat>().SlimeDefinition = baseSlime;
        GordoFaceComponents component = gameObject.GetComponent<GordoFaceComponents>();
        if (face.eyesBlink != null)
        {
            component.BlinkEyes = face.eyesBlink;
        }
        if (face.eyesNormal != null)
        {
            component.StrainEyes = face.eyesNormal;
        }
        if (face.mouthHappy != null)
        {
            component.HappyMouth = face.mouthHappy;
        }
        if (face.mouthChompOpen != null)
        {
            component.ChompOpenMouth = face.mouthChompOpen;
        }
        if (face.mouthEating != null)
        {
            component.StrainMouth = face.mouthEating;
        }
        Il2CppArrayBase<SkinnedMeshRenderer> componentsInChildren = gameObject.transform.GetComponentsInChildren<SkinnedMeshRenderer>();
        int num = 0;
        foreach (SkinnedMeshRenderer item in componentsInChildren)
        {
            if (materials.Length > num && materials[num] != null)
            {
                item.material = materials[num];
            }
            num++;
        }
        SRSingleton<GameContext>.Instance.LookupDirector._gordoDict.Add(gordoType, gameObject);
        SRSingleton<GameContext>.Instance.LookupDirector._gordoEntries.items.Add(gameObject);
        gordoType.prefab = gameObject;
        return gameObject;
    }

    public static void DebuggingPrintGordoAttachments(GameObject baseObj)
    {
        string text = "[DEBUG] Printing gordo attachments for " + baseObj.name + "\n";
        int value = 0;
        foreach (SkinnedMeshRenderer componentsInChild in baseObj.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            text += $"[index: {value}] {componentsInChild.gameObject.name} -- {componentsInChild.material.name}\n";
        }
        MelonLogger.Msg(text);
    }

    public static void SetRequiredBait(this GameObject gordo, IdentifiableType baitType)
    {
        GordoIdentifiable component = gordo.GetComponent<GordoIdentifiable>();
        if (!component)
        {
            throw new InvalidCastException("You cannot set the bait for this object as it is not a gordo!");
        }
        gordoBaitDict.Add(baitType.name, component.identType);
    }
}
