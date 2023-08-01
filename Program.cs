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
            Console.WriteLine("==========================================================================");
            Console.WriteLine("\nSet up the beam camera as usual.\n");

            Console.WriteLine("Set grid parameters in GridConfiguration.txt.");
            Console.WriteLine("Line format: <VariableName = value>\n");
            Console.WriteLine("The current location will be the zero point of the calibration grid.\n");
            Console.WriteLine("==========================================================================");

            Console.WriteLine("\nEnter the filename for the exported calibration file.");
            string Filename = Console.ReadLine();
            Console.WriteLine();
            Console.WriteLine("After completion, the CSV-file will be saved on the desktop and can be imported into the Aerotech Calibration File Converter.\n" );
            Console.WriteLine("==========================================================================");
            Console.WriteLine("\nType 'start' to start the calibration or 'cancel' to terminate the program.");
            
            // Loop for command parsing
            
            while (true)
            {
                string answer = Console.ReadLine().ToLower();
                Console.WriteLine();
                Console.WriteLine("==========================================================================\n");
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
                    Console.WriteLine("\nInvalid command! Type 'start' to start the calibration or 'cancel' to terminate the program.");
                }
            }
            
            try
            {
                CalibrationProgram Calibration = new CalibrationProgram(BGConnection, A3200Connection);              
                Calibration.RunProgram();
                string FilenameCalFileMaker = Filename + "_CalibrationFileMaker";
                string FilenameDataFile = Filename + "_Data";
                string FilenameSystemConfiguration = Filename + "_SystemConfiguration";
                Calibration.ExportToCalibrationFileMaker(FilenameCalFileMaker);
                Calibration.ExportDataFile(FilenameDataFile);
                A3200Connection.ExprotSystemConfiguration(FilenameSystemConfiguration);
            }
            finally
            {
                // Disconnect from A3200 controller and BeamGage even when CalibrationProgram produced an error.
                A3200Connection.Disconnect();
                BGConnection.Disconnect();
                Console.WriteLine("\nPress enter to close the program.");
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
        private List<double> ResultEllipticity = new List<double>();

        public void Connect()
        {
            /**
            Starts a new BeamGage automation client, lets user know over the console and shows the GUI. 
            Registeres the NewFrameEvent callback function.
            **/
            bgClient = new AutomatedBeamGage("ScannerCalibration", true);
            new AutomationFrameEvents(bgClient.ResultsPriorityFrame).OnNewFrame += OnFrameFunction; // Register callback function
            bgClient.SaveLoadSetup.LoadSetup("Automated Beam Gage.bgSetup");
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
                ResultX.Add(bgClient.SpatialResults.CentroidX/1000); // in mm
                ResultY.Add(bgClient.SpatialResults.CentroidY/1000);
                ResultD.Add(bgClient.SpatialResults.D4SigmaDiameter/1000); // in mm
                ResultIntensity.Add(bgClient.PowerEnergyResults.Peak); // in cts
                ResultEllipticity.Add(bgClient.SpatialResults.Ellipticity); // [-]
            }
            // TODO Implement measurement rejection using ellipticity and/or peak intensity threshold.
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
            Console.WriteLine("\nMeasurement started.");
            MeasureOn = true;
            Thread.Sleep(MillisecondsDuration);
            MeasureOn = false;
            Console.WriteLine("Measurement ended.");
           
            double[] Result = {ResultX.Average(), ResultY.Average(), ResultD.Average(), ResultIntensity.Average(), ResultEllipticity.Average()};
            Console.WriteLine("\nMeasured position: g = {0:F5} mm, h = {1:F5} mm", Result[0], Result[1]);
            Console.WriteLine("Measured diametre: {0:F5} mm", Result[2]);
            Console.WriteLine("Measured peak intensity: {0:F5} cts", Result[3]);
            Console.WriteLine("Measured ellipticity: {0:F5}\n", Result[4]);
            
            ResultX.Clear();
            ResultY.Clear();
            ResultD.Clear();
            ResultIntensity.Clear();
            ResultEllipticity.Clear();

            return Result;
        }
    }

    public class A3200Connector
    {
        /**
        Class to handle the connection to Aerotech A3200. And execute the necessary commands in a simplified way.
        **/
        private Controller controller; // Declare controller handle.

        private int[] AxisIndicesUV;
        private int[] ReverseMotionUV;
        private double[] CountsPerUnitUV;
        private int LensNumber;

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
            AxisIndicesUV = new int[] {
                controller.Information.Axes["U"].Number,
                controller.Information.Axes["V"].Number
                };
            ReverseMotionUV = new int[] {
                controller.Parameters.Axes["U"].Motion.ReverseMotionDirection.Value,
                controller.Parameters.Axes["V"].Motion.ReverseMotionDirection.Value
                };
            CountsPerUnitUV = new double[]{
                controller.Parameters.Axes["U"].Units.CountsPerUnit.Value,
                controller.Parameters.Axes["V"].Units.CountsPerUnit.Value
                };
            LensNumber = Convert.ToInt16(controller.Variables.Global.Doubles["lens"].Value);
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
        public void ExprotSystemConfiguration(string filename)
        {
            string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); // Always write to desktop
            string path = DesktopPath + "\\" + filename + ".txt";
            using(StreamWriter OutputFile = new StreamWriter(path))
            {
                OutputFile.WriteLine("XIndex = {0:D}; int", AxisIndicesUV[0]);
                OutputFile.WriteLine("YIndex = {0:D}; int", AxisIndicesUV[1]);

                OutputFile.WriteLine("XReverseMotion = {0:D}; bool", ReverseMotionUV[0]);
                OutputFile.WriteLine("YReverseMotion = {0:D}; bool", ReverseMotionUV[1]);

                OutputFile.WriteLine("XCountsPerUnit = {0}; float", CountsPerUnitUV[0]);
                OutputFile.WriteLine("YCountsPerUnit = {0}; float", CountsPerUnitUV[1]);

                OutputFile.WriteLine("Lens = {0:D}; int", LensNumber);

                OutputFile.WriteLine("dX = {0}; float", CalibrationProgram.DeltaU);
                OutputFile.WriteLine("dY = {0}; float", CalibrationProgram.DeltaV);
            }
        }
    }

    public class CalibrationProgram
    {
        /**
        Class which contains the functionality of the Aerotech measurement program.
        **/
        public double [,] Results; // Declare result matrix as public class property

        public double [,] IdealPositions; // Declare matrix for ideal positions
        
        private int[,] Indices;

        // Declare geometric variables as class properties
        private static int NumU;
        private static int NumV;
        public static double DeltaU;
        public static double DeltaV;
        bool CustomGridFlag = false;
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

            while(true)
            {
                Console.WriteLine("\nGenerate new measurement grid?"); 
                Console.WriteLine("Type 'yes' or 'no'.");
                string answer = Console.ReadLine().ToLower();
                Console.WriteLine();
                if (answer == "yes")
                {
                    break;
                }

                else if (answer == "no")
                {
                    CustomGridFlag = true;
                    break;
                }

                else if (answer == "cancel")
                {
                    // Typed "cancel" -> Disconnect everything and terminate the program
                    AerotechConnector.Disconnect();
                    BeamGageConnector.Disconnect();
                    Environment.Exit(0);
                }

                else
                {
                    Console.WriteLine("Invalid command.");
                }
            }

            if (CustomGridFlag)
            {
                while(true)
                {
                    Console.WriteLine("==========================================================================\n");
                    Console.WriteLine("Import measurement coordinates from file path in GridConfiguration.txt?");
                    Console.WriteLine("Type 'yes' or 'no'. When 'no' is chosen, the standard grid file is imported. Type 'cancel' to terminate the program.");
                    string answer = Console.ReadLine().ToLower();
                    Console.WriteLine();
                    if (answer == "no")
                    {
                        try
                        {
                            GetSavedGrid("MeasureCoordinates.csv");
                            break;
                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine("The standard grid file does not exist.");
                            CreateGrid();
                            CustomGridFlag = false;
                            break;
                        }
                        catch (Exception ex) when (!(ex is FileNotFoundException))
                        {
                            Console.WriteLine("Something went wrong when importing the standard grid file. Check the formatting inside the file.");
                        }
                    }
                    else if (answer == "yes")
                    {
                        try
                        {
                            GridVariables = GetGridVariables();
                            string path = GetVariable<string>("CoordinatesFilePath", GridVariables);
                            GetSavedGrid(path);
                            break;
                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine("The file at the given path does not exist. Check if the path is correct. Press enter to retry.");
                            Console.ReadLine();
                            GridVariables = GetGridVariables();

                        }
                        catch (Exception ex) when (!(ex is FileNotFoundException))
                        {
                            Console.WriteLine("Something went wrong during import of the coordinate list. Check if the formatting in the file is correct.");
                            Console.ReadLine();
                        }
                    }
                    else if (answer == "cancel")
                    {
                        // Typed "cancel" -> Disconnect everything and terminate the program
                        AerotechConnector.Disconnect();
                        BeamGageConnector.Disconnect();
                        Environment.Exit(0);
                    }
                    else
                    {
                        Console.WriteLine("Invalid command.");
                    }
                }
            }

            else
            {
                CreateGrid();
            }
            
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
            string [,] result = new string[lines.Count,2]; // Initialize nx2 array for variable name string and value string
            for (int i=0; i<lines.Count; i++)
            {
                string[] temp = lines[i].Split('='); // Split line string at '=' sign
                result[i,0] = temp[0].Trim(); // Remove leading and trailing whitespaces from substrings
                result[i,1] = temp[1].Trim();
            }
            return result;
        }
        private void GetSavedGrid(string filepath)
        {
            if (File.Exists(filepath))
                {
                    List<double[]> PositionList = new List<double[]>();
                    List<int[]> IndexList = new List<int[]>();
                    using(StreamReader InputFile = new StreamReader(filepath))
                    {
                        InputFile.ReadLine(); // Remove header
                        string line;
                        while((line = InputFile.ReadLine()) != null)
                        {
                            if (String.IsNullOrWhiteSpace(line))
                            {
                                continue;
                            }
                            string[] temp = line.Split(',');
                            for (int i=0; i<temp.Length; i++)
                            {
                                temp[i].Trim();
                            }
                            double[] Position = new double[2];
                            int[] Index = new int[3];
                            Index[0] = Convert.ToInt16(Convert.ToDouble(temp[0]));
                            Index[1] = Convert.ToInt16(Convert.ToDouble(temp[1]));
                            Index[2] = Convert.ToInt16(Convert.ToDouble(temp[2]));
                            Position[0] = Convert.ToDouble(temp[3]);
                            Position[1] = Convert.ToDouble(temp[4]);
                            PositionList.Add(Position);
                            IndexList.Add(Index);
                        }
                    }
                    IdealPositions = new double[PositionList.Count,2];
                    Indices = new int[IndexList.Count, 3];
                    for (int i=0; i<PositionList.Count; i++)
                    {
                        IdealPositions[i,0] = PositionList[i][0];
                        IdealPositions[i,1] = PositionList[i][1];

                        Indices[i,0] = IndexList[i][0];
                        Indices[i,1] = IndexList[i][1];
                        Indices[i,2] = IndexList[i][2];
                    }
                Console.WriteLine("Grid points successfully imported.");
                }
            else
                {
                    throw new FileNotFoundException();
                }
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
        private void SaveGrid()
        {
            string path = "MeasureCoordinates.csv";
            using(StreamWriter OutputFile = new StreamWriter(path))
            {
                OutputFile.WriteLine("Row, Column, Zero, U-Coordinate [mm], V-Coordinate [mm]");
                for (int i=0; i<IdealPositions.GetLength(0); i++)
                {
                    OutputFile.WriteLine("{0}, {1}, {2}, {3}, {4}", Indices[i,0], Indices[i,1], Indices[i,2], IdealPositions[i,0], IdealPositions[i,1]);
                }
            }
        }
        public void RunProgram()
        {
            /**
            Run the main calibration program at the current position with the current setup.
            **/
            Results = new double[IdealPositions.GetLength(0), 5];
            Aerotech.SetZero(); // Set reference at current position
            double SetLength = 0.1;
            Console.WriteLine("==========================================================================");
            Console.WriteLine("\nDetermining the coordinate transform between the camera sensor and the mechanical axes.");
            double[] BaseVectors = GetBaseCoordinates(SetLength);
            double[,] TransformMatrix = GetCoordinateTransform(BaseVectors, SetLength);
            double ScalingFactor = GetScalingFactor(BaseVectors, SetLength);
            Console.WriteLine("==========================================================================");
            Console.WriteLine("\nMeasuring reference position");
            double[] Reference = BeamGage.Measure(MeasureDuration);
            
            for (int i=0; i<IdealPositions.GetLength(0); i++)
            {
                int IdxV = Indices[i,0];
                int IdxU = Indices[i,1];
                double UCoord = IdealPositions[i,0];
                double VCoord = IdealPositions[i,1];
                Console.WriteLine("==========================================================================\n");
                Console.WriteLine("IdxV = {0}, IdxU = {1}", Indices[i,0], Indices[i,1]);
                if (Convert.ToBoolean(Indices[i,2]))
                {
                    // If current position is reference position don't measure again
                    Results[i, 0] = 0.0;
                    Results[i, 1] = 0.0;
                    Results[i, 2] = Reference[2]*ScalingFactor;
                    Results[i, 3] = 1.0;
                    Results[i, 4] = Reference[4];
                    Console.WriteLine("Reference position [mm]: dU = 0.00, dV = 0.00");
                    Console.WriteLine("Reference diametre: D = {0:F5} mm", Results[i, 2]);
                    Console.WriteLine("Ellipticity: e = {0:F5}\n", Results[i, 4]);
                    continue;
                }
                Aerotech.MoveToAbsXYUV(UCoord, VCoord);
                double[] Position = BeamGage.Measure(MeasureDuration); // Execute measurement, MeasureDuration in milliseconds
                Position[0] = Position[0] - Reference[0]; // Measurement position result relative to reference
                Position[1] = Position[1] - Reference[1];
                double[] TransformedPosition = {Position[0], Position[1]};
                TransformedPosition = Calculate2DCoordinateTransform(TransformedPosition, TransformMatrix); // Transform to machine coordinates
                Results[i, 0] = TransformedPosition[0]; 
                Results[i, 1] = TransformedPosition[1];
                Results[i, 2] = Position[2] * ScalingFactor; // Diameter scaled to machine coordinates is an absolute measurement
                Results[i, 3] = Position[3] / Reference[3]; // Intensity is a percentage of reference
                Results[i, 4] = Position[4]; // Ellipticity
                // Printing
                Console.WriteLine("Deviation: dU = {0:F5} mm, dV = {1:F5} mm", Results[i, 0], Results[i, 1]);
                Console.WriteLine("Diametre: D = {0:F5} mm", Results[i, 2]);
                Console.WriteLine("Relative Peak Intensity: I = {0:F5}", Results[i, 3]);
                Console.WriteLine("Ellipticity: e = {0:F5}\n", Results[i, 4]);
                
            }
            Console.WriteLine("==========================================================================");
            Aerotech.MoveToAbsXYUV(0,0);
            if (!CustomGridFlag)
            {
                SaveGrid();
            }
            ShowResult(); // Print result matrix to console
            Console.WriteLine("\n==========================================================================\n");
        }
        public void ShowResult()
        {
            /**
            Writes the entries of the result matrix to their according positions in the console.
            U- and V-deviations of individual points are grouped together.
            **/
            Console.WriteLine("Results:\n");
            Console.WriteLine("Row, Column, Zero, U_ideal [mm], V_ideal [mm], Deviation U [mm], Deviation V [mm], D_13.5%peak [mm], Relative peak intensity [-], Ellipticity [-]"); // Header
            for (int i=0; i<IdealPositions.GetLength(0); i++)
            {
                Console.WriteLine("{0:D}, {1:D}, {2:D}, {3}, {4}, {5}, {6}, {7}, {8}",
                Indices[i,0], Indices[i,1], Indices[i,2], IdealPositions[i, 0], IdealPositions[i, 1], Results[i, 0], Results[i, 1], Results[i, 2], Results[i, 3], Results[i,4]);
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
            Console.WriteLine("\nThe transformation matrix is:");
            Console.WriteLine("{0:F6}    {1:F6}", a, b);
            Console.WriteLine("{0:F6}    {1:F6}\n", c, d);
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

            double scaling = (scaling1+scaling2)/2;
            Console.WriteLine("\nThe scaling factor is: {0}\n", scaling);
            
            return scaling;
        }
        private void CreateGrid()
        {
            IdealPositions = new double[NumU*NumV, 2];
            Indices = new int[NumU*NumV, 3];
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
                    
                    double UCoord = (IdxU-(NumU-1)/2)*DeltaU; // Measure positions in optical coordinates
                    double VCoord = ((NumV-1)/2-IdxV)*DeltaV;

                    Indices[i*NumV+j, 0] = IdxV; // Row
                    Indices[i*NumV+j, 1] = IdxU; // Column
                    Indices[i*NumV+j, 2] = Convert.ToInt16(IdxU==(NumU-1)/2 && IdxV==(NumV-1)/2); // Reference bool

                    IdealPositions[i*NumV+j, 0] = UCoord; // U Coordinate
                    IdealPositions[i*NumV+j, 1] = VCoord; // V Coordinate
                }
            }
            Console.WriteLine("New measurement grid created.\n");
        }
        public void ExportToCalibrationFileMaker(string filename)
        {
            /**
            Exports a csv file using the Intersection finder formatting to use the Calibration File Maker for inter- and extrapolation.
            **/
            string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); // Always write to desktop
            string path = DesktopPath + "\\" + filename + ".csv";
            using(StreamWriter OutputFile = new StreamWriter(path))
            {
                OutputFile.WriteLine("1.0"); // Resolution placeholder
                OutputFile.WriteLine("row, column, zero point, Min, Max, X, Y"); // Header

                for (int i=0; i<IdealPositions.GetLength(0); i++)
                {
                    OutputFile.WriteLine(
                        "{0},{1},{2},0.0,0.0,{3},{4}",
                        Indices[i,0],
                        Indices[i,1],
                        Indices[i,2],
                        IdealPositions[i, 0]+Results[i, 0],
                        -IdealPositions[i, 1]-Results[i, 1]);
                    }
                }
            }
        public void ExportDataFile(string filename)
        {
            string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); // Always write to desktop
            string path = DesktopPath + "\\" + filename + ".csv";
            using(StreamWriter OutputFile = new StreamWriter(path))
            {
                OutputFile.WriteLine("Row, Column, Zero, U_ideal [mm], V_ideal [mm], Deviation U [mm], Deviation V [mm], D_13.5%peak [mm], Relative peak intensity [-], Ellipticity [-]"); // Header
                for (int i=0; i<IdealPositions.GetLength(0); i++)
                {
                    OutputFile.WriteLine("{0:D}, {1:D}, {2:D}, {3}, {4}, {5}, {6}, {7}, {8}",
                    Indices[i,0], Indices[i,1], Indices[i,2], IdealPositions[i, 0], IdealPositions[i, 1], Results[i, 0], Results[i, 1], Results[i, 2], Results[i, 3], Results[i,4]);
                }
            }
        }
        private void PrintArray(int[,] array)
        {
            for (int i=0; i<array.GetLength(0); i++)
            {
                for (int j=0; j<array.GetLength(1); j++)
                {
                    Console.Write("{0}\t", array[i,j]);
                }
                Console.Write("\n");
            }
        }
        private void PrintArray(double[,] array)
        {
            for (int i=0; i<array.GetLength(0); i++)
            {
                for (int j=0; j<array.GetLength(1); j++)
                {
                    Console.Write("{0}\t", array[i,j]);
                }
                Console.Write("\n");
            }
        }
    }
}