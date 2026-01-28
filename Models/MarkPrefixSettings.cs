using System;
using System.Collections.Generic;
using System.Linq;
using JSE_Parameter_Service.Services;
using JSE_Parameter_Service.Services.Logging;

namespace JSE_Parameter_Service.Models
{
    /// <summary>
    /// Mark prefix configuration for MEPMARK parameter
    /// Stores project-level and category-specific prefixes
    /// Used for UI state transfer to mark parameter command
    /// </summary>
    public class MarkPrefixSettings
    {
        public string ProjectPrefix { get; set; } = "";
        public string DuctPrefix { get; set; } = "DCT";
        public string PipePrefix { get; set; } = "PLU";
        public string CableTrayPrefix { get; set; } = "ELE";
        public string DamperPrefix { get; set; } = "DMP";
        
        public bool RemarkAll { get; set; } = false;
        public string NumberFormat { get; set; } = "000";
        
        public bool RemarkProjectPrefix { get; set; } = false;
        public bool RemarkDuctPrefix { get; set; } = true;
        public bool RemarkPipePrefix { get; set; } = true;
        public bool RemarkCableTrayPrefix { get; set; } = true;
        public bool RemarkDamperPrefix { get; set; } = true;
        
        public bool ActiveViewOnly { get; set; } = false;
        public int StartNumber { get; set; } = 1;
        public bool UseContinueNumbering { get; set; } = false;
        public string? ContinueFromViewName { get; set; }

        /// <summary>
        /// Enable advanced prefix resolution logic based on constituent sleeves in clusters/combined.
        /// </summary>
        public bool UseAdvancedPrefixResolution { get; set; } = true;

        public Dictionary<string, string> DuctSystemTypeOverrides { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> PipeSystemTypeOverrides { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> DuctAccessoriesSystemTypeOverrides { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> CableTrayServiceTypeOverrides { get; set; } = new Dictionary<string, string>();
        
        public string GetDisciplinePrefix(string category)
        {
            return category switch
            {
                "Ducts" => DuctPrefix,
                "Pipes" => PipePrefix,
                "Cable Trays" => CableTrayPrefix,
                "Duct Accessories" => DamperPrefix,
                _ => "OPN"
            };
        }
        
        public bool GetRemarkFlag(string category)
        {
            if (RemarkAll || RemarkProjectPrefix) return true;
            
            return category switch
            {
                "Ducts" => RemarkDuctPrefix,
                "Pipes" => RemarkPipePrefix,
                "Cable Trays" => RemarkCableTrayPrefix,
                "Duct Accessories" => RemarkDamperPrefix,
                _ => false
            };
        }
        
        public string GetPrefixForElement(string category, string? systemType = null, string? serviceType = null)
        {
            NumberingDebugLogger.LogInfo($"[MarkPrefixSettings] Resolving prefix for Category: {category}, SystemType: '{systemType}', ServiceType: '{serviceType}'");

            string? overriden = null;

            if (category == "Ducts") overriden = ResolveOverride(DuctSystemTypeOverrides, systemType, NormalizeMepType(systemType), "Duct");
            else if (category == "Pipes") overriden = ResolveOverride(PipeSystemTypeOverrides, systemType, NormalizeMepType(systemType), "Pipe");
            else if (category == "Duct Accessories") overriden = ResolveOverride(DuctAccessoriesSystemTypeOverrides, systemType, NormalizeMepType(systemType), "Duct Acc");
            else if (category == "Cable Trays") overriden = ResolveOverride(CableTrayServiceTypeOverrides, serviceType, NormalizeMepType(serviceType), "Tray");

            if (overriden != null) return overriden;

            return GetDisciplinePrefix(category);
        }

        private string? ResolveOverride(Dictionary<string, string> overrides, string? original, string? normalized, string logLabel)
        {
            if (overrides == null || overrides.Count == 0) return null;

            string searchNorm = normalized ?? "";
            
            // 1. Precise Match (Original Keys)
            if (!string.IsNullOrEmpty(original) && overrides.TryGetValue(original, out var exact)) return exact;

            // 2. Precise Match (Thematic/Normalized)
            // This is CRITICAL: If searching for "CHWS", and UI has "CHILLED WATER SUPPLY",
            // both normalize to "CHILLED" and should match.
            if (!string.IsNullOrEmpty(searchNorm))
            {
                var normMatch = overrides.FirstOrDefault(kvp => 
                    string.Equals(NormalizeMepType(kvp.Key), searchNorm, StringComparison.OrdinalIgnoreCase));
                
                if (!string.IsNullOrEmpty(normMatch.Key))
                {
                    NumberingDebugLogger.LogInfo($"[ResolveOverride] Thematic match found! '{original}' -> '{normMatch.Key}' (Theme: '{searchNorm}') -> Result: '{normMatch.Value}'");
                    return normMatch.Value;
                }
            }

            // 3. Partial Match (Heuristic Substring)
            string searchString = original ?? "";
            var matches = overrides
                .Where(kvp => 
                {
                    string keyNorm = NormalizeMepType(kvp.Key) ?? kvp.Key;
                    return searchString.IndexOf(keyNorm, StringComparison.OrdinalIgnoreCase) >= 0 || 
                           keyNorm.IndexOf(searchString, StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .OrderByDescending(kvp => kvp.Key.Length)
                .ToList();

            if (matches.Any()) return matches.First().Value;

            return null;
        }

        public string? NormalizeMepType(string? type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            
            string normalized = type.Trim();

            // ✅ ROBUST CHILLED WATER THEME:
            // Normalize "CHILLED WATER SUPPLY", "CHW S", "CHWR", "CHRW", etc. all to a common "CHILLED" theme
            if (normalized.IndexOf("chille", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("CHW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("CHRW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("CHRSS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "CHILLED";
            }

            // Standard character replacement for other systems
            return normalized.Replace(" - ", " ")
                           .Replace("-", " ")
                           .Replace("_", " ")
                           .Replace("/", " ")
                           .Replace(".", " ")
                           .Trim();
        }
    }
}
