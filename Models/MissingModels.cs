using System;
using System.Collections.Generic;

namespace JSE_Parameter_Service.Models
{
    public class ErrorHandlingResult
    {
        // Stub
    }

    public class ErrorSummary
    {
        // Stub
    }

    // Creating PerformanceMonitor alias if needed or class stub
    public class PerformanceMonitor : JSE_Parameter_Service.Services.Interfaces.IPerformanceMonitor
    {
         // Stub to satisfy constructor if PlacementPerformanceMonitor is not used directly
         public PerformanceMonitor(string logPath) { }
         public bool IsEnabled => false;
         public void StartOperation(string name) { }
         public void StopOperation(string name, int count = 0) { }
         public void LogMetric(string name, object value) { }
         public JSE_Parameter_Service.Services.Interfaces.IOperationTracker TrackOperation(string name) 
         { 
            return new OperationTracker(); 
         }

         public class OperationTracker : JSE_Parameter_Service.Services.Interfaces.IOperationTracker
         {
             public void Dispose() { }
             public void SetItemCount(int count) { }
             public JSE_Parameter_Service.Services.Interfaces.IOperationTracker TrackSubOperation(string name) { return this; }
         }
    }
}
