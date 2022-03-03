//*******************************************************************************************
//
// Library for writing GPS data files according to legacy VTK files of different flavours.
//
// Usage:
//   1) instantiate class with main title as parameter
//   2) call respective methods to fill the various file parts:
//      Header, Pointdata, Celldata, Pointdatasets, Celldatasets.
//   3) finally produce the output file by calling WriteToFile(string) 
//
// Known problems and restrictions:
//   The order of calls to the methods is free, however some need the number of points,
//   hence InsertPoints() must be called in time.
//   All methods return false if something went wrong.
//
// Author: Michael Matus, 2017
//   1.8	first working version
//   1.9	massive code refactoring
//
//*******************************************************************************************


using Bev.Bev3Dlib; //TODO this will reside on a different place in the future!!
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Bev.IO.VtkWriter
{
    public class VtkWriter
    {
        private readonly StringBuilder sb;               // the place for the complete file contents
        private readonly StringBuilder sbHeader;         // the file header
        private readonly StringBuilder sbPointData;      // coordinates of the points
        private readonly StringBuilder sbCellData;       // connectivity/topology of the points
        private readonly StringBuilder sbPointDataSets;  // decoration of the points
        private readonly StringBuilder sbCellDataSets;   // decoration of the cells
        private DataSetType dataSetType;        // The type of the vtk-file data structure
        private int? numberPoints;              // number of data points (derived from DATAPOINTS)
        private int? numberPointsFromGrid;      // number of data points (derived from STRUCTURED_GRID)

        public bool ForceDefaultFileExtension { get; set; } // If true forces the ".vtk" extension for the output file name.

        public VtkWriter(string title)
        {
            // I am not completely confident on this, but it works.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            ForceDefaultFileExtension = true;
            dataSetType = DataSetType.Unknow;
            sb = new StringBuilder();
            sbHeader = new StringBuilder();
            sbPointData = new StringBuilder();
            sbCellData = new StringBuilder();
            sbPointDataSets = new StringBuilder();
            sbCellDataSets = new StringBuilder();
            numberPoints = null;
            numberPointsFromGrid = null;
            // write first part of the legacy VTK header
            sb.AppendLine("# vtk DataFile Version 3.1"); // or 1.0, 2.0, 3.0, 3.1 ?
            sb.AppendLine(TrimTitle(title));
            sb.AppendLine("ASCII");
        }

        public void WriteToFile(string outFileName)
        {
            // check if data present
            if (string.IsNullOrWhiteSpace(GetFileContent()))
                return;
            // change file name extension
            string fileName = outFileName;
            if (ForceDefaultFileExtension)
                fileName = Path.ChangeExtension(outFileName, ".vtk");
            // write the file
            using (StreamWriter hOutFile = File.CreateText(fileName))
            {
                hOutFile.Write(GetFileContent());
            }            
        }

        public string GetFileContent()
        {
            if (CombineSection())
                return sb.ToString();
            else
                return "";
        }

        #region Step 0: Define Dataset Format (Header)
        public bool HeaderPolydataFormat()
        {
            dataSetType = DataSetType.POLYDATA;
            return GenericDatasetFormat(dataSetType, "");
        }

        public bool HeaderStructuredGridFormat(int xDimension, int yDimension, int zDimension)
        {
            numberPointsFromGrid = xDimension * yDimension * zDimension;
            dataSetType = DataSetType.STRUCTURED_GRID;
            string parameter = $"DIMENSIONS {xDimension} {yDimension} {zDimension}";
            return GenericDatasetFormat(dataSetType, parameter);
        }

        public bool HeaderStructuredGridFormat(int xDimension, int yDimension) => HeaderStructuredGridFormat(xDimension, yDimension, 1);

        #endregion

        // Step 1: Insert point data in file
        public bool InsertPoints(List<Point> points) => InsertPoints(points.ToArray());

        // Step 1: Insert point data in file
        public bool InsertPoints(Point[] points)
        {
            if (sbPointData.Length != 0)
                return false; // points allready inserted
            if (points.Length == 0)
                return false;   // no points provided
            numberPoints = points.Length;
            sbPointData.AppendLine($"POINTS {numberPoints} DOUBLE");
            foreach (var p in points)
                sbPointData.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F10} {1:F10} {2:F10}", p.X, p.Y, p.Z));
            sbPointData.AppendLine();
            return true;
        }

        #region Step 2: Generate and insert topology data.
        // Topology (connectivity) for a hemisphere grid. Order of data points as delivered by the NMM-1 in 3D measurement mode. 
        // numberPoints must be known when calling, otherwise the method will fail.
        public bool BuildHemiSphere(int nTheta, int mPhi)
        {
            if (nTheta * mPhi + 1 != numberPoints)
                return false;
            // if(dataSetType != DataSetType.POLYDATA) return false;  // optimal for surface data, Unstructured_grid may also work
            sbCellData.AppendLine($"POLYGONS {nTheta * mPhi} {mPhi * (4 + 5 * (nTheta - 1))}");
            // triangles
            for (int i = 1; i <= mPhi; i++)
                sbCellData.AppendLine($"3 0 {i} {(i % mPhi) + 1}");
            // quadrangles
            int a, b, c, d;
            for (int j = 2; j <= nTheta; j++)
            {
                for (int i = 1; i <= mPhi; i++)
                {
                    a = i + (j - 2) * mPhi;
                    b = i + (j - 1) * mPhi;
                    c = b + 1;
                    d = a + 1;
                    if (i == mPhi) { c -= mPhi; d -= mPhi; }
                    sbCellData.AppendLine($"4 {a} {b} {c} {d}");
                }
            }
            sbCellData.AppendLine();
            return true;
        }
        #endregion

        // Step 3: Decorate points with scalars or vectors
        public bool PointAttributes(List<double> scalarField, string title) => PointAttributes(scalarField.ToArray(), title);

        // Step 3: Decorate points with scalars or vectors
        public bool PointAttributes(double[] scalarField, string title)
        {
            if (scalarField.Length != numberPoints)
                return false; // number must match the number of points
            if (sbPointDataSets.Length == 0)
                sbPointDataSets.AppendLine("POINT_DATA " + numberPoints); // must only occur once!
            title = title.Trim().Replace(' ', '_');
            sbPointDataSets.AppendLine("SCALARS " + title + " DOUBLE");
            sbPointDataSets.AppendLine("LOOKUP_TABLE custom_table");
            foreach (var d in scalarField)
                sbPointDataSets.AppendLine(d.ToString("F10", CultureInfo.InvariantCulture));
            sbPointDataSets.AppendLine();
            return true;
        }

        // Step 3: Decorate points with scalars or vectors
        public bool PointAttributes(List<Vector> vectorField, string title) => PointAttributes(vectorField.ToArray(), title);

        // Step 3: Decorate points with scalars or vectors
        public bool PointAttributes(Vector[] vectorField, string title)
        {
            if (vectorField.Length != numberPoints)
                return false; // number must match the number of points
            if (sbPointDataSets.Length == 0)
                sbPointDataSets.AppendLine("POINT_DATA " + numberPoints); // must only occur once!
            title = title.Trim().Replace(' ', '_');
            sbPointDataSets.AppendLine("VECTORS " + title + " DOUBLE");
            foreach (var v in vectorField)
                sbPointDataSets.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F10} {1:F10} {2:F10}", v.X, v.Y, v.Z));
            sbPointDataSets.AppendLine();
            return true;
        }

        private bool GenericDatasetFormat(DataSetType dst, string optionalTextLine)
        {
            if (sbHeader.Length != 0)
                return false; // only a single header allowed
            if (dst == DataSetType.Unknow)
                return false; // a data set type must be defined
            sbHeader.AppendLine($"DATASET {dataSetType}");
            if (optionalTextLine != "")
                sbHeader.AppendLine(optionalTextLine);
            sbHeader.AppendLine();
            return true;
        }

        private bool CombineSection()
        {
            if (sbHeader.Length == 0)
                return false;      // header is necessary
            if (sbPointData.Length == 0)
                return false;   // point data is necessary, too
            if (numberPointsFromGrid != null)
                if (numberPointsFromGrid != numberPoints)
                    return false; // inconsitency found
            sb.Append(sbHeader.ToString());
            sb.Append(sbPointData.ToString());
            sb.Append(sbCellData.ToString());
            sb.Append(sbPointDataSets.ToString());
            sb.Append(sbCellDataSets.ToString());
            return true;
        }

        // Ensures the length of the string to be shorter than 255 characters.
        private string TrimTitle(string title)
        {
            title = title.Trim();
            if(string.IsNullOrWhiteSpace(title))
                title = "<no title provided>";
            if (title.Length <= 254)
                return title;
            return title.Substring(0, 250) + " ...";
        }
    }
}
