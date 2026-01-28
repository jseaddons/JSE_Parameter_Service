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
            panel.AddItem(buttonData);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
