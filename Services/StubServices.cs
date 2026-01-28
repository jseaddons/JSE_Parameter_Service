using System;
using Autodesk.Revit.DB;
using JSE_Parameter_Service.Data;
using JSE_Parameter_Service.Models;
using JSE_Parameter_Service.Services.Logging; // For NumberingDebugLogger

namespace JSE_Parameter_Service.Services
{
    public class ServiceTypeAbbreviationService
    {
        public string GetAbbreviatedServiceTypeFromElement(Element element) { return "STUB"; }
        public string GetAbbreviation(string serviceType) { return "STUB"; }
        public string GetAbbreviation(string val, string paramName) { return "STUB"; }
    }


    public interface ICombinedSleeveMarkService
    {
        System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> GetAllCombinedSleeves(JSE_Parameter_Service.Data.Repositories.ClashZoneRepository repo);
        void CalculateCombinedSleevePrefixes(System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> sleeves, JSE_Parameter_Service.Models.MarkPrefixSettings settings);
        (int SuccessCount, int FailCount) ApplyCombinedSleeveMarksBatch(Document doc, System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> sleeves);
        System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> GetAllCombinedSleeves(Document doc);
        System.Collections.Generic.Dictionary<int, string> CalculateCombinedSleevePrefixes(Document doc, System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> sleeves, string projectPrefix, bool remarkAll);
        (int SuccessCount, int FailCount) ApplyCombinedSleeveMarksBatch(Document doc, System.Collections.Generic.Dictionary<int, string> assignments);
    }

    public class CombinedSleeveMarkService : ICombinedSleeveMarkService
    {
        public System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> GetAllCombinedSleeves(JSE_Parameter_Service.Data.Repositories.ClashZoneRepository repo) 
        { 
            return new System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone>(); 
        }

        public System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> GetAllCombinedSleeves(Document doc) 
        { 
            return new System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone>(); 
        }

        public void CalculateCombinedSleevePrefixes(System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> sleeves, JSE_Parameter_Service.Models.MarkPrefixSettings settings) 
        { 
        }

        public System.Collections.Generic.Dictionary<int, string> CalculateCombinedSleevePrefixes(Document doc, System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> sleeves, string projectPrefix, bool remarkAll) 
        { 
            return new System.Collections.Generic.Dictionary<int, string>();
        }

        public (int SuccessCount, int FailCount) ApplyCombinedSleeveMarksBatch(Document doc, System.Collections.Generic.List<JSE_Parameter_Service.Models.ClashZone> sleeves) 
        { 
            return (0, 0); 
        }

        public (int SuccessCount, int FailCount) ApplyCombinedSleeveMarksBatch(Document doc, System.Collections.Generic.Dictionary<int, string> assignments) 
        { 
            return (0, 0); 
        }
    }

    public class ParameterSnapshotService
    {
        public void CaptureSnapshots(Document doc, System.Collections.Generic.IEnumerable<Element> elements) { }
        public static void AddLearnedKey(string category, string paramName) { }
        public static void AddLearnedKey(string paramName) { }
    }

    public class ElementRetrievalService
    {
        public System.Collections.Generic.List<Element> GetElementsForTransfer(Document doc, JSE_Parameter_Service.Models.ParameterTransferConfiguration config) { return new System.Collections.Generic.List<Element>(); }
        public static Element GetElementFromDocumentOrLinked(Document doc, ElementId id, string linkTitle) { return null; }
        public static Element GetElementFromDocumentOrLinked(Document doc, ElementId id) { return null; }
    }

    public class LinkedFileService
    {
        public System.Collections.Generic.List<JSE_Parameter_Service.Models.LinkedFileInfo> GetLinkedFiles(Document doc) { return new System.Collections.Generic.List<JSE_Parameter_Service.Models.LinkedFileInfo>(); }
        public System.Collections.Generic.List<JSE_Parameter_Service.Models.LinkedFileInfo> GetHostElementFiles(Document doc) { return new System.Collections.Generic.List<JSE_Parameter_Service.Models.LinkedFileInfo>(); }
    }

    public class ParameterExtractionService
    {
        public System.Collections.Generic.Dictionary<string, object> GetCurrentOpeningParameters(Element e) { return new System.Collections.Generic.Dictionary<string, object>(); }
        public System.Collections.Generic.List<string> GetParametersForMepCategories(Document doc, System.Collections.Generic.List<string> categories) 
        { 
            return new System.Collections.Generic.List<string> { "System Type", "Size", "System Abbreviation", "Reference Level", "Mark", "Comments" }; 
        }
        public System.Collections.Generic.List<string> GetAllOpeningParameters(Document doc) { return new System.Collections.Generic.List<string> { "Mark", "Comments", "Level" }; }
    }

    public interface IOperationTracker
    {
        void StartOperation(string name);
        void EndOperation();
        void SetItemCount(int count);
    }
    
    public class FilterUiStateProvider
    {
        // Public static property returning a Func, initialized to return an empty list
        public static Func<System.Collections.Generic.List<string>> GetSelectedHostCategories { get; set; } = () => new System.Collections.Generic.List<string>();
    }
}

namespace JSE_Parameter_Service.Models
{
    public class OpeningFilter
    {
        public ClashZoneStorage ClashZoneStorage { get; set; } = new ClashZoneStorage();
    }

    public class LinkedFileInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Title { get; set; } 
        public bool IsLoaded { get; set; }
        public Document Doc { get; set; }
        public RevitLinkInstance LinkInstance { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
    }

    public class VersionInfo
    {
        public static string VersionNumber => "1.0.0";
    }
}

namespace JSE_Parameter_Service.Views
{
    public class ServiceTypeAbbreviationDialog : System.Windows.Forms.Form
    {
        public ServiceTypeAbbreviationDialog(Document doc) { }
    }

    public class OpeningParameterConfigurationDialog : System.Windows.Forms.Form
    {
        public OpeningParameterConfigurationDialog(Document doc) { }
        public JSE_Parameter_Service.Models.ParameterTransferConfiguration GetConfiguration() { return new JSE_Parameter_Service.Models.ParameterTransferConfiguration(); }
    }
}

namespace JSE_Parameter_Service.Services.Logging
{
    public class FilterNameHelper
    {
        public static string GetFilterName(string filter) { return "Filter"; }
        public static string NormalizeBaseName(string name, string filter, string category) { return name; }
    }



}

namespace JSE_Parameter_Service.Services
{
    public class LoggingConfiguration
    {
        public static bool IsLoggingEnabled(string serviceName) { return true; }
    }
    
    public class ErrorSummary
    {
        public int TotalErrors { get; set; }
        public int CriticalErrors { get; set; }
        public int Warnings { get; set; }
        public System.Collections.Generic.List<string> ErrorMessages { get; set; } = new System.Collections.Generic.List<string>();
        public bool ShouldAbort { get; set; }
    }

    public class ErrorHandlingResult
    {
        public bool ShouldContinue { get; set; } = true;
        public string Message { get; set; } = "";
        public string LogLevel { get; set; } = "Info";
        public System.Exception Exception { get; set; }
        public string Scope { get; set; }
        public object Context { get; set; }
    }

    public interface ILogger
    {
        void Info(string msg);
        void Info(string msg, string scope);
        void Debug(string msg);
        void Debug(string msg, string scope);
        void Error(string msg);
        void Error(string msg, string scope);
        void Error(string msg, System.Exception ex, string scope);
        void Warning(string msg);
        void Warning(string msg, string scope);
    }

    public class LoggerAdapter : ILogger
    {
        public static ILogger Default => new LoggerAdapter();
        public void Info(string msg) { }
        public void Info(string msg, string scope) { }
        public void Debug(string msg) { }
        public void Debug(string msg, string scope) { }
        public void Error(string msg) { }
        public void Error(string msg, string scope) { }
        public void Error(string msg, System.Exception ex, string scope) { }
        public void Warning(string msg) { }
        public void Warning(string msg, string scope) { }
    }

    
    public class ParameterOperationPerformanceMonitor : System.IDisposable
    {
         public ParameterOperationPerformanceMonitor(string op) { }
         public void Dispose() { }
         public void SetItemCount(int count) { }
    }
}

namespace JSE_Parameter_Service.Services
{
    public class RevitSleeveCollector
    {
        public System.Collections.Generic.List<int> CollectAllSleeveIds(Document doc)
        {
            return new System.Collections.Generic.List<int>();
        }
    }
}
