using Clipper2Lib;
using framework_iiw.Data_Structures;
using framework_iiw.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;

using SlicerSettings = framework_iiw.Settings.Settings;


namespace framework_iiw.Modules
{
    class ModelSlicer
    {
        GeometryModel3D? geometryModel3D;

        public ModelSlicer() {}
    
        // --- Slice

        public List<PathsD> Slice()
        {
            if (geometryModel3D == null) { throw new NullException("Geometry Model Must Be Loaded First"); }

            MeshGeometry3D meshGeometry3D = ModelLoader.LoadMesh(geometryModel3D);

            List<int> triangleIndices = meshGeometry3D.TriangleIndices.ToList();
            List<Point3D> positions = meshGeometry3D.Positions.ToList();

            var layers = new List<PathsD>();
            var totalAmountOfLayers  = geometryModel3D.Bounds.SizeZ / SlicerSettings.LayerHeight;

            for (var idx = 0; idx < totalAmountOfLayers; idx++)
            {
                var layer = SliceModelAtSpecificLayer(idx * SlicerSettings.LayerHeight, meshGeometry3D, triangleIndices, positions);

                layers.Add(layer);
            }

            return layers;
        }

        // --- Slice Object At Specific Layer

        private PathsD SliceModelAtSpecificLayer(double layer, MeshGeometry3D meshGeometry, List<int> triangleIndices, List<Point3D> positions)
        {
            var slicingPlaneHeight = GetSlicingPlaneHeight(meshGeometry.Bounds.Z, layer);

            // Get paths according to slicing
            var paths = SlicingAlgorithm(slicingPlaneHeight, triangleIndices, positions);

            // Combine paths
            var combinedPaths = ConnectLineSegments(paths);
            // Adjust to line segments

            return combinedPaths;
        }

        // ------

        // --- Slicing Algorithm

        private List<LineSegment> SlicingAlgorithm(double slicingPlaneHeight, List<int> triangleIndices, List<Point3D> positions) 
        {
            var paths = new List<LineSegment>();
            
            // NORDIN 
            for (int i = 0; i < triangleIndices.Count; i += 3)
            {
                // Get the vertices of the triangle
                Point3D v0 = positions[triangleIndices[i]];
                Point3D v1 = positions[triangleIndices[i + 1]];
                Point3D v2 = positions[triangleIndices[i + 2]];

                // Check if the triangle intersects with the slicing plane
                List<Point3D> intersections = GetPlaneIntersections(v0, v1, v2, slicingPlaneHeight);

                if (intersections.Count == 2)
                {
                    // Create a line segment from the two intersection points
                    var lineSegment = new LineSegment();
                    lineSegment.AddPoint(intersections[0]);  // Add first point
                    lineSegment.AddPoint(intersections[1]);  // Add second point
                    paths.Add(lineSegment);  // Add the line segment to paths
                }
            }
            
            return paths;
        }

        // NORDIN: Help function to compute the intersection of a triangle with the slicing plane
        private List<Point3D> GetPlaneIntersections(Point3D v0, Point3D v1, Point3D v2, double slicingPlaneHeight)
        {
            var intersectionPoints = new List<Point3D>();

            // Check each edge of the triangle for intersections with the slicing plane
            AddIntersection(v0, v1, slicingPlaneHeight, intersectionPoints);
            AddIntersection(v1, v2, slicingPlaneHeight, intersectionPoints);
            AddIntersection(v2, v0, slicingPlaneHeight, intersectionPoints);

            return intersectionPoints;
        }
        // Nordin: Help function to check and add intersection points between two vertices
        private void AddIntersection(Point3D p1, Point3D p2, double slicingPlaneHeight, List<Point3D> intersectionPoints)
        {
            if ((p1.Z <= slicingPlaneHeight && p2.Z >= slicingPlaneHeight) || (p1.Z >= slicingPlaneHeight && p2.Z <= slicingPlaneHeight))
            {
                // Linear interpolation to find intersection point
                double t = (slicingPlaneHeight - p1.Z) / (p2.Z - p1.Z);
                double x = p1.X + t * (p2.X - p1.X);
                double y = p1.Y + t * (p2.Y - p1.Y);
                intersectionPoints.Add(new Point3D(x, y, slicingPlaneHeight));
            }
        }
        private double GetSlicingPlaneHeight(double zOffset, double layer)
        {
            // TODO
            // Nordin: Assuming each layer has a fixed height, calculate the Z-height for the current layer
            double layerHeight = 0.2;  // Example: each layer is 0.2 units tall
            return zOffset + layer * layerHeight;
            //return 0;
        }

        // --- Path Combining Algorithm
        
        private PathsD ConnectLineSegments(List<LineSegment> lineSegments) {
            if (lineSegments.Count == 0) return new PathsD();

            // TODO: Nordin
            var paths = new PathsD();
            var remainingSegments = new List<LineSegment>(lineSegments);

            while (remainingSegments.Count > 0)
            {
                // Start a new path with the first segment
                var currentPath = new PathD();
                var currentSegment = remainingSegments[0];
                remainingSegments.RemoveAt(0);

                // Add the points of the current segment to the path
                currentPath.Add(ConvertToPointD(currentSegment.GetLineStart()));
                currentPath.Add(ConvertToPointD(currentSegment.GetLineEnd()));

                bool pathClosed = false;

                while (!pathClosed && remainingSegments.Count > 0)
                {
                    pathClosed = false;
                    for (int i = 0; i < remainingSegments.Count; i++)
                    {
                        var segment = remainingSegments[i];

                        // Convert Point3D to PointD for comparison
                        PointD start = ConvertToPointD(segment.GetLineStart());
                        PointD end = ConvertToPointD(segment.GetLineEnd());

                        // Check if the last point in the current path matches the start or end of the next segment
                        if (ArePointsClose(currentPath.Last(), start))
                        {
                            // Add the other end of the segment to the path
                            currentPath.Add(end);
                            remainingSegments.RemoveAt(i);
                            break;
                        }
                        else if (ArePointsClose(currentPath.Last(), end))
                        {
                            // Add the other end of the segment to the path (reversed)
                            currentPath.Add(start);
                            remainingSegments.RemoveAt(i);
                            break;
                        }
                    }

                    // If the first and last point of the path are the same, we've closed a loop
                    pathClosed = ArePointsClose(currentPath.First(), currentPath.Last());
                }

                // Add the completed path to paths
                paths.Add(currentPath);
            }

            return paths;

        }
        //Nordin: Helper method to convert a Point3D to a PointD (2D point)
        private PointD ConvertToPointD(Point3D point3D)
        {
            return new PointD { x = point3D.X, y = point3D.Y };
        }

        // Nordin: Helper method to check if two PointD objects are close enough to be considered the same
        private bool ArePointsClose(PointD p1, PointD p2, double tolerance = 1e-6)
        {
            return Math.Abs(p1.x - p2.x) < tolerance && Math.Abs(p1.y - p2.y) < tolerance;
        }

        // ------

        // --- Convert List<LineSegment> To PathD 

        public PathD ConvertListToPathD(List<LineSegment> list)
        {
            var path = new PathD();

            foreach (var segment in list)
            {
                path.AddRange(segment.GetPoints());
            }

            return path;
        }

        // ------

        // --- Default Getter And Setters

        public void SetGeometryModel3D(GeometryModel3D geometryModel)
        {
            geometryModel3D = geometryModel;
        }  
    
        public GeometryModel3D GetGeometryModel3D()
        {
            return geometryModel3D;
        }
    
        // ------
    }
}
