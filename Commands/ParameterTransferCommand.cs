using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using ICommand = System.Windows.Input.ICommand;
using JSE_Parameter_Service.Models;
using JSE_Parameter_Service.Services;

namespace JSE_Parameter_Service.Commands
{
    /// <summary>
    /// Bulletproof parameter transfer using industry-standard pattern
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ParameterTransferCommand : ICommand, IExternalCommand
    {
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) 
        {
             if (parameter is UIApplication app) Execute(app);
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Execute(commandData.Application);
            return Result.Succeeded;
        }
        private readonly Document _doc;
        private readonly List<ElementId> _openingIds;
        private readonly ParameterTransferConfiguration _config;
        private readonly string _logPrefix;

        public ParameterTransferCommand(Document doc, List<ElementId> openingIds, ParameterTransferConfiguration config)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _openingIds = openingIds ?? throw new ArgumentNullException(nameof(openingIds));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logPrefix = "[ParameterTransferCommand]";
        }

        private const string TransferLog = "parameter_transfer.log";

        private static void LogStep(string message)
        {
            SafeFileLogger.SafeAppendTextAlways(TransferLog, message);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                LogStep($"========== PARAMETER TRANSFER STARTED ==========");
                LogStep($"[STEP 1] Command invoked — {_openingIds.Count} openings, {_config.Mappings.Count} total mappings");
                DebugLogger.Info($"{_logPrefix} Starting parameter transfer for {_openingIds.Count} openings");

                // ---- 1. VALIDATION: Check document state ----
                if (!_doc.IsModifiable)
                {
                    var msg = "Document is read-only or workshared and not checked out";
                    LogStep($"[STEP 1 FAILED] Document not modifiable — aborting");
                    DebugLogger.Error($"{_logPrefix} {msg}");
                    TaskDialog.Show("Error", msg);
                    return;
                }
                LogStep($"[STEP 1 OK] Document is modifiable");

                // ---- 2. READ-ONLY: Data gathering (NO transaction) ----
                var transferService = new ParameterTransferService();
                var validationResult = ValidateTransferConfiguration(_config);

                if (!validationResult.IsValid)
                {
                    LogStep($"[STEP 2 FAILED] Configuration invalid: {validationResult.ErrorMessage}");
                    TaskDialog.Show("Configuration Error", validationResult.ErrorMessage);
                    return;
                }

                var enabledCount = _config.Mappings.Count(m => m.IsEnabled);
                var mappingDetails = string.Join(", ", _config.Mappings.Where(m => m.IsEnabled).Select(m => $"{m.SourceParameter}->{m.TargetParameter}"));
                LogStep($"[STEP 2 OK] Configuration validated — {enabledCount} enabled mappings: [{mappingDetails}]");
                LogStep($"[STEP 2] TransferModelNames={_config.TransferModelNames}, ModelNameParameter='{_config.ModelNameParameter}'");
                DebugLogger.Info($"{_logPrefix} Configuration validated: {_config.Mappings.Count} mappings enabled");

                // ---- 3. SINGLE TRANSACTION: All parameter transfers ----
                LogStep($"[STEP 3] Starting Revit transaction...");
                using (var t = new Transaction(_doc, "Transfer Parameters to Openings"))
                {
                    if (t.Start() == TransactionStatus.Started)
                    {
                        LogStep($"[STEP 3 OK] Transaction started successfully");

                        // Set failure handler to auto-resolve warnings
                        var options = t.GetFailureHandlingOptions();
                        options.SetFailuresPreprocessor(new ParameterTransferWarningSwallower());
                        t.SetFailureHandlingOptions(options);

                        // ✅ UPDATED: Use optimized Batch Transfer (Read-Calculate(Parallel)-Write)
                        LogStep($"[STEP 4] Calling ExecuteBatchTransferInTransaction...");
                        var result = transferService.ExecuteBatchTransferInTransaction(_doc, _openingIds, _config);
                        LogStep($"[STEP 4 DONE] Batch transfer returned — Success={result.Success}, Transferred={result.TransferredCount}, Failed={result.FailedCount}, Message='{result.Message}'");

                        if (result.Warnings.Count > 0)
                            LogStep($"[STEP 4 WARNINGS] {result.Warnings.Count} warnings: {string.Join("; ", result.Warnings.Take(10))}");

                        LogStep($"[STEP 5] Committing transaction...");
                        var status = t.Commit();
                        if (status == TransactionStatus.Committed)
                        {
                            LogStep($"[STEP 5 OK] Transaction committed — {result.TransferredCount} sleeves updated");
                            ShowTransferResults(result);
                            DebugLogger.Info($"{_logPrefix} Successfully transferred {result.TransferredCount} parameters");
                        }
                        else
                        {
                            LogStep($"[STEP 5 FAILED] Transaction commit failed: {status}");
                            DebugLogger.Error($"{_logPrefix} Transaction failed to commit: {status}");
                            TaskDialog.Show("Error", "Failed to transfer parameters. Check log for details.");
                        }
                    }
                    else
                    {
                        LogStep($"[STEP 3 FAILED] Could not start transaction");
                        DebugLogger.Error($"{_logPrefix} Failed to start transaction");
                        TaskDialog.Show("Error", "Failed to start transaction for parameter transfer.");
                    }
                }
                LogStep($"========== PARAMETER TRANSFER FINISHED ==========\n");
            }
            catch (Exception ex)
            {
                LogStep($"[FATAL ERROR] Exception: {ex.Message}\n{ex.StackTrace}");
                DebugLogger.Error($"{_logPrefix} Exception during parameter transfer: {ex.Message}");
                DebugLogger.Error($"{_logPrefix} Stack trace: {ex.StackTrace}");
                TaskDialog.Show("Error", $"Exception during parameter transfer: {ex.Message}");
            }
        }

        private ValidationResult ValidateTransferConfiguration(ParameterTransferConfiguration config)
        {
            var result = new ValidationResult { IsValid = true };

            if (config.Mappings.Count == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "No parameter mappings configured.";
                return result;
            }

            var enabledMappings = config.Mappings.Where(m => m.IsEnabled).ToList();
            if (enabledMappings.Count == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "No enabled parameter mappings found.";
                return result;
            }

            // Validate each mapping has required parameters
            foreach (var mapping in enabledMappings)
            {
                if (string.IsNullOrEmpty(mapping.SourceParameter) || string.IsNullOrEmpty(mapping.TargetParameter))
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"Invalid mapping: Source or Target parameter is empty.";
                    break;
                }
            }

            return result;
        }

        private void ShowTransferResults(ParameterTransferResult result)
        {
            var message = $"Parameter transfer completed successfully!\n\n" +
                                    $"Transferred: {result.TransferredCount}\n" +
                                    $"Failed: {result.FailedCount}";
                    
            if (result.Warnings.Count > 0)
            {
                message += $"\nWarnings: {result.Warnings.Count}";
            }

            TaskDialog.Show("Success", message);
        }
    }

    /// <summary>
    /// Validation result for configuration checking
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    // ParameterTransferWarningSwallower moved to Models/ParameterTransferWarningSwallower.cs
}
