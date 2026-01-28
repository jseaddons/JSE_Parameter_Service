using System;

namespace JSE_Parameter_Service.Services.Interfaces
{
    public interface IPerformanceMonitor
    {
        bool IsEnabled { get; }
        void StartOperation(string operationName);
        void StopOperation(string operationName, int itemCount = 0);
    }

    public interface IOperationTracker : IDisposable
    {
        // Wrapper for operation tracking
    }
}

namespace JSE_Parameter_Service.Services.ErrorHandling
{
    public interface IErrorPolicy
    {
        // Stub
    }


}
