using System;
using System.Collections.Generic;
using System.Linq;
using JSE_Parameter_Service.Models;
using JSE_Parameter_Service.Services.Logging;

namespace JSE_Parameter_Service.Services.Helpers
{
    public static class MarkPrefixHelper
    {
        public static string? ResolveAdvancedPrefix(ClashZone cz, MarkPrefixSettings settings, IMarkCacheService cache)
        {
            // 0. Safety/Flag check (redundant but safe)
            if (!settings.UseAdvancedPrefixResolution || !OptimizationFlags.EnableAdvancedMarkingOptimization)
                return null;

            // 1. Identify context (Combined vs Cluster vs Individual)
            if (cz.CombinedClusterSleeveInstanceId > 0)
            {
                return ResolveCombinedPrefix(cz, settings, cache);
            }
            
            if (cz.ClusterInstanceId > 0)
            {
                return ResolveClusterPrefix(cz, settings, cache);
            }

            return null; // Fallback for individual or unhandled
        }

        private static string ResolveCombinedPrefix(ClashZone cz, MarkPrefixSettings settings, IMarkCacheService cache)
        {
            var zones = cache.GetZonesForCombined(cz.CombinedClusterSleeveInstanceId).ToList();
            NumberingDebugLogger.LogInfo($"[ResolveCombinedPrefix] Combined {cz.CombinedClusterSleeveInstanceId}: Loaded {zones.Count} constituents from DB.");
            
            if (!zones.Any()) return "MEP";

            // RULE #2: Multi-Link Combined -> "MEP" (Hardcoded)
            // Even if it contains chilled water, multi-link takes precedence per "Rule: If zones are from different links -> use 'MEP' prefix"
            if (IsMultiLink(zones))
            {
                NumberingDebugLogger.LogInfo($"[ResolveCombinedPrefix] Combined {cz.CombinedClusterSleeveInstanceId}: Multi-link detected (SourceKeys: {string.Join(", ", zones.Select(z => z.SourceDocKey).Distinct())}). Hardcoding to 'MEP'.");
                return "MEP";
            }

            // RULE #3: Single-Link Combined + "Chilled" -> User Override (EXCEPTION)
            // "EXCEPTION: Single link + 'chilled water' system -> use user-defined prefix from system type overrides"
            
            // Log analysis of constituents for debugging
            foreach(var z in zones)
            {
                 NumberingDebugLogger.LogInfo($"[ResolveCombinedPrefix] Constituent Analysis: ID={z.ClashZoneId}, Cat='{z.MepElementCategory}', Sys='{z.MepSystemType}', Norm='{GetNormalizedSystemType(z.MepSystemType)}'");
            }

            // Check for Chilled Theme
            bool hasChilledWater = zones.Any(z => 
                !string.IsNullOrEmpty(z.MepSystemType) && 
                (z.MepSystemType.IndexOf("chilled water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 GetNormalizedSystemType(z.MepSystemType).Equals("CHILLED", StringComparison.OrdinalIgnoreCase)));

            if (hasChilledWater)
            {
                // Find the first chilled zone to resolve the prefix
                var cwZone = zones.First(z => 
                    !string.IsNullOrEmpty(z.MepSystemType) && 
                    (z.MepSystemType.IndexOf("chilled water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     GetNormalizedSystemType(z.MepSystemType).Equals("CHILLED", StringComparison.OrdinalIgnoreCase)));
                
                // Get User-Defined Prefix
                string prefix = settings.GetPrefixForElement(cwZone.MepElementCategory, cwZone.MepSystemType, cwZone.MepServiceType);
                
                // Only return if it's NOT the default category prefix (meaning an override exists)
                // Actually the doc says "use user-defined prefix". settings.GetPrefixForElement returns override OR discipline default.
                // We should use it.
                
                NumberingDebugLogger.LogInfo($"[ResolveCombinedPrefix] Combined {cz.CombinedClusterSleeveInstanceId}: Rule #3 'CHILLED' Exception. Single-link + Chilled. Using '{prefix}' from UI settings for '{cwZone.MepSystemType}'.");
                return prefix;
            }

            // DEFAULT: Normal combined sleeve -> "MEP"
            NumberingDebugLogger.LogInfo($"[ResolveCombinedPrefix] Combined {cz.CombinedClusterSleeveInstanceId}: Default Rule. Not Multi-link, Not Chilled. Hardcoding to 'MEP'.");
            return "MEP";
        }

        private static string ResolveClusterPrefix(ClashZone cz, MarkPrefixSettings settings, IMarkCacheService cache)
        {
            var zones = cache.GetZonesForCluster(cz.ClusterInstanceId).ToList();
            NumberingDebugLogger.LogInfo($"[ResolveClusterPrefix] Cluster {cz.ClusterInstanceId}: Loaded {zones.Count} constituents from DB.");

            if (!zones.Any()) return null;

            // RULE #2 Equivalent: Multi-Link Cluster -> "MEP"
            if (IsMultiLink(zones)) 
            {
                 NumberingDebugLogger.LogInfo($"[ResolveClusterPrefix] Cluster {cz.ClusterInstanceId}: Multi-link detected. Hardcoding to 'MEP'.");
                 return "MEP";
            }

            // Log analysis
            foreach(var z in zones)
            {
                 // NumberingDebugLogger.LogInfo($"[ResolveClusterPrefix] Constituent Analysis: ID={z.ClashZoneId}, Cat='{z.MepElementCategory}', Sys='{z.MepSystemType}', Norm='{GetNormalizedSystemType(z.MepSystemType)}'");
            }

            // User Requirement: "IF SYSTEM TYP IS CHILLED OR CHW AFTER NOMALISATION I EXPECT AC"
            // This applies to Clusters too.
            
            // Check for Chilled Theme (Priority)
            bool hasChilledWater = zones.Any(z => 
                !string.IsNullOrEmpty(z.MepSystemType) && 
                (z.MepSystemType.IndexOf("chilled water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 GetNormalizedSystemType(z.MepSystemType).Equals("CHILLED", StringComparison.OrdinalIgnoreCase)));

            if (hasChilledWater)
            {
                var cwZone = zones.First(z => 
                    !string.IsNullOrEmpty(z.MepSystemType) && 
                    (z.MepSystemType.IndexOf("chilled water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     GetNormalizedSystemType(z.MepSystemType).Equals("CHILLED", StringComparison.OrdinalIgnoreCase)));
                
                string prefix = settings.GetPrefixForElement(cwZone.MepElementCategory, cwZone.MepSystemType, cwZone.MepServiceType);
                NumberingDebugLogger.LogInfo($"[ResolveClusterPrefix] Cluster {cz.ClusterInstanceId}: 'CHILLED' theme detected. Using '{prefix}'.");
                return prefix;
            }

            // RULE #1: Homogeneous Service Type
            // "If all zones in cluster have same service type -> use user-defined service type prefix"
            string firstNormalized = GetNormalizedSystemType(zones.First().MepSystemType);
            bool allSameSystem = zones.All(z => string.Equals(GetNormalizedSystemType(z.MepSystemType), firstNormalized, StringComparison.OrdinalIgnoreCase));
            
            if (allSameSystem)
            {
                var first = zones.First();
                string prefix = settings.GetPrefixForElement(first.MepElementCategory, first.MepSystemType, first.MepServiceType);
                NumberingDebugLogger.LogInfo($"[ResolveClusterPrefix] Cluster {cz.ClusterInstanceId}: Rule #1 Homogeneous system '{first.MepSystemType}'. Using '{prefix}'.");
                return prefix;
            }

            // FALLBACK for Mixed Clusters (User Shouty Requirement): "OR IF NOT I EXPECT PLU THE DISCIPLINE PREFIX"
            // If we are here, it's mixed and not chilled.
            // Check if all are same category.
            string firstCat = zones.First().MepElementCategory;
            if (zones.All(z => string.Equals(z.MepElementCategory, firstCat, StringComparison.OrdinalIgnoreCase)))
            {
                string catPrefix = settings.GetDisciplinePrefix(firstCat);
                NumberingDebugLogger.LogInfo($"[ResolveClusterPrefix] Cluster {cz.ClusterInstanceId}: Mixed systems but Homogeneous Category '{firstCat}'. Fallback to Discipline Prefix '{catPrefix}'.");
                return catPrefix;
            }

            // Truly Mixed Category (e.g. Pipe + Duct) -> MEP
            NumberingDebugLogger.LogInfo($"[ResolveClusterPrefix] Cluster {cz.ClusterInstanceId}: Mixed Category Cluster (e.g. Pipe+Duct). Fallback to 'MEP'.");
            return "MEP";
        }

        private static string GetNormalizedSystemType(string? systemType)
        {
            if (string.IsNullOrEmpty(systemType)) return string.Empty;

            string input = systemType.Trim();

            // ✅ ROBUST CHILLED WATER THEME:
            // Normalize "CHILLED WATER SUPPLY", "CHW S", "CHWR", "CHRW", etc. all to a common "CHILLED" theme
            if (input.IndexOf("chille", StringComparison.OrdinalIgnoreCase) >= 0 ||
                input.StartsWith("CHW", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("CHRW", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("CHRSS", StringComparison.OrdinalIgnoreCase))
            {
                return "CHILLED";
            }
                
            // Handle Hot Water theme
            if (input.IndexOf("hot water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                input.StartsWith("HWS", StringComparison.OrdinalIgnoreCase) || 
                input.StartsWith("HWR", StringComparison.OrdinalIgnoreCase))
            {
                return "HW";
            }

            // Strip " Supply" or " Return" suffixes for other systems
            if (input.EndsWith(" Supply", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(0, input.Length - 7).Trim();
            else if (input.EndsWith(" Return", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(0, input.Length - 7).Trim();
            
            return input;
        }

        private static string? GetOverrideOnly(MarkPrefixSettings settings, string category, string? systemType, string? serviceType)
        {
            if (string.IsNullOrEmpty(systemType)) return null;

            if (category == "Ducts")
            {
                var match = settings.DuctSystemTypeOverrides.FirstOrDefault(kvp => string.Equals(kvp.Key, systemType, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Key)) return match.Value;
            }
            else if (category == "Pipes")
            {
                var match = settings.PipeSystemTypeOverrides.FirstOrDefault(kvp => string.Equals(kvp.Key, systemType, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Key)) return match.Value;
            }
            else if (category == "Duct Accessories")
            {
                var match = settings.DuctAccessoriesSystemTypeOverrides.FirstOrDefault(kvp => string.Equals(kvp.Key, systemType, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Key)) return match.Value;
            }
            else if (category == "Cable Trays" && !string.IsNullOrEmpty(serviceType))
            {
                var match = settings.CableTrayServiceTypeOverrides.FirstOrDefault(kvp => string.Equals(kvp.Key, serviceType, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Key)) return match.Value;
            }

            return null;
        }

        private static bool IsMultiLink(List<ClashZone> zones)
        {
            if (zones.Count <= 1) return false;
            string firstKey = zones.First().SourceDocKey;
            return zones.Any(z => !string.Equals(z.SourceDocKey, firstKey, StringComparison.Ordinal));
        }
    }
}
