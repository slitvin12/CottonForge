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
    internal struct ModdedLocalizedText
    {
        public object[] parameters;

        public string table;

        public string srKey;

        public LocalizedString str;
    }

    internal static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>> languages = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>>();

    private static System.Collections.Generic.Dictionary<string, string> loadedLanguage = new System.Collections.Generic.Dictionary<string, string>();

    private static System.Collections.Generic.Dictionary<string, string> defaultLang = null;

    internal static System.Collections.Generic.Dictionary<string, LocalizedString> loadedLocalizedStrings = new System.Collections.Generic.Dictionary<string, LocalizedString>();

    internal static System.Collections.Generic.Dictionary<string, ModdedLocalizedText> moddedLocalizedStrings = new System.Collections.Generic.Dictionary<string, ModdedLocalizedText>();

    /// <summary>
    /// Loads a string from the modded language csv.
    /// </summary>
    /// <param name="key">The key for the text</param>
    /// <returns>The text from the language selected in-game.</returns>
    public static string LoadLocalizedText(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !loadedLanguage.ContainsKey(key))
        {
            return key;
        }
        return loadedLanguage[key];
    }

    /// <summary>
    /// Loads a string from the modded language csv.
    /// </summary>
    /// <param name="args">The arguments for the string.</param>
    /// <param name="key">The key for the text</param>
    /// <returns>The text from the language selected in-game.</returns>
    public static string LoadLocalizedText(string key, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key) || !loadedLanguage.ContainsKey(key))
        {
            return key;
        }
        int num = 1;
        string text = loadedLanguage[key];
        foreach (object obj in args)
        {
            text = text.Replace($"${num}", obj.ToString());
            num++;
        }
        return text;
    }

    /// <summary>
    /// Creates a LocalizedString from the <c>LoadLocalizedText</c> function.
    /// </summary>
    /// <param name="key">The key for the text</param>
    /// <param name="table">The table to store the LocalizedString in</param>
    /// <param name="args">The arguments for the LocalizedString.</param>
    /// <returns>A new LocalizedString</returns>
    public static LocalizedString CreateLocalizedString(string key, string table = "Actor")
    {
        return CreateLocalizedString(key, table, new object[0]);
    }

    /// <summary>
    /// Creates a LocalizedString from the <c>LoadLocalizedText</c> function.
    /// </summary>
    /// <param name="key">The key for the text</param>
    /// <param name="table">The table to store the LocalizedString in</param>
    /// <param name="args">The arguments for the LocalizedString.</param>
    /// <returns>A new LocalizedString</returns>
    public static LocalizedString CreateLocalizedString(string key, string table = "Actor", params object[] args)
    {
        LocalizedString localizedString = CreateStaticString(LoadLocalizedText(key, args), key, table);
        loadedLocalizedStrings.TryAdd(key, localizedString);
        moddedLocalizedStrings.TryAdd(key, new ModdedLocalizedText
        {
            str = localizedString,
            table = table,
            srKey = key,
            parameters = args
        });
        return localizedString;
    }

    /// <summary>
    /// Add a localization table.
    /// </summary>
    /// <param name="csvFile">The <c>.CSV</c> file containing the translations</param>
    public static void AddLanguages(string csvFile)
    {
        System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> dictionary = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>();
        System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
        Assembly callingAssembly = Assembly.GetCallingAssembly();
        Stream stream = callingAssembly.GetManifestResourceStream(callingAssembly.GetName().Name + "." + csvFile + ".csv");
        if (stream == null)
        {
            // fork: clear message instead of ArgumentNullException from TextFieldParser
            MelonLogger.Error($"[Cotton] AddLanguages: embedded resource {callingAssembly.GetName().Name}.{csvFile}.csv not found (EmbeddedResource in the .csproj?)");
            return;
        }
        using (TextFieldParser textFieldParser = new TextFieldParser(stream))
        {
            textFieldParser.CommentTokens = new string[1] { "#" };
            textFieldParser.SetDelimiters(",");
            textFieldParser.HasFieldsEnclosedInQuotes = true;
            bool flag = true;
            while (!textFieldParser.EndOfData)
            {
                string[] array = textFieldParser.ReadFields();
                if (flag)
                {
                    flag = false;
                    if (array == null || array.Length < 1)
                    {
                        return;
                    }
                    bool flag2 = true;
                    string[] array2 = array;
                    foreach (string text in array2)
                    {
                        if (flag2)
                        {
                            flag2 = false;
                            continue;
                        }
                        if (!dictionary.ContainsKey(text))
                        {
                            dictionary[text] = new System.Collections.Generic.Dictionary<string, string>();
                        }
                        list.Add(text);
                    }
                }
                else
                {
                    if (array == null || array.Length < 1)
                    {
                        continue;
                    }
                    bool flag3 = true;
                    string key = array[0];
                    int num = 0;
                    string[] array2 = array;
                    foreach (string text2 in array2)
                    {
                        if (flag3)
                        {
                            flag3 = false;
                            continue;
                        }
                        // fork: an empty cell falls back to English instead of overriding it with ""
                        if (list.Count > num && !string.IsNullOrEmpty(text2))
                        {
                            dictionary[list[num]][key] = text2.Replace("\\n", "\n");
                        }
                        num++;
                    }
                }
            }
        }
        foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.Dictionary<string, string>> item in dictionary)
        {
            string key2 = item.Key;
            if (!languages.ContainsKey(key2))
            {
                languages.Add(key2, new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>());
            }
            languages[key2].Add(item.Value);
        }
        // fork: the merged English table is cached on the first LoadLanguage; rebuild it so these keys are included
        defaultLang = null;
        Patches.LocalizationRepair.RequestReload();
    }

    public static void LoadLanguage(string code)
    {
        loadedLanguage = new System.Collections.Generic.Dictionary<string, string>();
        if (defaultLang == null)
        {
            defaultLang = new System.Collections.Generic.Dictionary<string, string>();
            // fork: no English table (no mod with localization) threw KeyNotFoundException
            if (!languages.ContainsKey("en"))
            {
                languages["en"] = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>();
            }
            foreach (System.Collections.Generic.Dictionary<string, string> item in languages["en"])
            {
                foreach (System.Collections.Generic.KeyValuePair<string, string> item2 in item)
                {
                    defaultLang[item2.Key] = item2.Value;
                }
            }
        }
        loadedLanguage = new System.Collections.Generic.Dictionary<string, string>(defaultLang);
        if (!(code != "en") || !languages.ContainsKey(code))
        {
            return;
        }
        foreach (System.Collections.Generic.Dictionary<string, string> item3 in languages[code])
        {
            foreach (System.Collections.Generic.KeyValuePair<string, string> item4 in item3)
            {
                loadedLanguage[item4.Key] = item4.Value;
            }
        }
    }

    /// <summary>
    /// Renamed from <c>AddTranslation</c> in 0.3.0
    /// </summary>
    /// <param name="localized">The text to put in the localized string</param>
    /// <param name="key">The localized string's key</param>
    /// <param name="table">The localization table</param>
    /// <returns>A <c>LocalizedString</c> with your text.   </returns>
    public static LocalizedString CreateStaticString(string localized, string key = "l.Empty", string table = "Actor")
    {
        StringTable table2 = LocalizationUtil.GetTable(table);
        return new LocalizedString(entryReference: table2.AddEntry(key, localized).SharedEntry.Id, tableReference: table2.SharedData.TableCollectionName);
    }
}
