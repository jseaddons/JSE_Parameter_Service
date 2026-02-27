using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.Revit.DB;
using JSE_Parameter_Service.Models;
using JSE_Parameter_Service.Data;
using JSE_Parameter_Service.Data.Repositories;
using JSE_Parameter_Service.Services.Strategies;
using JSE_Parameter_Service.Services.Helpers;
using JSE_Parameter_Service.Services.Logging;
using Autodesk.Revit.UI;

namespace JSE_Parameter_Service.Services
{
    /// <summary>
    /// Service for applying MEPMARK parameters to cluster sleeves.
    /// Handles sequential numbering and prefix resolution.
    /// </summary>
    public partial class MarkParameterService
    {
        private readonly IMarkCacheService _cache;
        private readonly ICombinedSleeveMarkService _combinedService;
        private readonly Action<string> _logger;

        public MarkParameterService(Document doc = null, Action<string> logger = null)
        {
            _logger = logger ?? ((msg) => { });
            _cache = new MarkCacheService();
            _combinedService = new CombinedSleeveMarkService();
            
            if (doc != null)
            {
                using var context = new SleeveDbContext(doc);
                _cache.Initialize(doc, context);
            }
        }

        #region Public API (Standard & Batch)

        public (int processedCount, int errorCount) ApplyMarksFromDatabase(Document doc, MarkPrefixSettings settings, string? category = null)
        {
            try
            {
                var activeView = doc.ActiveView;
                if (!(activeView is ViewPlan plan)) throw new InvalidOperationException("Active view must be a floor plan");
                var level = plan.GenLevel;
                if (level == null) throw new InvalidOperationException("Floor plan must have an associated level");

                return ApplyMarksFromDatabase(doc, level.Name, category, settings.StartNumber, settings.NumberFormat, settings);
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"[MarkParameterService] ApplyMarksFromDatabase wrapper error: {ex.Message}");
                return (0, 1);
            }
        }

        public (int processedCount, int errorCount) ApplyMarksFromDatabase(Document doc, string levelName, string category, int startNumber, string numberFormat, MarkPrefixSettings? markPrefixes = null)
        {
            int processedCount = 0;
            int errorCount = 0;
            try
            {
                using var context = new SleeveDbContext(doc);
                // âœ… CRITICAL FIX: Initialize cache with LIVE context
                _cache.Initialize(doc, context);

                var markRepo = new MarkDataRepository(context);
                var prefixStrategy = new DisciplinePrefixStrategy();
                var geoHelper = new ViewGeometryHelper();

                var zones = markRepo.GetSleevesForLevel(levelName, category);
                if (zones.Count == 0) return (0, 0);

                Outline worldBounds = geoHelper.GetWorldViewBounds(doc.ActiveView as ViewPlan);
                var validZones = zones.Where(z => geoHelper.IsPointInView(new XYZ(z.IntersectionPointX, z.IntersectionPointY, z.IntersectionPointZ), worldBounds)).ToList();

                if (validZones.Count == 0) return (0, 0);

                var elementGroups = validZones
                    .GroupBy(z => z.CombinedClusterSleeveInstanceId > 0 ? z.CombinedClusterSleeveInstanceId : (z.ClusterInstanceId > 0 ? z.ClusterInstanceId : z.SleeveInstanceId))
                    .OrderBy(g => g.Key)
                    .ToList();

                int clusterGroupCount = elementGroups.Count(g => g.Any(z => z.ClusterInstanceId > 0 || z.CombinedClusterSleeveInstanceId > 0));
                int individualCount = elementGroups.Count - clusterGroupCount;
                RemarkDebugLogger.LogInfo($"[MarkParameterService] Groups: {elementGroups.Count} total ({clusterGroupCount} clusters/combined, {individualCount} individuals)");

                var prefixGroups = new Dictionary<string, List<ElementId>>();
                foreach (var group in elementGroups)
                {
                    int instanceId = group.Key;
                    var zonesInGroup = group.ToList();
                    var primaryZone = zonesInGroup.First();

                    // Centralized Prefix Resolution (Advanced Fixes behind flag)
                    string prefix = ResolvePrefix(category, markPrefixes ?? new MarkPrefixSettings(), primaryZone, zonesInGroup);
                    
                    if (!prefixGroups.ContainsKey(prefix)) prefixGroups[prefix] = new List<ElementId>();
                    prefixGroups[prefix].Add(new ElementId(instanceId));
                }

                var updates = new List<(ElementId ElementId, string Value)>();
                foreach (var group in prefixGroups)
                {
                    string prefix = group.Key;
                    var elements = group.Value;
                    int currentNumber = startNumber;
                    if (category != null && category.Equals("Combined", StringComparison.OrdinalIgnoreCase)) currentNumber = 1;

                    foreach (var elId in elements)
                    {
                        string markValue = $"{prefix}{currentNumber.ToString(numberFormat)}";
                        updates.Add((elId, markValue));
                        currentNumber++;
                    }
                }

                // Definition caching: resolve "MEP Mark" / "Mark" once, use get_Parameter(Definition) in loop
                if (OptimizationFlags.UseDefinitionCachingForBatchWrites && updates.Count > 0)
                {
                    var firstEl = doc.GetElement(updates[0].ElementId);
                    var (mepMarkDef, markDef) = CacheMarkDefinitions(firstEl);

                    foreach (var update in updates)
                    {
                        try
                        {
                            var el = doc.GetElement(update.ElementId);
                            var p = GetMarkParameter(el, mepMarkDef, markDef);
                            if (p != null && !p.IsReadOnly) { p.Set(update.Value); processedCount++; }
                        }
                        catch { errorCount++; }
                    }
                }
                else
                {
                    foreach (var update in updates)
                    {
                        try
                        {
                            var el = doc.GetElement(update.ElementId);
                            var p = el?.LookupParameter("MEP Mark") ?? el?.LookupParameter("Mark");
                            if (p != null && !p.IsReadOnly) { p.Set(update.Value); processedCount++; }
                        }
                        catch { errorCount++; }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"[MarkParameterService] ApplyMarksFromDatabase error: {ex.Message}");
            }
            return (processedCount, errorCount);
        }

        public (int processedCount, int errorCount) ApplyPrefixesOnly(Document doc, string category, string projectPrefix, string disciplinePrefix, bool remarkAll, MarkPrefixSettings? markPrefixes = null)
        {
            int processedCount = 0;
            int errorCount = 0;
            try
            {
                using var context = new SleeveDbContext(doc);
                // âœ… CRITICAL FIX: Initialize cache with the LIVE context for this scope
                // The constructor context was disposed, causing ObjectDisposedException
                _cache.Initialize(doc, context);

                var markRepo = new MarkDataRepository(context);
                var prefixStrategy = new DisciplinePrefixStrategy();

                var zones = markRepo.GetMarkableClashZones(category);
                NumberingDebugLogger.LogInfo($"Found {zones.Count} markable zones for category '{category}' in DB");
                if (zones.Count == 0) return (0, 0);

                var updates = new List<(ElementId Id, string Value)>();
                var settings = markPrefixes ?? new MarkPrefixSettings();

                RemarkDebugLogger.LogInfo($"[PREFIX-ONLY] Total zones fetched from DB: {zones.Count}");

                // Group zones by target ID (Combined > Cluster > Individual) to avoid redundant Revit updates
                var groupedZones = zones
                    .GroupBy(z => z.CombinedClusterSleeveInstanceId > 0 ? z.CombinedClusterSleeveInstanceId : (z.ClusterInstanceId > 0 ? z.ClusterInstanceId : z.SleeveInstanceId))
                    .Where(g => g.Key > 0)
                    .ToList();

                int clusterGroups = groupedZones.Count(g => g.Any(z => z.ClusterInstanceId > 0 || z.CombinedClusterSleeveInstanceId > 0));
                int individualGroups = groupedZones.Count - clusterGroups;
                RemarkDebugLogger.LogInfo($"[PREFIX-ONLY] Unique elements to update: {groupedZones.Count} ({clusterGroups} clusters/combined, {individualGroups} individuals)");

                // Cache definitions for "MEP Mark" / "Mark" once for both read and write loops
                Definition mepMarkDef = null, markDef = null;
                bool useDefCache = OptimizationFlags.UseDefinitionCachingForBatchWrites && groupedZones.Count > 0;
                if (useDefCache)
                {
                    var firstGroupId = groupedZones[0].Key;
                    var firstEl = doc.GetElement(new ElementId(firstGroupId));
                    if (firstEl != null)
                        (mepMarkDef, markDef) = CacheMarkDefinitions(firstEl);
                }

                foreach (var group in groupedZones)
                {
                    int targetId = group.Key;
                    var primaryZone = group.First();
                    var zonesInGroup = group.ToList();

                    var el = doc.GetElement(new ElementId(targetId));
                    if (el == null) continue;

                    Parameter p = useDefCache ? GetMarkParameter(el, mepMarkDef, markDef) : (el.LookupParameter("MEP Mark") ?? el.LookupParameter("Mark"));
                    string existingMark = p?.AsString() ?? "";

                    // Centralized Prefix Resolution (Advanced Fixes behind flag)
                    // Pass the list of zones in group so ResolvePrefix knows it's a cluster/combined context
                    string elementPrefix = ResolvePrefix(category, settings, primaryZone, zonesInGroup);

                    // NUMBER PRESERVATION: Extract existing number (if any)
                    string existingNumber = ExtractExistingNumber(existingMark);

                    string targetMark = $"{projectPrefix}{elementPrefix}{existingNumber}";

                    // SMART SKIP: Only skip if the mark is identical
                    if (string.Equals(targetMark, existingMark, StringComparison.Ordinal))
                    {
                        if (!remarkAll) continue;
                    }

                    RemarkDebugLogger.LogInfo($"[PREFIX-ONLY] ID {targetId}: Target '{targetMark}' (Existing: '{existingMark}')");
                    updates.Add((el.Id, targetMark));
                }

                // Sort updates by mark value to ensure sequential application (though dictionary iteration order is not guaranteed, the loop above was sequential)
                updates = updates.OrderBy(u => u.Value).ToList();

                NumberingDebugLogger.LogStep($"[DEBUG] Total updates to apply: {updates.Count}");
                foreach (var up in updates.Take(50)) // Log first 50 to see enough examples
                {
                    NumberingDebugLogger.LogInfo($"[DEBUG] Update: ElementId={up.Id.GetIdInt()}, Value='{up.Value}'");
                }

                NumberingDebugLogger.LogStep($"Applying {updates.Count} prefix updates to Revit...");
                foreach (var update in updates)
                {
                    try
                    {
                        var el = doc.GetElement(update.Id);
                        Parameter p2 = useDefCache ? GetMarkParameter(el, mepMarkDef, markDef) : (el?.LookupParameter("MEP Mark") ?? el?.LookupParameter("Mark"));
                        if (p2 != null && !p2.IsReadOnly)
                        {
                            p2.Set(update.Value);
                            processedCount++;
                        }
                        else
                        {
                            NumberingDebugLogger.LogStep($"FAILED to update element {update.Id.GetIdInt()}: Parameter NULL or ReadOnly");
                        }
                    }
                    catch (Exception ex)
                    {
                        RemarkDebugLogger.LogError($"Error updating element {update.Id.GetIdInt()}", ex);
                        errorCount++;
                    }
                }
                RemarkDebugLogger.LogStep($"Finished applying prefixes. Processed: {processedCount}, Errors: {errorCount}");
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"[MarkParameterService] Prefix error: {ex.Message}");
            }
            return (processedCount, errorCount);
        }

        public (int processedCount, int errorCount) ApplyNumbersBatch(Document doc, string numberFormat, HashSet<string> allowedPrefixes = null, MarkPrefixSettings? markPrefixes = null)
        {
            int processedCount = 0;
            int errorCount = 0;
            try
            {
                FilteredElementCollector collector = (markPrefixes?.ActiveViewOnly == true && doc.ActiveView != null) 
                    ? new FilteredElementCollector(doc, doc.ActiveView.Id) 
                    : new FilteredElementCollector(doc);

                var allSleeves = collector.OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(fi => {
                        var famName = fi.Symbol?.Family?.Name ?? "";
                        return famName.Contains("OpeningOnWall") || famName.Contains("OpeningOnSlab");
                    }).ToList();

                if (allSleeves.Count == 0) return (0, 0);

                // Cache definitions for "MEP Mark" / "Mark" once for read and write loops
                Definition numMepMarkDef = null, numMarkDef = null;
                bool useNumDefCache = OptimizationFlags.UseDefinitionCachingForBatchWrites && allSleeves.Count > 0;
                if (useNumDefCache)
                    (numMepMarkDef, numMarkDef) = CacheMarkDefinitions(allSleeves[0]);

                var prefixGroups = new Dictionary<string, List<FamilyInstance>>();
                foreach (var sleeve in allSleeves)
                {
                    Parameter p = useNumDefCache ? GetMarkParameter(sleeve, numMepMarkDef, numMarkDef) : (sleeve.LookupParameter("MEP Mark") ?? sleeve.LookupParameter("Mark"));
                    string currentMark = p?.AsString() ?? "";
                    if (string.IsNullOrWhiteSpace(currentMark))
                    {
                        NumberingDebugLogger.LogInfo($"[NUMBERING SKIPPED] Element {sleeve.Id.GetIdInt()} has empty/null MEP Mark.");
                        continue;
                    }

                    string prefix = ExtractPrefix(currentMark);

                    if (allowedPrefixes != null && !allowedPrefixes.Any(ap => prefix.StartsWith(ap)))
                    {
                        NumberingDebugLogger.LogInfo($"[NUMBERING SKIPPED] Element {sleeve.Id.GetIdInt()} Prefix '{prefix}' NOT in allowed list: {string.Join(", ", allowedPrefixes)}");
                        continue;
                    }

                    if (!prefixGroups.ContainsKey(prefix)) prefixGroups[prefix] = new List<FamilyInstance>();
                    prefixGroups[prefix].Add(sleeve);
                }

                using var context = new SleeveDbContext(doc);
                var markerRepo = new CategoryProcessingMarkerRepository(context);
                var updates = new List<(ElementId Id, string Value)>();

                // âœ… PER-VIEW SCOPING: Determine the scope for numbering memory
                string targetScope = ""; // Where we save progress (Active View)
                string sourceScope = ""; // Where we read start number from (Selected View or Active View)

                if (markPrefixes?.ActiveViewOnly == true && doc.ActiveView != null)
                {
                    // Target is ALWAYS the Active View/Level
                    if (doc.ActiveView is ViewPlan vp && vp.GenLevel != null)
                        targetScope = vp.GenLevel.Name;
                    else
                        targetScope = doc.ActiveView.Name;

                    // Source defaults to Target, unless "Continue" is selected
                    if (markPrefixes.UseContinueNumbering && !string.IsNullOrEmpty(markPrefixes.ContinueFromViewName))
                    {
                        sourceScope = markPrefixes.ContinueFromViewName;
                        NumberingDebugLogger.LogInfo($"[NUMBERING SCOPE] CONTINUING from Source: '{sourceScope}' -> Target: '{targetScope}'");
                    }
                    else
                    {
                        sourceScope = targetScope;
                        NumberingDebugLogger.LogInfo($"[NUMBERING SCOPE] Using View-Specific memory for: '{targetScope}'");
                    }
                }
                else
                {
                    NumberingDebugLogger.LogInfo("[NUMBERING SCOPE] Using Global (Project-wide) memory.");
                }

                // Collect deferred DB marker updates (write AFTER Revit params, not during prefix loop)
                var deferredMarkerUpdates = new List<(string targetKey, int lastNum, string sleeveIds)>();

                foreach (var group in prefixGroups)
                {
                    string prefix = group.Key;
                    var items = group.Value.OrderBy(i => i.Id.GetIdInt()).ToList();

                    // SCOPED KEYS: Use source for reading, target for writing
                    string sourceKey = string.IsNullOrEmpty(sourceScope) ? prefix : $"{prefix}|{sourceScope}";
                    string targetKey = string.IsNullOrEmpty(targetScope) ? prefix : $"{prefix}|{targetScope}";

                    int startNum = 1;
                    if (markPrefixes != null && markPrefixes.StartNumber > 0)
                    {
                         // Respect UI Start Number if set (forces a reset for this batch)
                         startNum = markPrefixes.StartNumber;
                         NumberingDebugLogger.LogInfo($"[NUMBERING] Prefix '{prefix}' - FORCING Start Number {startNum} (UI Override)");
                    }
                    else
                    {
                         // Fallback to Scoped History
                         var (lastNum, _) = markerRepo.GetMarker(sourceKey);
                         startNum = lastNum + 1;
                         NumberingDebugLogger.LogInfo($"[NUMBERING] Prefix '{prefix}' - Starting from '{sourceKey}' history: {startNum}");
                    }

                    foreach (var item in items)
                    {
                        string markValue = $"{prefix}{startNum.ToString(markPrefixes?.NumberFormat ?? "000")}";
                        updates.Add((item.Id, markValue));
                        startNum++;
                    }

                    // Defer DB marker write (collected, flushed after Revit writes)
                    deferredMarkerUpdates.Add((targetKey, startNum - 1, string.Join(",", items.Select(i => i.Id.GetIdInt()))));
                }

                // PRIORITY: Revit parameter writes first (main thread work)
                foreach (var up in updates)
                {
                    try
                    {
                        var el = doc.GetElement(up.Id);
                        Parameter p = useNumDefCache ? GetMarkParameter(el, numMepMarkDef, numMarkDef) : (el?.LookupParameter("MEP Mark") ?? el?.LookupParameter("Mark"));
                        if (p != null && !p.IsReadOnly)
                        {
                            p.Set(up.Value);
                            processedCount++;
                        }
                    }
                    catch { errorCount++; }
                }

                // DEFERRED: Flush marker updates to DB (after Revit writes complete)
                foreach (var marker in deferredMarkerUpdates)
                {
                    markerRepo.UpdateMarker(marker.targetKey, marker.lastNum, marker.sleeveIds);
                }
            }
            catch (Exception ex) { DebugLogger.Error($"[MarkParameterService] Batch numbering error: {ex.Message}"); }
            return (processedCount, errorCount);
        }

        public (int processedCount, int errorCount) ApplyNumbersOnly(Document doc, string category, string numberFormat, MarkPrefixSettings? markPrefixes = null)
        {
            // Simplified version for de-bloat, preserving category logic
            return ApplyNumbersBatch(doc, numberFormat, null, markPrefixes);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Resolves the "MEP Mark" or "Mark" parameter using cached Definition objects.
        /// get_Parameter(Definition) is ~10x faster than LookupParameter(string).
        /// Returns null if neither parameter exists on the element.
        /// </summary>
        private static Parameter GetMarkParameter(Element el, Definition mepMarkDef, Definition markDef)
        {
            if (el == null) return null;
            Parameter p = null;
            if (mepMarkDef != null) p = el.get_Parameter(mepMarkDef);
            if (p == null && markDef != null) p = el.get_Parameter(markDef);
            return p;
        }

        /// <summary>
        /// Caches Definition objects for "MEP Mark" and "Mark" from a representative element.
        /// Call once before a write loop, then use GetMarkParameter() in the loop.
        /// </summary>
        private static (Definition mepMarkDef, Definition markDef) CacheMarkDefinitions(Element representativeElement)
        {
            if (representativeElement == null) return (null, null);
            var mepMark = representativeElement.LookupParameter("MEP Mark");
            var mark = representativeElement.LookupParameter("Mark");
            return (mepMark?.Definition, mark?.Definition);
        }

        private string ExtractPrefix(string mark)
        {
            string prefix = mark.Trim();
            int lastNonDigit = -1;
            for (int i = prefix.Length - 1; i >= 0; i--) { if (!char.IsDigit(prefix[i])) { lastNonDigit = i; break; } }
            return (lastNonDigit >= 0 && lastNonDigit < prefix.Length - 1) ? prefix.Substring(0, lastNonDigit + 1) : prefix;
        }

        private string ExtractExistingNumber(string mark)
        {
            if (string.IsNullOrWhiteSpace(mark)) return "";
            
            // Find continuous digits at the end of the string
            int lastNonDigit = -1;
            for (int i = mark.Length - 1; i >= 0; i--)
            {
                if (!char.IsDigit(mark[i]))
                {
                    lastNonDigit = i;
                    break;
                }
            }
            
            if (lastNonDigit < mark.Length - 1)
            {
                return mark.Substring(lastNonDigit + 1);
            }
            
            return "";
        }

        public int ResetMarksForLevel(Document doc, string levelName, BoundingBoxXYZ viewExtent = null)
        {
            int clearedCount = 0;
            try
            {
                FilteredElementCollector collector = (viewExtent != null && doc.ActiveView != null) 
                    ? new FilteredElementCollector(doc, doc.ActiveView.Id) 
                    : new FilteredElementCollector(doc);

                var sleeves = collector.OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol.Family.Name.Contains("OpeningOnWall") || fi.Symbol.Family.Name.Contains("OpeningOnSlab"))
                    .ToList();

                // Cache definitions for "MEP Mark" / "Mark" once
                Definition resetMepMarkDef = null, resetMarkDef = null;
                bool useResetDefCache = OptimizationFlags.UseDefinitionCachingForBatchWrites && sleeves.Count > 0;
                if (useResetDefCache)
                    (resetMepMarkDef, resetMarkDef) = CacheMarkDefinitions(sleeves[0]);

                using var t = new Transaction(doc, "Reset Marks for Level");
                t.Start();
                foreach (var sleeve in sleeves)
                {
                    Parameter p = useResetDefCache ? GetMarkParameter(sleeve, resetMepMarkDef, resetMarkDef) : (sleeve.LookupParameter("MEP Mark") ?? sleeve.LookupParameter("Mark"));
                    if (p != null && !p.IsReadOnly && !string.IsNullOrEmpty(p.AsString()))
                    {
                        p.Set("");
                        clearedCount++;
                    }
                }
                t.Commit();
            }
            catch (Exception ex) { DebugLogger.Error($"[MarkParameterService] ResetMarksForLevel error: {ex.Message}"); }
            return clearedCount;
        }

        public int ResetMarksForSelection(Document doc, ICollection<ElementId> selectedIds)
        {
            int clearedCount = 0;
            try
            {
                // Cache definitions for "MEP Mark" / "Mark" once
                Definition selMepMarkDef = null, selMarkDef = null;
                bool useSelDefCache = OptimizationFlags.UseDefinitionCachingForBatchWrites && selectedIds.Count > 0;
                if (useSelDefCache)
                {
                    var firstEl = doc.GetElement(selectedIds.First());
                    if (firstEl != null)
                        (selMepMarkDef, selMarkDef) = CacheMarkDefinitions(firstEl);
                }

                using var t = new Transaction(doc, "Reset Marks for Selection");
                t.Start();
                foreach (var id in selectedIds)
                {
                    var el = doc.GetElement(id);
                    Parameter p = useSelDefCache ? GetMarkParameter(el, selMepMarkDef, selMarkDef) : (el?.LookupParameter("MEP Mark") ?? el?.LookupParameter("Mark"));
                    if (p != null && !p.IsReadOnly && !string.IsNullOrEmpty(p.AsString()))
                    {
                        p.Set("");
                        clearedCount++;
                    }
                }
                t.Commit();
            }
            catch (Exception ex) { DebugLogger.Error($"[MarkParameterService] ResetMarksForSelection error: {ex.Message}"); }
            return clearedCount;
        }

        public void ResetCategoryCounters(Document doc, string levelName = null)
        {
            try
            {
                using var context = new SleeveDbContext(doc);
                var repo = new CategoryProcessingMarkerRepository(context);
                
                if (string.IsNullOrEmpty(levelName))
                {
                    repo.ResetAllMarkers();
                }
                else
                {
                    repo.ResetMarkersForLevel(levelName);
                }
            }
            catch (Exception ex) { DebugLogger.Error($"[MarkParameterService] ResetCategoryCounters error: {ex.Message}"); }
        }

        /// <summary>
        /// âœ… HELPER: Centralized prefix resolution with support for advanced fixes.
        /// If flags are enabled, uses MarkPrefixHelper for clusters/combined/multi-link rules.
        /// Otherwise falls back to legacy/stable behavior.
        /// </summary>
        private string ResolvePrefix(string category, MarkPrefixSettings settings, ClashZone zone, List<ClashZone>? zonesInGroup = null)
        {
            NumberingDebugLogger.LogInfo($"[ResolvePrefix] ID: {zone.ClashZoneId}, Category: {category}, OptFlag: {OptimizationFlags.EnableAdvancedMarkingOptimization}, SettingsFlag: {settings.UseAdvancedPrefixResolution}, IsClusterInfo: (ID={zone.ClusterInstanceId}, Flag={zone.IsClusterResolved})");
            // 1. ADVANCED PATH (Optimized logic for clusters, combined, and multi-link)
            // Guarded by BOTH Global and Session-level flags for maximum safety.
            if (OptimizationFlags.EnableAdvancedMarkingOptimization && settings.UseAdvancedPrefixResolution)
            {
                var advanced = MarkPrefixHelper.ResolveAdvancedPrefix(zone, settings, _cache);
                if (advanced != null)
                {
                    NumberingDebugLogger.LogInfo($"[MarkParameterService] {zone.ClashZoneId}: Using ADVANCED prefix '{advanced}'");
                    return advanced;
                }
            }

            // 2. LEGACY PATH (The "working code" we must not affect)
            var prefixStrategy = new DisciplinePrefixStrategy();
            
            // Cluster logic: Use strategy to allow system type overrides (homogeneous)
            if (zonesInGroup != null && zonesInGroup.Count > 1)
            {
                string prefix = prefixStrategy.ResolvePrefix(category, settings, zone);
                NumberingDebugLogger.LogInfo($"[MarkParameterService] Cluster {zone.ClusterInstanceId}: Resolved prefix '{prefix}' via strategy");
                return prefix;
            }
            
            // Individual logic (Legacy)
            string legacyPrefix = prefixStrategy.ResolvePrefix(category, settings, zone);
            NumberingDebugLogger.LogStep($"[MarkParameterService] Individual {zone.SleeveInstanceId}: Using legacy resolved prefix '{legacyPrefix}'");
            return legacyPrefix;
        }

        #endregion
    }
}

