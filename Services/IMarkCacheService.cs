using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using JSE_Parameter_Service.Data;
using JSE_Parameter_Service.Models;

namespace JSE_Parameter_Service.Services
{
    public interface IMarkCacheService
    {
        void Initialize(Document doc, SleeveDbContext context);
        IEnumerable<ClashZone> GetZonesForCombined(int combinedSleeveInstanceId);
        IEnumerable<ClashZone> GetZonesForCluster(int clusterInstanceId);
    }
}
