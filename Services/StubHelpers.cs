using System.Collections.Generic;

namespace JSE_Parameter_Service.Services
{
    public class ApplicationProfileService
    {
        public static ApplicationProfileService Instance { get; } = new ApplicationProfileService();
        public JSE_Parameter_Service.Models.SettingsModel GetCurrentSettings() => new JSE_Parameter_Service.Models.SettingsModel();
    }
}

namespace JSE_Parameter_Service.Models
{
    public class SettingsModel
    {
        public double MinWallThickness { get; set; } = 0;
        public bool IncludeHostElementsNotVisible { get; set; } = false;
        public bool IncludeReferenceElementsNotVisible { get; set; } = false;
        
        // Added properties
        public double RoundingValue { get; set; } = 5.0; // Default 5mm
        public bool RoundAlwaysUp { get; set; } = true;
        public bool PipeOpeningTypeRectangular { get; set; } = false;
    }
}

namespace JSE_Parameter_Service.Services.ClearanceProviders
{
    public class ClearanceManager
    {
         public static ClearanceManager Instance { get; } = new ClearanceManager();
         public Dictionary<string, double> GetUIClearances() => new Dictionary<string, double>();
    }
}


