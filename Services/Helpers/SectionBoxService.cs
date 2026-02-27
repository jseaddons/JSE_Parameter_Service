using System;
using Autodesk.Revit.DB;
using System.Data;
using System.Data.Common;

namespace JSE_Parameter_Service.Services
{
    public interface ISectionBoxService
    {
        void CaptureAndStore(View3D view3D, DbConnection dbConnection);
        BoundingBoxXYZ? GetSectionBoxBounds(DbConnection dbConnection);
    }

    public class SectionBoxService : ISectionBoxService
    {
        public void CaptureAndStore(View3D view3D, DbConnection dbConnection)
        {
            if (view3D == null || !view3D.IsSectionBoxActive) return;
            var sectionBox = view3D.GetSectionBox();
            var transform = sectionBox.Transform;
            XYZ min = sectionBox.Min;
            XYZ max = sectionBox.Max;
            // Transform all 8 corners to world coordinates and get global min/max
            double wMinX = double.MaxValue, wMinY = double.MaxValue, wMinZ = double.MaxValue;
            double wMaxX = double.MinValue, wMaxY = double.MinValue, wMaxZ = double.MinValue;
            XYZ[] localCorners = new XYZ[8]
            {
                new XYZ(min.X, min.Y, min.Z), new XYZ(max.X, min.Y, min.Z), new XYZ(min.X, max.Y, min.Z), new XYZ(max.X, max.Y, min.Z),
                new XYZ(min.X, min.Y, max.Z), new XYZ(max.X, min.Y, max.Z), new XYZ(min.X, max.Y, max.Z), new XYZ(max.X, max.Y, max.Z)
            };
            foreach (XYZ corner in localCorners)
            {
                XYZ worldCorner = transform.OfPoint(corner);
                if (worldCorner.X < wMinX) wMinX = worldCorner.X;
                if (worldCorner.Y < wMinY) wMinY = worldCorner.Y;
                if (worldCorner.Z < wMinZ) wMinZ = worldCorner.Z;
                if (worldCorner.X > wMaxX) wMaxX = worldCorner.X;
                if (worldCorner.Y > wMaxY) wMaxY = worldCorner.Y;
                if (worldCorner.Z > wMaxZ) wMaxZ = worldCorner.Z;
            }
            // Store in DB (replace with your ORM or raw SQL as needed)
            using (var cmd = dbConnection.CreateCommand())
            {
                cmd.CommandText = @"REPLACE INTO SessionContext (Key, Value, UpdatedAt) VALUES (@k1, @v1, @dt), (@k2, @v2, @dt), (@k3, @v3, @dt), (@k4, @v4, @dt), (@k5, @v5, @dt), (@k6, @v6, @dt), (@k7, @v7, @dt)";

                void AddParam(string name, object value)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value;
                    cmd.Parameters.Add(p);
                }

                AddParam("@k1", "SectionBoxMinX"); AddParam("@v1", wMinX);
                AddParam("@k2", "SectionBoxMinY"); AddParam("@v2", wMinY);
                AddParam("@k3", "SectionBoxMinZ"); AddParam("@v3", wMinZ);
                AddParam("@k4", "SectionBoxMaxX"); AddParam("@v4", wMaxX);
                AddParam("@k5", "SectionBoxMaxY"); AddParam("@v5", wMaxY);
                AddParam("@k6", "SectionBoxMaxZ"); AddParam("@v6", wMaxZ);
                AddParam("@k7", "SectionBoxIsActive"); AddParam("@v7", 1);
                AddParam("@dt", DateTime.UtcNow);
                cmd.ExecuteNonQuery();
            }
        }

        public BoundingBoxXYZ? GetSectionBoxBounds(DbConnection dbConnection)
        {
            double minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;
            bool isActive = false;
            using (var cmd = dbConnection.CreateCommand())
            {
                cmd.CommandText = "SELECT Key, Value FROM SessionContext WHERE Key LIKE 'SectionBox%'";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string key = reader.GetString(0);
                        string value = reader.GetString(1);
                        switch (key)
                        {
                            case "SectionBoxMinX": minX = double.Parse(value); break;
                            case "SectionBoxMinY": minY = double.Parse(value); break;
                            case "SectionBoxMinZ": minZ = double.Parse(value); break;
                            case "SectionBoxMaxX": maxX = double.Parse(value); break;
                            case "SectionBoxMaxY": maxY = double.Parse(value); break;
                            case "SectionBoxMaxZ": maxZ = double.Parse(value); break;
                            case "SectionBoxIsActive": isActive = value == "1"; break;
                        }
                    }
                }
            }
            if (!isActive) return null;
            return new BoundingBoxXYZ { Min = new XYZ(minX, minY, minZ), Max = new XYZ(maxX, maxY, maxZ) };
        }
    }
}
