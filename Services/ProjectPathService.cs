using System;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace JSE_Parameter_Service.Services
{
    public static class ProjectPathService
    {
        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Default";
            // Remove invalid path chars and trim
            var invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            var pattern = "[" + Regex.Escape(invalid) + "]";
            var cleaned = Regex.Replace(name, pattern, "_");
            cleaned = cleaned.Trim();
            if (cleaned.Length == 0) cleaned = "Default";
            return cleaned;
        }

        public static string GetProjectRoot(Document doc)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // ✅ FIX: AppData can be empty string in some Revit contexts (causes Path.Combine to throw 'path1' null)
                if (string.IsNullOrEmpty(appData))
                    appData = Environment.GetEnvironmentVariable("APPDATA") ?? Path.GetTempPath();

                var root = Path.Combine(appData, "JSE_MEP_Openings", "Projects");

                // ✅ FIX: Strip .rvt extension before sanitizing to match JSE_MEPOPENING_23's path resolution.
                // Without this, a document titled "MyProject.rvt" resolves to folder "MyProject_rvt" here
                // but "MyProject" in MEPOPENING_23 — causing a different DB path and empty dropdowns.
                var rawName = doc?.Title ?? "Default";
                if (rawName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                    rawName = Path.GetFileNameWithoutExtension(rawName);

                var projectName = Sanitize(rawName);
                var projectPath = Path.Combine(root, projectName);

                if (!Directory.Exists(projectPath))
                    Directory.CreateDirectory(projectPath);

                return projectPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectPathService] Error resolving project path: {ex.Message}");
                // Absolute fallback — same as MEPOPENING_23
                return Path.Combine(Path.GetTempPath(), "JSE_MEP_Openings", "Default");
            }
        }

        public static string GetFiltersDirectory(Document doc)
        {
            return Path.Combine(GetProjectRoot(doc), "Filters");
        }

        public static void EnsureFiltersDirectory(Document doc)
        {
            var dir = GetFiltersDirectory(doc);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }

}


