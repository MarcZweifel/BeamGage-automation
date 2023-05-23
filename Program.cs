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
            }
            finally
            {
                // Error during program execution leads to disconnect of everything to allow for a clean restart.
                A3200Connection.Disconnect();
                BGConnection.Disconnect();
            }

            // Disconnect everything after program execution.
            A3200Connection.Disconnect();
            BGConnection.Disconnect();
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
                ResultX.Add(bgClient.SpatialResults.CentroidX);
                ResultY.Add(bgClient.SpatialResults.CentroidY);
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

        public double[] MeasurePosition(int MillisecondsDuration)
        {
            ///Controls the measurement routine. Duration is in seconds.
            Console.WriteLine("Measurement started.");
            MeasureOn = true;
            Thread.Sleep(MillisecondsDuration);
            MeasureOn = false;
            Console.WriteLine("Measurement ended.");
           
            double[] Result = {ResultX.Average(), ResultY.Average()};
            Console.WriteLine("X = {0:F2}, Y = {1:F2}", Result[0], Result[1]);
            
            ResultX.Clear();
            ResultY.Clear();

            return Result;
        }
    }

    public class A3200Connector
    {
        /**
        Class to handle the connection to Aerotech A3200. And execute the necessary commands in a simplified way.
        **/
        private Controller controller; // Declare controller handle.
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
            controller.Commands.Motion.Linear(new string[] {"U", "V"}, new double[] {0, 0}, 100); // Go to U0 V0
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

        public void MoveToAbs(double UCoord, double VCoord)
        {
            /**
            Move optical and mechanical axes to the measurement point in absolute machine coordinates.
            Arguments are the coordinates in the optical system.
            **/
            controller.Commands.Motion.Setup.Absolute();
            controller.Commands.Motion.Linear(new string[] {"U", "V", "X", "Y"}, new double[] {UCoord, VCoord, -UCoord, -VCoord}, 20);
            Thread.Sleep(500); // milliseconds of settling time for axis vibration.
        }
    }

    public class CalibrationProgram
    {
        /**
        Class which contains the functionality of the Aerotech measurement program.
        **/
        public double [,,] Results; // Declare result matrix as public class property
        

        // Declare geometric variables as class properties
        private static int NumU;
        private static int NumV;
        private static double DeltaU;
        private static double DeltaV;


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
            Results = new double[2, NumV, NumU];
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
            double[] Reference = BeamGage.MeasurePosition(MeasureDuration);
            
            for (int i=0; i<NumV; i++)
            {
                for (int j=0; j<NumU; j++)
                {
                    // Go through all the measurement positions from top to bottom. Left to right in a zig-zag-pattern.
                    // TODO Continue commenting here.
                    int IdxU;
                    if (i%2==0)
                    {
                        IdxU = j;
                    }
                    else
                    {
                        IdxU = NumU-1-j;
                    }
                    
                    int IdxV = i;
                    Console.WriteLine("IdxV = {0}, IdxU = {1}", IdxV, IdxU);
                    if (IdxU==(NumU-1)/2 && IdxV==(NumV-1)/2)
                    {
                        // If current position is reference position don't measure again
                        Results[0, IdxV, IdxU] = 0.0;
                        Results[1, IdxV, IdxU] = 0.0;
                        continue;
                    }
                    
                    double UCoord = (IdxU-(NumU-1)/2)*DeltaU;
                    double VCoord = ((NumV-1)/2-IdxV)*DeltaV;
                    Aerotech.MoveToAbs(UCoord, VCoord);

                    double[] Position = BeamGage.MeasurePosition(MeasureDuration);

                    Results[0, IdxV, IdxU] = Position[0]-Reference[0];
                    Results[1, IdxV, IdxU] = Position[1]-Reference[1];
                }
            }
            ShowResult();
        }
    
        public void ShowResult()
        {
            for (int i=0; i<NumV; i++)
            {
                for (int j=0; j<NumU; j++)
                {
                    Console.Write("({0:F2}, {1:F2})\t", Results[0,i,j], Results[1,i,j]);
                }
                Console.Write("\n\n");
            }
        }
    }

}