using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using JSE_Parameter_Service.Data;
using JSE_Parameter_Service.Data.Repositories;
using JSE_Parameter_Service.Models;

namespace JSE_Parameter_Service.Services
{
    public class MarkCacheService : IMarkCacheService
    {
        private SleeveDbContext _context;
        private CombinedSleeveRepository _combinedRepo;
        private ClashZoneRepository _clashRepo;

        public void Initialize(Document doc, SleeveDbContext context)
        {
            _context = context;
            _combinedRepo = new CombinedSleeveRepository(context);
            _clashRepo = new ClashZoneRepository(context);
        }

        public IEnumerable<ClashZone> GetZonesForCombined(int combinedSleeveInstanceId)
        {
            if (_combinedRepo == null || _clashRepo == null) return Enumerable.Empty<ClashZone>();

            var combinedSleeve = _combinedRepo.GetCombinedSleeveByInstanceId(combinedSleeveInstanceId);
            if (combinedSleeve == null || combinedSleeve.Constituents == null) return Enumerable.Empty<ClashZone>();

            var results = new List<ClashZone>();
            foreach (var constituent in combinedSleeve.Constituents)
            {
                if (constituent.Type == ConstituentType.Individual && constituent.ClashZoneGuid.HasValue)
                {
                    var zones = _clashRepo.GetClashZonesByGuids(new[] { constituent.ClashZoneGuid.Value });
                    if (zones != null) results.AddRange(zones);
                }
                else if (constituent.Type == ConstituentType.Cluster && constituent.ClusterInstanceId.HasValue)
                {
                    var zones = _clashRepo.GetClashZonesBySleeveIds(new[] { constituent.ClusterInstanceId.Value });
                    if (zones != null) results.AddRange(zones);
                }
            }
            return results;
        }

        public IEnumerable<ClashZone> GetZonesForCluster(int clusterInstanceId)
        {
            if (_clashRepo == null) return Enumerable.Empty<ClashZone>();
            return _clashRepo.GetClashZonesBySleeveIds(new[] { clusterInstanceId });
        }
    }
}
