using Autodesk.Revit.UI;

namespace JSE_Parameter_Service.Services
{
    /// <summary>
    /// Base interface for all Revit commands
    /// </summary>
    public interface ICommand
    {
        void Execute(UIApplication app);
    }
}

