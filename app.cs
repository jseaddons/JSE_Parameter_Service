using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace JSE_Parameter_Service
{
    [Transaction(TransactionMode.Manual)]
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "JSE_Parameter_Service"; // ⚠️ MUST MATCH MAIN ADDIN TAB NAME EXACTLY
            string panelName = "Commands";
            RibbonPanel panel = null;
            
            // 1. Try to find the existing panel from the Main Addin
            try
            {
                List<RibbonPanel> panels = application.GetRibbonPanels(tabName);
                panel = panels.FirstOrDefault(p => p.Name == panelName);
            }
            catch { }
            // 2. Create if missing (Backup)
            if (panel == null)
            {
                try { application.CreateRibbonTab(tabName); } catch { } 
                panel = application.CreateRibbonPanel(tabName, panelName);
            }
            // 3. Add the "Parameter Transfer" Button
            // Ensure you copy 'ParameterTransferCommand.cs' to the new project!
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "cmdParameterTransfer", 
                "Parameter\nTransfer", 
                assemblyPath, 
                "JSE_Parameter_Service.Commands.ParameterTransferCommand"); // Note: Namespace might need adjustment if Commands are in JSE_Parameter_Service.Commands
            PushButton button = panel.AddItem(buttonData) as PushButton;
            if (button != null)
            {
                button.LargeImage = LoadIconImage("JSE_Parameter_Service.Resources.Icons.RibbonIcon32.png");
                button.Image = LoadIconImage("JSE_Parameter_Service.Resources.Icons.RibbonIcon16.png");
                button.ToolTip = "Transfer parameters from MEP elements to openings.";
            }

            return Result.Succeeded;
        }

        private System.Windows.Media.ImageSource? LoadIconImage(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(stream, 
                        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, 
                        System.Windows.Media.Imaging.BitmapCacheOption.Default);
                    return decoder.Frames[0];
                }
            }
            catch { return null; }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
