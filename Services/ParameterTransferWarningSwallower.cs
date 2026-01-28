using Autodesk.Revit.DB;

namespace JSE_Parameter_Service.Services
{
    public class ParameterTransferWarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            return FailureProcessingResult.Continue;
        }
    }
}
