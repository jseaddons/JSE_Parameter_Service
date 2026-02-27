using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using JSE_Parameter_Service.Models;
using JSE_Parameter_Service.Services;
using JSE_Parameter_Service.Services.Logging;

namespace JSE_Parameter_Service.Services
{
    /// <summary>
    /// Handles persistence of System Type Override settings to/from JSON file
    /// Saves to: %AppData%\JSE_Parameter_Service\MarkPrefixSettings.json
    /// AND separate override files for isolation.
    /// </summary>
    public static class SystemTypeOverridePersistenceService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JSE_Parameter_Service"
        );

        private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "MarkPrefixSettings.json");
        
        // Separate files for strict isolation
        private static readonly string DuctsOverridePath = Path.Combine(AppDataFolder, "Overrides_Ducts.json");
        private static readonly string PipesOverridePath = Path.Combine(AppDataFolder, "Overrides_Pipes.json");
        private static readonly string TraysOverridePath = Path.Combine(AppDataFolder, "Overrides_CableTrays.json");
        private static readonly string AccOverridePath = Path.Combine(AppDataFolder, "Overrides_DuctAccessories.json");

        /// <summary>
        /// Load settings from JSON file, AND load separate override files
        /// </summary>
        public static MarkPrefixSettings LoadSettings()
        {
            RemarkDebugLogger.LogInfo("[UI-PERSIST] === LoadSettings (Multi-File) CALLED ===");
            
            MarkPrefixSettings settings = new MarkPrefixSettings();

            try
            {
                // 1. Load Main Settings (Prefixes, etc.)
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    settings = JsonSerializer.Deserialize<MarkPrefixSettings>(json) ?? new MarkPrefixSettings();
                    RemarkDebugLogger.LogInfo("[UI-PERSIST] Loaded Main Settings.");
                }
                else
                {
                    RemarkDebugLogger.LogInfo("[UI-PERSIST] Main settings file not found, using defaults.");
                }

                // 2. Load Overrides from separate files
                settings.DuctSystemTypeOverrides = LoadDictionary(DuctsOverridePath, "Ducts");
                settings.PipeSystemTypeOverrides = LoadDictionary(PipesOverridePath, "Pipes");
                settings.CableTrayServiceTypeOverrides = LoadDictionary(TraysOverridePath, "CableTrays");
                settings.DuctAccessoriesSystemTypeOverrides = LoadDictionary(AccOverridePath, "DuctAccessories");

                return settings;
            }
            catch (Exception ex)
            {
                RemarkDebugLogger.LogError($"[UI-PERSIST] Exception during load: {ex.Message}");
                return new MarkPrefixSettings();
            }
        }

        private static Dictionary<string, string> LoadDictionary(string path, string label)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    RemarkDebugLogger.LogInfo($"[UI-PERSIST] Loaded {dict?.Count ?? 0} overrides for {label}");
                    return dict ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                RemarkDebugLogger.LogWarning($"[UI-PERSIST] Failed to load {label} overrides: {ex.Message}");
            }
            return new Dictionary<string, string>();
        }

        /// <summary>
        /// Save settings to JSON file AND separate override files
        /// </summary>
        public static void SaveSettings(MarkPrefixSettings settings)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder)) Directory.CreateDirectory(AppDataFolder);

                var options = new JsonSerializerOptions { WriteIndented = true };

                // 1. Save Main Settings (Overrides are [JsonIgnore] now)
                string mainJson = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFilePath, mainJson);
                RemarkDebugLogger.LogInfo("[UI-PERSIST] Saved Main Settings.");

                // 2. Save Overrides to separate files
                SaveDictionary(DuctsOverridePath, settings.DuctSystemTypeOverrides, "Ducts");
                SaveDictionary(PipesOverridePath, settings.PipeSystemTypeOverrides, "Pipes");
                SaveDictionary(TraysOverridePath, settings.CableTrayServiceTypeOverrides, "CableTrays");
                SaveDictionary(AccOverridePath, settings.DuctAccessoriesSystemTypeOverrides, "DuctAccessories");
            }
            catch (Exception ex)
            {
                RemarkDebugLogger.LogError($"[UI-PERSIST] Exception during save: {ex.Message}");
            }
        }

        private static void SaveDictionary(string path, Dictionary<string, string> dict, string label)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(dict ?? new Dictionary<string, string>(), options);
                File.WriteAllText(path, json);
                RemarkDebugLogger.LogInfo($"[UI-PERSIST] Saved {label} overrides to {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                RemarkDebugLogger.LogError($"[UI-PERSIST] Failed to save {label} overrides: {ex.Message}");
            }
        }
    }
}
