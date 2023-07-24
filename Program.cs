using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;


using Spiricon.Automation;
using Aerotech.A3200;

namespace BeamGageAutomation
{
    class Program
    {
        static void Main(string[] args)
        {
            // Connect to the two software APIs using custom connector classes.
            // Aerotech A3200
            A3200Connector A3200Connection = new A3200Connector();
            A3200Connection.Connect();

            // BeamGage
            BeamGageConnector BGConnection = new BeamGageConnector();
            BGConnection.Connect();
            
            // Print instructions to console
            Console.WriteLine("Set up the beam camera as usual.\n");

            Console.WriteLine("Set grid parameters in GridConfiguration.txt.");
            Console.WriteLine("Line format: <VariableName = value>\n");

            Console.WriteLine("Enter the filename for the exported calibration file.");
            string Filename = Console.ReadLine();
            Console.WriteLine("After completion, the CSV-file will be saved as " + Filename + ".csv on the desktop and can be imported to the Aerotech Calibration File Converter.\n" );

            Console.WriteLine("The current location will be the zero point of the calibration.");
            Console.WriteLine("Type start to start the calibration and cancel to terminate the program.");
            
            // Loop for command parsing
            string answer = Console.ReadLine().ToLower();
            while (true)
            {
                
                if (answer == "start")
                {
                    // Typed "start" -> exit loop & continue with program
                    break;
                }
                else if (answer == "cancel")
                {
                    // Typed "cancel" -> Disconnect everything and terminate the program
                    A3200Connection.Disconnect();
                    BGConnection.Disconnect();
                    Environment.Exit(0);
                }
                else
                {
                    // Typed invalid command -> try again and repeat loop.
                    Console.WriteLine("Invalid command! Set up the beam camera as usual. Type start to start the calibration and cancel to terminate the program.");
                    answer = Console.ReadLine().ToLower();
                }
            }
            
            try
            {
                CalibrationProgram Calibration = new CalibrationProgram(BGConnection, A3200Connection);
                Calibration.RunProgram();
                string FilenameAerotech = Filename + "_aerotech";
                string FilenameCalFileMaker = Filename + "_CalibrationFileMaker";
                string FilenameDataFile = Filename + "_Data";
                string FilenameSystemConfiguration = Filename + "_SystemConfiguration";
                Export.ToNmark(Calibration.Results, FilenameAerotech);
                Export.ToCalibrationFileMaker(Calibration.Results, Calibration.IdealPositions, FilenameCalFileMaker);
                Export.ToDatafile(Calibration.Results, Calibration.IdealPositions, FilenameDataFile);
                Export.ToSystemConfiguration(A3200Connection, FilenameSystemConfiguration);
            }
            finally
            {
                // Disconnect from A3200 controller and BeamGage even when CalibrationProgram produced an error.
                A3200Connection.Disconnect();
                BGConnection.Disconnect();
                Console.WriteLine("Press enter to close the program.");
                Console.ReadLine();
            }
        }
    }

    public class BeamGageConnector
    {
        /**
        Class to handle the connection to BeamGage and execute the necessary commands in a simplified way.
        **/

        
        private AutomatedBeamGage bgClient; // Declare the BeamGage Automation client.
        
        private bool MeasureOn = false; // Switch to control data collection.
        private List<double> ResultX = new List<double>(); // Dynamic lists to store the measured Centroid coordinates.
        private List<double> ResultY = new List<double>();
        private List<double> ResultD = new List<double>();
        private List<double> ResultIntensity = new List<double>();

        public void Connect()
        {
            /**
            Starts a new BeamGage automation client, lets user know over the console and shows the GUI. 
            Registeres the NewFrameEvent callback function.
            **/
            bgClient = new AutomatedBeamGage("ScannerCalibration", true);
            new AutomationFrameEvents(bgClient.ResultsPriorityFrame).OnNewFrame += OnFrameFunction; // Register callback function
            Console.WriteLine("BeamGage connected.");
        }
        
        private void OnFrameFunction()
        {
            /**
            Callback function when BeamGage captures a new frame.
            **/

            // Data collection switch is on -> Add centroid coordinates of the new frame to the results list.
            if (MeasureOn)
            {
                ResultX.Add(bgClient.SpatialResults.PeakLocationX/1000); // in mm
                ResultY.Add(bgClient.SpatialResults.PeakLocationY/1000);
                //ResultD.Add(bgClient.SpatialResults.KnifeEdgeDiameter_16_84/1000); // in mm
                ResultD.Add(bgClient.SpatialResults.PercentOfPeakDiameter/1000); // in mm
                ResultIntensity.Add(bgClient.PowerEnergyResults.Peak); // in cts
            }
        }

        public void Disconnect()
        {
            /**
            Disconnects the BeamGage automation client.
            **/
            bgClient.Instance.Shutdown();
            Console.WriteLine("BeamGage disconnected.");
        }

        public double[] Measure(int MillisecondsDuration)
        {
            ///Controls the measurement routine. Duration is in seconds.
            Console.WriteLine("Measurement started.");
            MeasureOn = true;
            Thread.Sleep(MillisecondsDuration);
            MeasureOn = false;
            Console.WriteLine("Measurement ended.");
           
            double[] Result = {ResultX.Average(), ResultY.Average(), ResultD.Average(), ResultIntensity.Average()};
            Console.WriteLine("Measured position: g = {0:F5} mm, h = {1:F5} mm", Result[0], Result[1]);
            Console.WriteLine("Measured diametre: {0:F5} mm", Result[2]);
            Console.WriteLine("Measured peak intensity: {0:F5} cts", Result[2]);
            
            ResultX.Clear();
            ResultY.Clear();
            ResultD.Clear();
            ResultIntensity.Clear();

            return Result;
        }
    }

    public class A3200Connector
    {
        /**
        Class to handle the connection to Aerotech A3200. And execute the necessary commands in a simplified way.
        **/
        private Controller controller; // Declare controller handle.

        public int[] AxisIndicesUV {
            get {return new int[] {
                controller.Information.Axes["U"].Number,
                controller.Information.Axes["V"].Number};
                }
        }

        public int[] ReverseMotionUV {
            get {return new int[] {
                controller.Parameters.Axes["U"].Motion.ReverseMotionDirection.Value, controller.Parameters.Axes["V"].Motion.ReverseMotionDirection.Value};
                }
        }

        public double[] CountsPerUnitUV {
            get {
                return new double[] {
                controller.Parameters.Axes["U"].Units.CountsPerUnit.Value,
                controller.Parameters.Axes["V"].Units.CountsPerUnit.Value};
                }
        }

        public int LensNumber {
            get {
                return Convert.ToInt16(controller.Variables.Global.Doubles["lens"].Value);
            }
        }
        public void Connect()
        {
            /**
            Prompts user to initialize the A3200 controller correctly. Afterwards, connects to it.
            **/

            // Loop as long as controller is not initialized
            while(!Controller.IsRunning)
            {
                // User prompt
                Console.WriteLine("Start the Aerotech A3200 Software and initialize / reset the controller.");
                Console.WriteLine("Wait for initialization sequence to finish.");
                Console.WriteLine("Press enter to continue.");
                Console.ReadLine();
            }
            controller = Controller.Connect(); // Connect after initialization
            controller.Commands.Motion.Enable(new string[] {"X", "Y", "U", "V"}); // Enable necessary axes.
            controller.Commands.Motion.Home(new string[] {"U", "V"}); // Home U and V
            Console.WriteLine("Controller connected.");
        }

        public void Disconnect()
        {
            /**
            Disconnects from A3200 controller and does some clean up.
            **/
            Controller.Disconnect();
            controller.Dispose();
            Console.WriteLine("Controller disconnected.");
        }

        public void SetZero()
        {
            /**
            Set the current position as the zero point of the machine coordinate system.
            **/
            controller.Commands.Motion.Setup.PosOffsetSet(new string[]{"X", "Y", "U", "V"}, new double[]{0, 0, 0, 0});
        }

        public void MoveToAbsXY(double XCoord, double YCoord)
        {
            controller.Commands.Motion.Setup.Absolute();
            controller.Commands.Motion.Linear(new string[] {"X", "Y"}, new double[] {XCoord, YCoord}, 10);
            WaitMotionDone(new string[] {"X", "Y"});
            Thread.Sleep(500); // milliseconds of settling time for axis vibration.
        }
        public void MoveToAbsUV(double UCoord, double VCoord)
        {
            controller.Commands.Motion.Setup.Absolute();
            controller.Commands.Motion.Linear(new string[] {"U", "V"}, new double[] {UCoord, VCoord}, 10);
            WaitMotionDone(new string[] {"U", "V"});
            Thread.Sleep(500); // milliseconds of settling time for axis vibration.
        }
        public void MoveToAbsXYUV(double UCoord, double VCoord)
        {
            /**
            Move optical and mechanical axes to the measurement point in absolute machine coordinates.
            Arguments are the coordinates in the optical system.
            **/
            controller.Commands.Motion.Setup.Absolute();
            controller.Commands.Motion.Linear(new string[] {"U", "V", "X", "Y"}, new double[] {UCoord, VCoord, -UCoord, -VCoord}, 10);
            WaitMotionDone(new string[] {"X", "Y", "U", "V"});
            Thread.Sleep(500); // milliseconds of settling time for axis vibration.
        }
        public void WaitMotionDone(string[] axes)
        {
            controller.Commands.Motion.WaitForMotionDone(Aerotech.A3200.Commands.WaitOption.InPosition, axes);
        }
    }

    public class CalibrationProgram
    {
        /**
        Class which contains the functionality of the Aerotech measurement program.
        **/
        public double [,,] Results; // Declare result matrix as public class property

        public double [,,] IdealPositions; // Declare matrix for ideal positions
        

        // Declare geometric variables as class properties
        private static int NumU;
        private static int NumV;
        public static double DeltaU;
        public static double DeltaV;


        // Declare measurement duration as class property
        private static int MeasureDuration;


        // Declare handles for program connectors
        private BeamGageConnector BeamGage;
        private A3200Connector Aerotech;
        
        public CalibrationProgram(BeamGageConnector BeamGageConnector, A3200Connector AerotechConnector)
        {
            /**
            Constructor initializes the grid and measurement parameters from the configuration file.
            Also initializes the result matrix and the Aerotech & BeamGage connectors.
            **/
            string[,] GridVariables = GetGridVariables();
            NumU = GetVariable<int>("NumU", GridVariables);
            NumV = GetVariable<int>("NumV", GridVariables);
            DeltaU = GetVariable<double>("MillimeterDeltaU", GridVariables);
            DeltaV = GetVariable<double>("MillimeterDeltaV", GridVariables);
            MeasureDuration = GetVariable<int>("MillisecondsMeasureDuration", GridVariables);
            Results = new double[4, NumV, NumU];
            IdealPositions = new double[2, NumV, NumU];
            Aerotech = AerotechConnector;
            BeamGage = BeamGageConnector;
        }

        private string[,] GetGridVariables()
        {
            /**
            Reads the lines contained in < and > in GridConfiguration.txt and reads the variables from them.
            Line format: <VariableName = Value>
            **/

            List<string> lines = new List<string>(); // Dynamic lists for individual lines
            
            // Read out file stream
            using (FileStream fs = File.OpenRead("GridConfiguration.txt"))
            {
                List<char> line = new List<char>(); // Dynamic list for single line
                bool ReadLine = false; // Switch to activate character read out.
                
                // Go through the whole file stream
                while (fs.Position != fs.Length)
                {
                    char character = Convert.ToChar(fs.ReadByte()); // Current character in file stream
                    if (character=='<') // Beginning of line
                    {
                        ReadLine = true; // Starts read out for the next character
                        continue;
                    }
                    else if (character=='>') // End of line
                    {
                        ReadLine = false; // Stop read out for this character
                        lines.Add(new string(line.ToArray())); // Add current line to the list as string
                        line.Clear(); // Clear current line
                        continue;
                    }
                    
                    if (ReadLine)
                    {
                        // Read out is activated -> add current character to current line.
                        line.Add(character);
                    }
                }
            }
            string [,] result = new string[lines.Capacity,2]; // Initialize nx2 array for variable name string and value string
            for (int i=0; i<lines.Count; i++)
            {
                string[] temp = lines[i].Split('='); // Split line string at '=' sign
                result[i,0] = temp[0].Trim(); // Remove leading and trailing whitespaces from substrings
                result[i,1] = temp[1].Trim();
            }
            return result;
        }
    
        private dynamic GetVariable<T>(string name, string[,] variables)
        {
            /**
            Extracts the variable with the given name from the nx2 variables array.
            name must be contained in the 1st column of variables.
            The variables value is returned as type T, which can be either int or double.
            **/
            string value = "";
            for (int i=0; i<variables.GetLength(0); i++) // Search through the first column of variables
            {
                if (variables[i,0] == name) // extracts the value in the 2nd column at the first occurence of "name"
                {
                    value = variables[i,1];
                    break;
                }
            }
            
            if (typeof(T)==typeof(int))
            {
                // Conversion to int
                int result = Convert.ToInt32(value);
                Console.WriteLine(name + " is " + result + " of type " + typeof(int));
                return result;
            }
            else if (typeof(T)==typeof(double))
            {
                // Conversion to double
                double result = Convert.ToDouble(value);
                Console.WriteLine(name + " is " + result + " of type " + typeof(double));
                return result;
            }
            else
            {
                // If no valid data type is given return the value as a string.
                Console.WriteLine(name + " is " + value + " of type " + typeof(string));
                return value;
            }
        }
        
        public void RunProgram()
        {
            /**
            Run the main calibration program at the current position with the current setup.
            **/
            Aerotech.SetZero(); // Set reference at current position
            double SetLength = 0.1;
            double[] BaseVectors = GetBaseCoordinates(SetLength);
            double[,] TransformMatrix = GetCoordinateTransform(BaseVectors, SetLength);
            double ScalingFactor = GetScalingFactor(BaseVectors, SetLength);
            Console.WriteLine("The transformation matrix is:");
            Console.WriteLine("{0:F6}    {1:F6}", TransformMatrix[0,0], TransformMatrix[0,1]);
            Console.WriteLine("{0:F6}    {1:F6}", TransformMatrix[1,0], TransformMatrix[1,1]);
            Console.WriteLine();
            double[] Reference = BeamGage.Measure(MeasureDuration);
            Console.WriteLine();
            
            for (int i=0; i<NumV; i++)
            {
                for (int j=0; j<NumU; j++)
                {
                    // Go through all the measurement positions from top to bottom. Left to right in a zig-zag-pattern.                    
                    int IdxU;
                    if (i%2==0) // Every even row is from left to right
                    {
                        IdxU = j;
                    }
                    else // Every odd row is from right to left
                    {
                        IdxU = NumU-1-j;
                    }
                    
                    int IdxV = i; // All rows from top to bottom
                    Console.WriteLine("IdxV = {0}, IdxU = {1}", IdxV, IdxU);
                    if (IdxU==(NumU-1)/2 && IdxV==(NumV-1)/2)
                    {
                        // If current position is reference position don't measure again
                        Results[0, IdxV, IdxU] = 0.0;
                        Results[1, IdxV, IdxU] = 0.0;
                        Console.WriteLine("Reference position [mm]: dU = 0.00, dV = 0.00\n");
                        continue;
                    }
                    
                    double UCoord = (IdxU-(NumU-1)/2)*DeltaU; // Measure positions in optical coordinates
                    double VCoord = ((NumV-1)/2-IdxV)*DeltaV;

                    IdealPositions[0, IdxV, IdxU] = UCoord;
                    IdealPositions[1, IdxV, IdxU] = VCoord;

                    Aerotech.MoveToAbsXYUV(UCoord, VCoord);

                    double[] Position = BeamGage.Measure(MeasureDuration); // Execute measurement, MeasureDuration in milliseconds
                    Position[0] = Position[0] - Reference[0]; // Measurement position result relative to reference
                    Position[1] = Position[1] - Reference[1];

                    double[] TransformedPosition = {Position[0], Position[1]};
                    TransformedPosition = Calculate2DCoordinateTransform(TransformedPosition, TransformMatrix); // Transform to machine coordinates

                    Results[0, IdxV, IdxU] = TransformedPosition[0]; 
                    Results[1, IdxV, IdxU] = TransformedPosition[1];
                    Results[2, IdxV, IdxU] = Position[2] * ScalingFactor; // Diameter scaled to machine coordinates is an absolute measurement
                    Results[3, IdxV, IdxU] = Position[3] / Reference[3]; // Intensity is a percentage of reference
                    Console.WriteLine("\nDeviation: dU = {0:F5} mm, dV = {1:F5} mm", Results[0, IdxV, IdxU], Results[1, IdxV, IdxU]);
                    Console.WriteLine("Diametre: D = {0:F5} mm", Results[2, IdxV, IdxU]);
                    Console.WriteLine("Relative Peak Intensity: I = {0:F5}\n", Results[3, IdxV, IdxU]);
                }
            }
            Aerotech.MoveToAbsXYUV(0,0);
            ShowResult(); // Print result matrix to console
        }
    
        public void ShowResult()
        {
            /**
            Writes the entries of the result matrix to their according positions in the console.
            U- and V-deviations of individual points are grouped together.
            **/
            Console.WriteLine("Deviation results in mm:\n");
            for (int i=0; i<NumV; i++)
            {
                for (int j=0; j<NumU; j++)
                {
                    Console.Write("({0:F5}, {1:F5})\t", Results[0,i,j], Results[1,i,j]);
                }
                Console.Write("\n\n");
            }
        }

        private double[,] GetCoordinateTransform(double[] BaseVectors, double SetLength)
        {
            /**
            Calculates the transformation matrix from the beam camera coordinates to the machine coordinates.
            The mechanical axes move a distance of 100 μm in positive X- and then 100 μm in positive Y-direction. From the beam positions measured during the movements the transformation matrix is calculated.
            **/

            // X movement
            double X1 = BaseVectors[0];
            double Y1 = BaseVectors[1];
            double X2 = BaseVectors[2];
            double Y2 = BaseVectors[3];

            double a = SetLength*Y2/(X1*Y2-X2*Y1);
            double b = SetLength*X2/(X2*Y1-X1*Y2);
            double c = SetLength*Y1/(Y1*X2-Y2*X1);
            double d = SetLength*X1/(X1*Y2-X2*Y1);
            Console.WriteLine("The transformation matrix is:");
            Console.WriteLine("{0:F6}    {1:F6}", a, b);
            Console.WriteLine("{0:F6}    {1:F6}", c, d);
            return new double[,] {{a, b}, {c, d}};

        }
        private double[] Calculate2DCoordinateTransform(double[] Vector, double[,] Matrix)
        {
            double[] TransformedVector = new double[2];
            TransformedVector[0] = Matrix[0,0]*Vector[0]+Matrix[0,1]*Vector[1];
            TransformedVector[1] = Matrix[1,0]*Vector[0]+Matrix[1,1]*Vector[1];
            return TransformedVector;
        }

        private double[] GetBaseCoordinates(double SetLength)
        {
            /**
            Function to find the components of the machine coordinate base vectors in the base of the beam camera sensor.
            **/
            // X Movement
            Aerotech.SetZero();
            double[] Reference = BeamGage.Measure(MeasureDuration);
            Aerotech.MoveToAbsXY(SetLength, 0);
            double[] Position = BeamGage.Measure(MeasureDuration);
            double X1 = Position[0]-Reference[0]; 
            double Y1 = Position[1]-Reference[1];

            // Y Movement
            Aerotech.MoveToAbsXY(0, SetLength);
            Position = BeamGage.Measure(MeasureDuration);
            double X2 = Position[0]-Reference[0]; 
            double Y2 = Position[1]-Reference[1];

            Aerotech.MoveToAbsXY(0,0);
            return new double[] {X1, Y1, X2, Y2};
        }
        private double GetScalingFactor(double [] BaseVectors, double SetLength)
        {
            double X1 = BaseVectors[0];
            double Y1 = BaseVectors[1];
            double X2 = BaseVectors[2];
            double Y2 = BaseVectors[3];
            double scaling1 = SetLength/(Math.Sqrt(Math.Pow(X1,2)+Math.Pow(Y1,2)));
            double scaling2 = SetLength/(Math.Sqrt(Math.Pow(X2,2)+Math.Pow(Y2,2)));
            
            return (scaling1+scaling2)/2;
        }
    }

    public static class Export
    {
        /**
        Class to export to different parsing systems using different formatting.
        Exports always to the desktop.
        **/
        private static string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); // Always write to desktop
        public static void ToNmark(double[,,] Deviations, string filename)
        {
            /**
            Exports to a csv file in aerotech formatting to create a .gcal file with the Aerotech Galvo Calibration File Converter.
            **/
            string path = DesktopPath + "\\" + filename + ".csv";
            using (StreamWriter OutputFile = new StreamWriter(path))
            {
                int NumU = Deviations.GetLength(2);
                int NumV = Deviations.GetLength(1);

                for (int i=NumV-1; i>=0; i--) // Go through grid from bottom to top
                {
                    for (int j=0; j<NumU; j++) // Go through grid from left to right
                    {
                        if (j<NumU-1)
                        {
                            OutputFile.Write("\"{0:F5},{1:F5},0.00000\",",-Deviations[0,i,j], -Deviations[1,i,j]);
                        }
                        else
                        {
                            OutputFile.Write("\"{0:F5},{1:F5},0.00000\"\n",-Deviations[0,i,j], -Deviations[1,i,j]); // Last entry of line
                        }
                    }
                }
            }
        }

        public static void ToCalibrationFileMaker(double[,,] Deviations, double[,,] Ideal, string filename)
        {
            /**
            Exports a csv file using the Intersection finder formatting to use the Calibration File Maker for inter- and extrapolation.
            **/
            string path = DesktopPath + "\\" + filename + ".csv";
            using(StreamWriter OutputFile = new StreamWriter(path))
            {
                int NumU = Deviations.GetLength(2);
                int NumV = Deviations.GetLength(1);

                OutputFile.WriteLine("1.0"); // Resolution placeholder
                OutputFile.WriteLine("row, column, zero point, Min, Max, X, Y"); // Header

                for (int i=0; i<NumV; i++)
                {
                    for (int j=0; j<NumU; j++)
                    {
                        OutputFile.WriteLine(
                            "{0:F1},{1:F1},{2:F1},0.0,0.0,{3},{4}",
                            i,
                            j,
                            Convert.ToDouble(i==(NumV-1)/2&&j==(NumU-1)/2),
                            Ideal[0,i,j]+Deviations[0,i,j],
                            -Ideal[1,i,j]-Deviations[1,i,j]);
                    }
                }
            }
        }

        public static void ToDatafile(double[,,] Results, double[,,] Ideal, string filename)
        {
            string path = DesktopPath + "\\" + filename + ".csv";
            using(StreamWriter OutputFile = new StreamWriter(path))
            {
                int NumU = Results.GetLength(2);
                int NumV = Results.GetLength(1);

                OutputFile.WriteLine("Row, Column, U_ideal [mm], V_ideal [mm], Deviation U [mm], Deviation V [mm], D_13.5%peak [mm], Relative peak intensity [-]"); // Header

                for (int i=0; i<NumV; i++)
                {
                    for (int j=0; j<NumU; j++)
                    {
                        OutputFile.WriteLine("{0:D}, {1:D}, {2}, {3}, {4}, {5}, {6}, {7}",
                        i, j, Ideal[0,i,j], Ideal[1,i,j], Results[0,i,j], Results[1,i,j], Results[2,i,j], Results[3,i,j]);
                    }
                }
            }
        }

        public static void ToSystemConfiguration(A3200Connector AerotechConnector, string filename)
        {
            string path = DesktopPath + "\\" + filename + ".csv";
            using(StreamWriter OutputFile = new StreamWriter(path))
            {
                OutputFile.WriteLine("XIndex = {0:D}; int", AerotechConnector.AxisIndicesUV[0]);
                OutputFile.WriteLine("YIndex = {0:D}; int", AerotechConnector.AxisIndicesUV[1]);

                OutputFile.WriteLine("XReverseMotion = {0:D}; bool", AerotechConnector.ReverseMotionUV[0]);
                OutputFile.WriteLine("YReverseMotion = {0:D}; bool", AerotechConnector.ReverseMotionUV[1]);

                OutputFile.WriteLine("XCountsPerUnit = {0}; float", AerotechConnector.CountsPerUnitUV[0]);
                OutputFile.WriteLine("YCountsPerUnit = {0]; float", AerotechConnector.CountsPerUnitUV[1]);

                OutputFile.WriteLine("Lens = {0:D}; int", AerotechConnector.LensNumber);

                OutputFile.WriteLine("dX = {0}; float", CalibrationProgram.DeltaU);
                OutputFile.WriteLine("dY = {0}; float", CalibrationProgram.DeltaV);
            }
        }

        
    }
}