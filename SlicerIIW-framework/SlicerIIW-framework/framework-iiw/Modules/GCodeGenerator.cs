using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using System.Text;
using Clipper2Lib;

using SlicerSettings = framework_iiw.Settings.Settings;

namespace framework_iiw.Modules
{
    class GCodeGenerator
    {
        
        public GCodeGenerator()
        {

        }
        public void GenerateGCode(List<PathsD> layers)
        {
            List<string> gCode = new List<string>();
            string startCode = "M140 S60 ;set bed temperature\r\n" +
                "M190 S60 ;wait for bed temperature\r\n" +
                "M104 S220 ;set nozzle temperature\r\n" +
                "M109 S220 ;wait for nozzle temperature\r\n" +
                "M82 ; absolute extrusion mode\r\n" +
                "G28 ; Home all axes\r\n" +
                "G92 E0 ; Reset Extruder\r\n" +
                "G1 Z2.0 F3000 ; Move Z axis up little to prevent scratching of Heat Bed\r\n" +
                "G1 X0.1 Y20 Z0.3 F5000.0 ; Move to start position\r\n" +
                "G1 X0.1 Y200.0 Z0.3 F1500 E15 ; Draw the first line\r\n" +
                "G1 X0.4 Y200.0 Z0.3 F5000.0 ; Move to side a little\r\n" +
                "G1 X0.4 Y20 Z0.3 F1500.0 E30 ; Draw the second line\r\n" +
                "G92 E0 ; Reset Extruder\r\n" +
                "G1 Z2.0 F3000 ; Move Z axis up little to prevent scratching of Heat Bed\r\n" +
                "G92 E0\r\nG1 F2400 E-5 ;retract filament to avoid oozing\r\nM107 ; fan off for first layer\r\n\r\n";

            gCode.Add(startCode);


            double extrusionMultiplier = 0.05; // adjust based on filament type and printer calibration


            // for each layer
            for (int i = 0; i < layers.Count; i++)
            {
                double currentLayerHeight = SlicerSettings.LayerHeight * (i + 1);
                gCode.Add($"G1 Z{currentLayerHeight:F2} F3000 ; Move to layer {i + 1}");

                // for each path in the layer
                foreach (var path in layers[i])
                {
                    if (path.Count < 2)
                        continue; // skip if the path has fewer than 2 points

                    // move to the starting point of the path without extrusion
                    var start = path[0];
                    gCode.Add($"G0 X{start.x:F2} Y{start.y:F2} ; Move to start of path");

                    // extrude along the path
                    for (int j = 1; j < path.Count; j++)
                    {
                        var point = path[j];
                        double distance = CalculateDistance(start, point);
                        double extrusion = distance * extrusionMultiplier;

                        gCode.Add($"G1 X{point.x:F2} Y{point.y:F2} E{extrusion:F4} ; Extrude along path");

                        // update the start point to the current point
                        start = point;
                    }
                }

                // Optional: retraction command for layer transitions if needed
                //gCode.Add("G1 F2400 E-2 ; Retract filament slightly before layer change");
            }

            string endCode = "M140 S0 ; set bed temperature to 0\r\n" +
                "M107 ; fan off\r\n" +
                "M220 S100 ; Reset Speed factor override percentage to default (100%)\r\n" +
                "M221 S100 ; Reset Extrude factor override percentage to default (100%)\r\n" +
                "G91 ; Set coordinates to relative\r\n" +
                "G1 F1800 E-3 ; Retract filament 3 mm to prevent oozing\r\n" +
                "G1 F3000 Z20 ; Move Z Axis up 20 mm to allow filament ooze freely\r\n" +
                "G90 ; Set coordinates to absolute\r\n" +
                "G1 X0 Y235 F1000 ; Move Heat Bed to the front for easy print removal\r\n" +
                "M107 ;fan off\r\n" +
                "M84 ; Disable stepper motors\r\n" +
                "M82 ; absulute extrusion mode\r\n" +
                "M104 S0 ;set extruder temperature";

            gCode.Add(endCode);

            using (StreamWriter file = new StreamWriter("test.gcode"))
            {
                foreach (string line in gCode)
                {
                    file.WriteLine(line);
                }
            }
        }

      
        private double CalculateDistance(PointD p1, PointD p2)
        {
            double distance = Math.Sqrt(Math.Pow(p2.x - p1.x, 2) + Math.Pow(p2.y - p1.y, 2));

            return distance;
        }
    }
}
