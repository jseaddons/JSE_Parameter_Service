using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using JSE_Parameter_Service.Views;

namespace JSE_Parameter_Service.Commands
{
    /// <summary>
    /// Test command to open the new Parameter Service Dialog V2
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TestParameterServiceDialogV2Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var document = commandData.Application.ActiveUIDocument.Document;
                var uiDocument = commandData.Application.ActiveUIDocument;

                // Open the new UI
                using (var dialog = new ParameterServiceDialogV2(document, uiDocument))
                {
                    dialog.ShowDialog();
                }

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Error", $"Failed to open Parameter Service V2:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}

