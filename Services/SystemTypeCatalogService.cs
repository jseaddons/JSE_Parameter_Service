using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using JSE_Parameter_Service.Models;

namespace JSE_Parameter_Service.Services
{
    /// <summary>
    /// Represents a catalog of unique system and service types discovered from persisted clash data.
    /// </summary>
    public class SystemTypeCatalog
    {
        public HashSet<string> SystemTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ServiceTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsEmpty => SystemTypes.Count == 0 && ServiceTypes.Count == 0;
    }

    /// <summary>
    /// Loads unique System Type / Service Type values from stored filter XML (and future persistence sources).
    /// Used to drive UI dropdowns for System Type overrides.
    /// </summary>
    public class SystemTypeCatalogService
    {
        private readonly Action<string> _logger;

        public SystemTypeCatalogService(Action<string> logger = null)
        {
            _logger = logger ?? (_ => { });
        }

        public SystemTypeCatalog Load(Document document, string category = null)
        {
            var catalog = new SystemTypeCatalog();

            if (document == null)
            {
                _logger("[SystemTypeCatalog] No document context supplied – returning empty catalog.");
                return catalog;
            }

            try
            {
                // ✅ CRITICAL CHANGE: Only load from database (Skip Revit per user request)
                // "NO REVIT ONLY DB"
                LoadSystemTypesFromDatabase(document, catalog, category);
            }
            catch (Exception ex)
            {
                _logger($"[SystemTypeCatalog] Error loading system types: {ex.Message}");
                _logger($"[SystemTypeCatalog] Stack trace: {ex.StackTrace}");
            }

            return catalog;
        }

        /// <summary>
        /// ✅ SIMPLIFIED: Loads system/service types directly from Revit by querying placed sleeves
        /// Gets unique System Type (for Ducts/Pipes) or Service Type (for Cable Trays) from MEP elements
        /// ✅ NEW: Filters by category if specified - only returns system/service types for that category
        /// </summary>
        private void LoadSystemTypesFromRevit(Document document, SystemTypeCatalog catalog, string category = null)
        {
            try
            {
                // ✅ CRITICAL FIX: Ensure document is valid
                if (document == null || document.IsValidObject == false)
                {
                    _logger($"[SystemTypeCatalog] ⚠️ Invalid document - cannot load system types from Revit");
                    return;
                }
                
                // ✅ STEP 1: Get individual sleeve IDs from database (SleeveInstanceId > 0, NOT ClusterInstanceId)
                var individualSleeveIds = new HashSet<int>();
                try
                {
                    using (var context = new Data.SleeveDbContext(document))
                    {
                        using (var cmd = context.Connection.CreateCommand())
                        {
                            // ✅ CRITICAL: Only get individual sleeves (SleeveInstanceId > 0), exclude cluster sleeves
                            cmd.CommandText = @"
                                SELECT DISTINCT SleeveInstanceId 
                                FROM ClashZones 
                                WHERE SleeveInstanceId > 0
                                UNION
                                SELECT DISTINCT SleeveInstanceId 
                                FROM SleeveSnapshots 
                                WHERE SleeveInstanceId > 0";
                            
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    var sleeveId = Convert.ToInt32(reader["SleeveInstanceId"] ?? 0);
                                    if (sleeveId > 0)
                                    {
                                        individualSleeveIds.Add(sleeveId);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception dbEx)
                {
                    _logger($"[SystemTypeCatalog] ⚠️ Error querying database for individual sleeves: {dbEx.Message}");
                    // Fallback: process all sleeves if database query fails
                }
                
                // ✅ STEP 2: Collect only individual sleeves (those with IDs in our set)
                var allSleeves = new FilteredElementCollector(document)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi =>
                    {
                        var famName = fi.Symbol?.Family?.Name ?? string.Empty;
                        // Match only the 4 specific opening families
                        bool isOpeningFamily = famName.IndexOf("OpeningOnWall", StringComparison.OrdinalIgnoreCase) >= 0
                            || famName.IndexOf("OpeningOnSlab", StringComparison.OrdinalIgnoreCase) >= 0;
                        
                        if (!isOpeningFamily) return false;
                        
                        // ✅ CRITICAL: Only include if this sleeve ID is in our individual sleeves set
                        // If database query failed, include all (fallback)
                        if (individualSleeveIds.Count == 0) return true; // Fallback: include all
                        
                        return individualSleeveIds.Contains(fi.Id.IntegerValue);
                    })
                    .ToList();
                
                _logger($"[SystemTypeCatalog] Found {allSleeves.Count} individual sleeves (SleeveInstanceId > 0) in document");
                
                // ✅ STEP 3: Use HashSet for proper deduplication
                var uniqueSystemTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueServiceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                int processedSleeves = 0;
                int skippedNoMepId = 0;
                int skippedMepNotFound = 0;
                
                // ✅ STEP 4: For each individual sleeve, get its MEP element and extract system/service type
                foreach (var sleeve in allSleeves)
                {
                    try
                    {
                        // Get MEP_ElementId parameter from sleeve
                        var mepIdParam = sleeve.LookupParameter("MEP_ElementId");
                        if (mepIdParam == null || !mepIdParam.HasValue)
                        {
                            skippedNoMepId++;
                            continue;
                        }
                        
                        int mepElementId = mepIdParam.AsInteger();
                        if (mepElementId <= 0)
                        {
                            skippedNoMepId++;
                            continue;
                        }
                        
                        // Get MEP element from document (could be in linked file)
                        Element mepElement = null;
                        
                        // Try active document first
                        mepElement = document.GetElement(new ElementId(mepElementId));
                        
                        // If not found, try linked files
                        if (mepElement == null)
                        {
                            var linkInstances = new FilteredElementCollector(document)
                                .OfClass(typeof(RevitLinkInstance))
                                .Cast<RevitLinkInstance>();
                            
                            foreach (var linkInstance in linkInstances)
                            {
                                var linkDoc = linkInstance.GetLinkDocument();
                                if (linkDoc != null)
                                {
                                    mepElement = linkDoc.GetElement(new ElementId(mepElementId));
                                    if (mepElement != null) break;
                                }
                            }
                        }
                        
                        if (mepElement == null)
                        {
                            skippedMepNotFound++;
                            continue;
                        }
                        
                        // ✅ STEP 3: Determine category from MEP element
                        var mepCategory = mepElement.Category?.Name ?? string.Empty;
                        
                        // ✅ STEP 4: Filter by category if specified
                        if (!string.IsNullOrWhiteSpace(category))
                        {
                            // Map category names
                            var categoryMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                            {
                                { "Ducts", new[] { "Ducts", "Duct Fittings", "Duct Accessories" } },
                                { "Pipes", new[] { "Pipes", "Pipe Fittings", "Plumbing Fixtures" } },
                                { "Cable Trays", new[] { "Cable Trays", "Conduits" } },
                                { "Duct Accessories", new[] { "Duct Accessories" } }
                            };
                            
                            if (categoryMap.ContainsKey(category))
                            {
                                var allowedCategories = categoryMap[category];
                                if (!allowedCategories.Any(c => mepCategory.Equals(c, StringComparison.OrdinalIgnoreCase)))
                                {
                                    continue; // Skip this sleeve - category doesn't match
                                }
                            }
                        }
                        
                        // ✅ STEP 5: Extract System Type or Service Type based on category
                        // ✅ CRITICAL: Use HashSet for deduplication - only add unique values
                        if (mepCategory.IndexOf("Cable Tray", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            mepCategory.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // For Cable Trays/Conduits: Get Service Type
                            var serviceType = GetSystemTypeFromElement(mepElement, isServiceType: true);
                            if (!string.IsNullOrWhiteSpace(serviceType))
                            {
                                var trimmed = serviceType.Trim();
                                if (uniqueServiceTypes.Add(trimmed)) // Add returns true if new item added
                                {
                                    catalog.ServiceTypes.Add(trimmed);
                                }
                            }
                        }
                        else if (mepCategory.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 mepCategory.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // For Ducts/Pipes: Get System Type
                            var systemType = GetSystemTypeFromElement(mepElement, isServiceType: false);
                            if (!string.IsNullOrWhiteSpace(systemType))
                            {
                                var trimmed = systemType.Trim();
                                if (uniqueSystemTypes.Add(trimmed)) // Add returns true if new item added
                                {
                                    catalog.SystemTypes.Add(trimmed);
                                }
                            }
                        }
                        else
                        {
                            // Unknown category - try both
                            var systemType = GetSystemTypeFromElement(mepElement, isServiceType: false);
                            if (!string.IsNullOrWhiteSpace(systemType))
                            {
                                var trimmed = systemType.Trim();
                                if (uniqueSystemTypes.Add(trimmed))
                                {
                                    catalog.SystemTypes.Add(trimmed);
                                }
                            }
                            
                            var serviceType = GetSystemTypeFromElement(mepElement, isServiceType: true);
                            if (!string.IsNullOrWhiteSpace(serviceType))
                            {
                                var trimmed = serviceType.Trim();
                                if (uniqueServiceTypes.Add(trimmed))
                                {
                                    catalog.ServiceTypes.Add(trimmed);
                                }
                            }
                        }
                        
                        processedSleeves++;
                    }
                    catch (Exception ex)
                    {
                        _logger($"[SystemTypeCatalog] Error processing sleeve {sleeve.Id}: {ex.Message}");
                    }
                }
                
                // ✅ DIAGNOSTIC: Log statistics (show unique counts, not total extracted)
                _logger($"[SystemTypeCatalog] Revit query stats: {allSleeves.Count} individual sleeves found, {processedSleeves} processed, {skippedNoMepId} skipped (no MEP ID), {skippedMepNotFound} skipped (MEP not found), {uniqueSystemTypes.Count} UNIQUE System Types, {uniqueServiceTypes.Count} UNIQUE Service Types");
            }
            catch (Exception ex)
            {
                _logger($"[SystemTypeCatalog] Error loading from Revit: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logger($"[SystemTypeCatalog] Inner exception: {ex.InnerException.Message}");
                }
            }
        }
        
        /// <summary>
        /// ✅ OPTIMIZED: Loads system types directly from MepSystemType and MepServiceType columns in ClashZones.
        /// Ignores the "category" filter to ensure ALL system types are available as requested.
        /// "LOAD ALL ACTEGORY NOT ONLY DUCT FOR SYSTEM TYPE"
        /// </summary>
        private void LoadSystemTypesFromDatabase(Document document, SystemTypeCatalog catalog, string category = null)
        {
            try
            {
                if (document == null || document.IsValidObject == false)
                {
                    _logger($"[SystemTypeCatalog] ⚠️ Invalid document - cannot load system types from database");
                    return;
                }

                using (var context = new Data.SleeveDbContext(document, msg =>
                {
                    if (!DeploymentConfiguration.DeploymentMode) _logger($"[SystemTypeCatalog][SQLite] {msg}");
                }))
                {
                    if (context.Connection == null)
                    {
                        _logger($"[SystemTypeCatalog] ⚠️ Database connection is null");
                        return;
                    }

                    // ✅ STEP 1: Load UNIQUE System Types from MepSystemType column (All Categories)
                    using (var cmd = context.Connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT DISTINCT MepSystemType 
                            FROM ClashZones 
                            WHERE MepSystemType IS NOT NULL 
                              AND MepSystemType != ''";

                        int systemCount = 0;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var val = reader[0]?.ToString()?.Trim();
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    if (catalog.SystemTypes.Add(val))
                                    {
                                        systemCount++;
                                    }
                                }
                            }
                        }
                        _logger($"[SystemTypeCatalog] Found {systemCount} unique System Types in database (All Categories)");
                    }

                    // ✅ STEP 2: Load UNIQUE Service Types from MepServiceType column (All Categories)
                    using (var cmd = context.Connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT DISTINCT MepServiceType 
                            FROM ClashZones 
                            WHERE MepServiceType IS NOT NULL 
                              AND MepServiceType != ''";

                        int serviceCount = 0;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var val = reader[0]?.ToString()?.Trim();
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    if (catalog.ServiceTypes.Add(val))
                                    {
                                        serviceCount++;
                                    }
                                }
                            }
                        }
                        _logger($"[SystemTypeCatalog] Found {serviceCount} unique Service Types in database (All Categories)");
                    }

                    // ✅ STEP 3: Fallback - Extract from MepElementSystemAbbreviation if needed
                    using (var cmd = context.Connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT DISTINCT MepElementSystemAbbreviation 
                            FROM ClashZones 
                            WHERE MepElementSystemAbbreviation IS NOT NULL 
                              AND MepElementSystemAbbreviation != ''";

                        int abbrCount = 0;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var val = reader[0]?.ToString()?.Trim();
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    if (catalog.SystemTypes.Add(val))
                                    {
                                        abbrCount++;
                                    }
                                }
                            }
                        }
                        if (abbrCount > 0)
                            _logger($"[SystemTypeCatalog] Added {abbrCount} unique System Types from Abbreviations");
                    }
                }

                _logger($"[SystemTypeCatalog] ✅ Final catalog: {catalog.SystemTypes.Count} System Types, {catalog.ServiceTypes.Count} Service Types");
            }
            catch (Exception ex)
            {
                _logger($"[SystemTypeCatalog] ❌ Error in LoadSystemTypesFromDatabase: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get System Type or Service Type parameter value from a MEP element
        /// </summary>
        private static string GetSystemTypeFromElement(Element element, bool isServiceType = false)
        {
            if (element == null) return null;
            
            try
            {
                if (isServiceType)
                {
                    // For Cable Trays: Get Service Type
                    var param = element.LookupParameter("Service Type") 
                             ?? element.LookupParameter("MEP Service Type");
                    
                    if (param != null && param.HasValue)
                    {
                        return param.AsString() ?? param.AsValueString();
                    }
                }
                else
                {
                    // For Ducts/Pipes: Get System Type
                    // Try built-in parameters first (faster and more reliable)
                    var builtInParam = element.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)
                                     ?? element.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)
                                     ?? element.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                    
                    if (builtInParam != null && builtInParam.HasValue)
                    {
                        // System Type is stored as ElementId - resolve to element name
                        if (builtInParam.StorageType == StorageType.ElementId)
                        {
                            var systemTypeId = builtInParam.AsElementId();
                            if (systemTypeId != null && systemTypeId != ElementId.InvalidElementId)
                            {
                                var systemTypeElement = element.Document.GetElement(systemTypeId);
                                if (systemTypeElement != null)
                                {
                                    return systemTypeElement.Name ?? systemTypeElement.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM)?.AsString();
                                }
                            }
                        }
                        else
                        {
                            return builtInParam.AsString() ?? builtInParam.AsValueString();
                        }
                    }
                    
                    // Fallback to instance parameter
                    var instanceParam = element.LookupParameter("System Type")
                                     ?? element.LookupParameter("MEP System Type")
                                     ?? element.LookupParameter("System Classification");
                    
                    if (instanceParam != null && instanceParam.HasValue)
                    {
                        return instanceParam.AsString() ?? instanceParam.AsValueString();
                    }
                }
            }
            catch
            {
                // Ignore errors - return null
            }
            
            return null;
        }

        /// <summary>
        /// Helper to get parameter value from dictionary with multiple key options
        /// ✅ CRITICAL: Uses case-insensitive matching to handle parameter name variations
        /// </summary>
        private static string GetParameterValue(Dictionary<string, string> dict, params string[] keys)
        {
            if (dict == null || keys == null || dict.Count == 0)
                return null;

            // First try exact match (fast path)
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (dict.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            // If exact match fails, try case-insensitive match
            // This handles variations like "Service Type" vs "SERVICE TYPE" vs "service type"
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var match = dict.FirstOrDefault(kv => 
                    kv.Key != null && 
                    kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(kv.Value));
                
                if (match.Key != null)
                {
                    return match.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Deserialize JSON dictionary using System.Text.Json (same as ClashZoneRepository)
        /// </summary>
        private static Dictionary<string, string> DeserializeDictionary(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
                return new Dictionary<string, string>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) 
                    ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private static string ExtractParameterValue(ClashZone zone, params string[] keys)
        {
            if (zone?.MepParameterValues == null || zone.MepParameterValues.Count == 0 || keys == null)
                return null;

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var match = zone.MepParameterValues
                    .FirstOrDefault(kv => kv != null &&
                                           kv.Key != null &&
                                           kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

                if (match != null && !string.IsNullOrWhiteSpace(match.Value))
                {
                    return match.Value;
                }
            }

            return null;
        }
    }
}


