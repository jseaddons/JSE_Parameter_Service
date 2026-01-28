using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace JSE_Parameter_Service
{
    public static class Utils
    {
        public static string GetParameterValue(Element e, string paramName)
        {
            Parameter p = e.LookupParameter(paramName);
            if (p == null) return string.Empty;
            return p.AsString() ?? string.Empty;
        }

        public static void ShowInfo(string title, string message)
        {
            // Simple placeholder
            System.Windows.Forms.MessageBox.Show(message, title);
        }

        public static void ShowError(string title, string message)
        {
            System.Windows.Forms.MessageBox.Show(message, title, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
        }
    }
}
