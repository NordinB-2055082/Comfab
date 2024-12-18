using Clipper2Lib;
using framework_iiw.Data_Structures;
using framework_iiw.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Markup.Localizer;
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
            Rect3D meshBounds = meshGeometry3D.Bounds; // Infill: get bounds

            List<int> triangleIndices = meshGeometry3D.TriangleIndices.ToList();
            List<Point3D> positions = meshGeometry3D.Positions.ToList();
           
            var layers = new List<PathsD>();
            var clippedInfillPaths = new List<PathsD>();
            var layersInnerPaths = new List<PathsD>(); 
            var layersInfillPaths = new List<PathsD>();
            var FloorsAndRoofs = new List<PathsD>();

            var roofPaths = new List<PathsD>();
            var floorPaths = new List<PathsD>();
            var totalAmountOfLayers  = geometryModel3D.Bounds.SizeZ / SlicerSettings.LayerHeight;
            var sizeXModel = geometryModel3D.Bounds.SizeX;
            var sizeYModel = geometryModel3D.Bounds.SizeY;

            // infill step 2
            double infillSpacing = SlicerSettings.NozzleThickness*2 / SlicerSettings.InfillDensity;
            //double infillSpacing = SlicerSettings.InfillDensity * (meshBounds.SizeX + meshBounds.SizeY)/100;
            //double infillSpacing = 10;
            
            // roof and floor settings
            int numRoofLayers = SlicerSettings.RoofLayers; 
            int numFloorLayers = SlicerSettings.FloorLayers;
               
            for (var idx = 0; idx < totalAmountOfLayers; idx++)
            {
                var result = SliceModelAtSpecificLayer(idx * SlicerSettings.LayerHeight, meshGeometry3D, triangleIndices, positions);

                var layer = result.Item1;
                var innerPaths = result.Item2;
                layers.Add(layer);
                layersInnerPaths.Add(innerPaths);
                PathsD infillGrid = new PathsD();
                // step 3: generate the infill grid for the current layer
                if (idx < numFloorLayers || idx > totalAmountOfLayers - numRoofLayers)
                {
                    infillGrid = GenerateInfillGrid(meshBounds, SlicerSettings.NozzleThickness*2, (idx%2==0) , false);
                }
                else {
                    infillGrid = GenerateInfillGrid(meshBounds, infillSpacing, (idx % 2 == 0), true);
                }
                // step 4: clip the infill pattern to the current layer's inner paths
                PathsD clippedInfill = ClipInfillToLayer(infillGrid, innerPaths);
                PathsD combinedInfillAndShell = CombineInfillAndShell(clippedInfill, layer);
                clippedInfillPaths.Add(clippedInfill);
                layersInfillPaths.Add(combinedInfillAndShell);  // store the infill paths

            }
            for (int idx = 0; idx < totalAmountOfLayers; idx++)
            {
                PathsD roofInfillGrid = GenerateInfillGrid(meshBounds, SlicerSettings.NozzleThickness * 2, (idx % 2 == 0), false);

                PathsD roofs = DetectRoofs(idx, layers, numRoofLayers, roofInfillGrid);
                PathsD floors = DetectFloors(idx, layers, numFloorLayers);
                
                //roofPaths.Add(roofs);
                //floorPaths.Add(floors);

                //TODO ADD INFILL AND GIVE TO LAYERSINFILLPATHS
                //layersInfillPaths[idx].AddRange(roofsInfillPaths); // merge roof paths into the layer

                //layersInfillPaths[idx].AddRange(floorsInfillPaths); // merge floor paths into the layer
                if (roofs.Count > 0)
                {
                    //PathsD roofInfillGrid = GenerateInfillGrid(meshBounds, SlicerSettings.NozzleThickness * 2, (idx % 2 == 0), false);
                    PathsD roofInfill = ClipInfillToLayer(roofInfillGrid, roofs);
                    layersInfillPaths[idx].AddRange(roofInfill);
                    roofPaths.Add(roofs);
                }

                // Generate and merge floor infill
                if (floors.Count > 0)
                {
                    PathsD floorInfillGrid = GenerateInfillGrid(meshBounds, SlicerSettings.NozzleThickness * 2, (idx % 2 == 0), false);
                    PathsD floorInfill = ClipInfillToLayer(floorInfillGrid, floors);
                    layersInfillPaths[idx].AddRange(floorInfill);
                    floorPaths.Add(floorInfill);
                }
            }
            GCodeGenerator gCode = new GCodeGenerator();
            //gCode.GenerateGCode(layers, clippedInfillPaths, roofPaths, floorPaths, sizeXModel, sizeYModel);

            return roofPaths;
        }
        // --- detect floors for a specific layer
        private PathsD DetectFloors(int layerIdx, List<PathsD> layers, int numFloorLayers)
        {
            if (layerIdx == 0 || layers.Count < 2) return new PathsD(); // no floors for the first layer

            var currentLayer = layers[layerIdx];
            PathsD combined = new PathsD();
            combined.AddRange(currentLayer);

            //clipperStore.AddPaths(infillGrid, PathType.Subject, false);
            //clipperStore.AddPaths(innerPaths, PathType.Clip, false);

            //clipperStore.Execute(ClipType.Intersection, FillRule.NonZero, result);

            for (int i = 1; i <= numFloorLayers && layerIdx - i >= 0; i++)
            {
                combined.AddRange(Clipper.BooleanOp(ClipType.Intersection, combined, layers[layerIdx - i], FillRule.EvenOdd, 5));
            }

            return Clipper.BooleanOp(ClipType.Difference, currentLayer, combined, FillRule.EvenOdd, 5);
        }

        // --- detect roofs for a specific layer
        private PathsD DetectRoofs(int layerIdx, List<PathsD> layers, int numRoofLayers, PathsD roofInfillGrid)
        {
            if (layerIdx == layers.Count - 1 || layers.Count < 2) return new PathsD(); // no roofs for the last layer

            var currentLayer = layers[layerIdx];
            PathsD combined = new PathsD();
            combined.AddRange(ClipInfillToLayer(roofInfillGrid, currentLayer));
            PathsD removed = new PathsD();
            for (int i = 1; i <= numRoofLayers && layerIdx + i < layers.Count; i++)
            {
                var localRoof = ClipInfillToLayer(roofInfillGrid, layers[layerIdx + i]);
                removed = (Clipper.BooleanOp(ClipType.Union, localRoof, removed, FillRule.EvenOdd, 5));
            }

            var local = Clipper.BooleanOp(ClipType.Difference, combined, removed, FillRule.EvenOdd, 5);
            return local;
            //return Clipper.InflatePaths(local, -((SlicerSettings.NozzleThickness / 2)), JoinType.Miter, EndType.Polygon, 5);
        }

        // --- Slice object at specific layer

        private (PathsD, PathsD) SliceModelAtSpecificLayer(double layer, MeshGeometry3D meshGeometry, List<int> triangleIndices, List<Point3D> positions)
        {
            var slicingPlaneHeight = GetSlicingPlaneHeight(meshGeometry.Bounds.Z, layer);

            // get paths according to slicing
            List<LineSegment> paths = SlicingAlgorithm(slicingPlaneHeight, triangleIndices, positions);

            // combine paths
            PathsD combinedPaths = ConnectLineSegments(paths);
            
            // make shells to print the walls
            var (innerPaths, shellPaths) = generateShellForPathsD(combinedPaths);

            return (shellPaths, innerPaths);
        }

        // ------
        private (PathsD, PathsD) generateShellForPathsD(PathsD paths)
        {
            PathsD sortedPolygons = Clipper.BooleanOp(ClipType.Union, paths, null, FillRule.EvenOdd, 5);

            PathsD results = new PathsD();
            PathsD innerPaths = new PathsD();

            //TODO dubbelcheck NozzleThickness

            for (int i = 0; i < SlicerSettings.AmountOfShells; i++)
            {
                results.AddRange(Clipper.InflatePaths(sortedPolygons, -((SlicerSettings.NozzleThickness / 2) + (i * SlicerSettings.NozzleThickness)), JoinType.Miter, EndType.Polygon, 5));
            }

            innerPaths.AddRange(Clipper.InflatePaths(sortedPolygons, -((SlicerSettings.NozzleThickness / 2) + ((SlicerSettings.AmountOfShells - 1) * SlicerSettings.NozzleThickness)), JoinType.Miter, EndType.Polygon, 5));

            return (innerPaths, results);
        }

        // ------ Generate infill Grid
        private PathsD GenerateInfillGrid(Rect3D bounds, double spacing, bool odd,  Boolean fullgrid = false)
        {
            var infillPaths = new PathsD();
            if(fullgrid){
                spacing = spacing * 2;
            }
            // Horizontal lines
            if (odd)
            {
                for (double y = bounds.Y; y <= bounds.Y + bounds.SizeY; y += spacing)
                {
                    PathD horizontalLine = new PathD
                {
                    new PointD(bounds.X, y),
                    new PointD(bounds.X + bounds.SizeX, y)
                };
                    infillPaths.Add(horizontalLine);
                }
                if (fullgrid)
                {
                    // Vertical lines
                    for (double x = bounds.X; x <= bounds.X + bounds.SizeX; x += spacing)
                    {
                        PathD verticalLine = new PathD
                {
                    new PointD(x, bounds.Y),
                    new PointD(x, bounds.Y + bounds.SizeY)
                };
                        infillPaths.Add(verticalLine);
                    }
                }
            }
            else
            {
                if (fullgrid)
                {
                    for (double y = bounds.Y; y <= bounds.Y + bounds.SizeY; y += spacing)
                    {
                        PathD horizontalLine = new PathD
                {
                    new PointD(bounds.X, y),
                    new PointD(bounds.X + bounds.SizeX, y)
                };
                        infillPaths.Add(horizontalLine);
                    }

                }
                // Vertical lines
                for (double x = bounds.X; x <= bounds.X + bounds.SizeX; x += spacing)
                    {
                        PathD verticalLine = new PathD
                {
                    new PointD(x, bounds.Y),
                    new PointD(x, bounds.Y + bounds.SizeY)
                };
                        infillPaths.Add(verticalLine);
                    }
                
            }


            PathsD infillPaths2 = Clipper.InflatePaths(infillPaths, -(SlicerSettings.NozzleThickness / 2), JoinType.Miter, EndType.Square, 5);
            return infillPaths2;
        }
        private PathsD ClipInfillToLayer(PathsD infillGrid, PathsD innerPaths)
        {
            // boolean intersection to keep the parts of the grid within the inner paths
            PathsD result = new PathsD();
            ClipperD clipperStore = new ClipperD();
            clipperStore.AddPaths(infillGrid, PathType.Subject, false);
            clipperStore.AddPaths(innerPaths, PathType.Clip, false);

            clipperStore.Execute(ClipType.Intersection, FillRule.NonZero, result);
            return result;
            //return Clipper.BooleanOp(ClipType.Intersection, infillGrid, innerPaths, FillRule.);
        }

        // --- Slicing Algorithm
        private PathsD CombineInfillAndShell(PathsD infill, PathsD shell)
        {
            PathsD result = new PathsD();
            ClipperD clipperStore = new ClipperD();
            clipperStore.AddPaths(infill, PathType.Subject, false);
            clipperStore.AddPaths(shell, PathType.Subject, false);


            clipperStore.Execute(ClipType.Union, FillRule.EvenOdd, result);
            return result;
        }
        private List<LineSegment> SlicingAlgorithm(double slicingPlaneHeight, List<int> triangleIndices, List<Point3D> positions) 
        {
            var paths = new List<LineSegment>();
            
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

        // Help function to compute the intersection of a triangle with the slicing plane
        private List<Point3D> GetPlaneIntersections(Point3D v0, Point3D v1, Point3D v2, double slicingPlaneHeight)
        {
            var intersectionPoints = new List<Point3D>();

            // Check each edge of the triangle for intersections with the slicing plane
            AddIntersection(v0, v1, slicingPlaneHeight, intersectionPoints);
            AddIntersection(v1, v2, slicingPlaneHeight, intersectionPoints);
            AddIntersection(v2, v0, slicingPlaneHeight, intersectionPoints);

            return intersectionPoints;
        }
        // Help function to check and add intersection points between two vertices
        private void AddIntersection(Point3D p1, Point3D p2, double slicingPlaneHeight, List<Point3D> intersectionPoints)
        {
            if ((p1.Z <= slicingPlaneHeight && p2.Z >= slicingPlaneHeight) || (p1.Z >= slicingPlaneHeight && p2.Z <= slicingPlaneHeight))
            {
                // Linear interpolation to find intersection point
                double t = (slicingPlaneHeight - p1.Z) / (p2.Z - p1.Z); //I think this ends up being 0/0 when all are same height. How is that legal? Why does this not throw an error?
                double x = p1.X + t * (p2.X - p1.X);
                double y = p1.Y + t * (p2.Y - p1.Y);
                intersectionPoints.Add(new Point3D(x, y, slicingPlaneHeight));
            }

        }
        private double GetSlicingPlaneHeight(double zOffset, double layer)
        {
            
            // Assuming each layer has a fixed height, calculate the Z-height for the current layer
            return zOffset + layer + 0.0000000001;
            //return 0;
        }

        // --- Path Combining Algorithm
        
        private PathsD ConnectLineSegments(List<LineSegment> lineSegments) {
            if (lineSegments.Count == 0) return new PathsD();

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
        // convert a Point3D to a PointD (2D point)
        private PointD ConvertToPointD(Point3D point3D)
        {
            return new PointD { x = point3D.X, y = point3D.Y };
        }

        // method to check if two PointD objects are close enough to be considered the same
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
