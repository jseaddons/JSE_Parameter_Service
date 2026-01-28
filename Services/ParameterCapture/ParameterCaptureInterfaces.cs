using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace JSE_Parameter_Service.Services.ParameterCapture
{
    public interface IParameterPolicy
    {
        HashSet<string> GetWhitelist();
        HashSet<string> GetMustCaptureKeys();
        bool ShouldCapture(string parameterName, Element element);
        string MapParameterName(string requestedName, Element element);
        int GetMaxParameterLimit();
    }

    public interface IParameterKeyStore
    {
        IEnumerable<string> LoadLearnedKeys();
    }
}
